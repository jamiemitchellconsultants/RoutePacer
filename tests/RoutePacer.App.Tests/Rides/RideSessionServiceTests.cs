using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using RoutePacer.App.Browser;
using RoutePacer.App.Rides;
using RoutePacer.Core.Domain;

namespace RoutePacer.App.Tests.Rides;

public sealed class RideSessionServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryRouteRepository routes = new();
    private readonly InMemoryRideRepository rides = new();
    private readonly FakeLocationService location = new();
    private readonly FakeWakeLockService wakeLock = new();
    private readonly FakeTimeProvider clock = new(Start);
    private readonly RouteTrack track = TrackFixtures.Straight();

    private RideSessionService Create() => new(routes, rides, location, wakeLock, clock);

    private async Task<RideSessionService> Started()
    {
        await routes.SaveAsync(track);
        var session = Create();
        await session.StartAsync(track.Summary.RouteId);
        return session;
    }

    private GeoFix Fix(double seconds, double longitude, double accuracy = 5, double? speed = null)
        => new(Start.AddSeconds(seconds), 0, longitude, accuracy, speed);

    [Fact]
    public async Task Starting_an_unknown_route_throws_and_leaves_the_session_idle()
    {
        var session = Create();

        var act = () => session.StartAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
        session.State.Should().Be(RideSessionState.Idle);
    }

    [Fact]
    public async Task The_ride_is_persisted_before_gps_starts()
    {
        await routes.SaveAsync(track);
        var session = Create();

        await session.StartAsync(track.Summary.RouteId);

        (await rides.ListAsync()).Should().ContainSingle().Which.Status.Should().Be(RideStatus.Running);
        location.StartCount.Should().Be(1);
        session.State.Should().Be(RideSessionState.Running);
    }

    [Fact]
    public async Task A_failing_wake_lock_is_not_fatal()
    {
        await routes.SaveAsync(track);
        wakeLock.AcquireFailure = new InvalidOperationException("no wake lock");
        var session = Create();

        await session.StartAsync(track.Summary.RouteId);

        session.State.Should().Be(RideSessionState.Running);
    }

    [Fact]
    public async Task A_gps_start_failure_finalises_the_ride_as_interrupted()
    {
        await routes.SaveAsync(track);
        location.StartFailure = new InvalidOperationException("denied");
        var session = Create();

        var act = () => session.StartAsync(track.Summary.RouteId);

        await act.Should().ThrowAsync<InvalidOperationException>();
        session.State.Should().Be(RideSessionState.Faulted);
        (await rides.ListAsync()).Single().Status.Should().Be(RideStatus.Interrupted);
    }

    [Fact]
    public async Task Starting_while_active_is_rejected()
    {
        var session = await Started();

        var act = () => session.StartAsync(track.Summary.RouteId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_second_ride_can_start_after_the_first_completes()
    {
        var session = await Started();
        await session.StopAsync();

        await session.StartAsync(track.Summary.RouteId);

        session.State.Should().Be(RideSessionState.Running);
        (await rides.ListAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Every_accepted_fix_is_persisted_even_when_snapshots_are_throttled()
    {
        var session = await Started();
        var published = 0;
        session.SnapshotChanged += _ => published++;

        for (var i = 1; i <= 10; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            await location.PushAsync(Fix(i, i * 0.0002));
        }

        rides.Points.Should().HaveCount(10);
        rides.Points.Select(p => p.Sequence).Should().BeInAscendingOrder();
        published.Should().BeLessThan(10);
    }

    [Fact]
    public async Task A_fix_off_the_route_is_not_persisted()
    {
        var session = await Started();

        await location.PushAsync(new GeoFix(Start.AddSeconds(1), 0.01, 0.001, 5, null));

        rides.Points.Should().BeEmpty();
        session.Snapshot!.Error.Should().Contain("Off route");
    }

    [Fact]
    public async Task Pause_stops_the_watch_and_resume_restarts_it()
    {
        var session = await Started();

        await session.PauseAsync();
        location.Watching.Should().BeFalse();
        wakeLock.ReleaseCount.Should().Be(1);

        await session.ResumeAsync();
        location.Watching.Should().BeTrue();
        location.StartCount.Should().Be(2);
        session.State.Should().Be(RideSessionState.Running);
    }

    [Fact]
    public async Task Paused_time_is_excluded_from_the_recorded_duration()
    {
        var session = await Started();
        clock.Advance(TimeSpan.FromMinutes(10));
        await session.PauseAsync();
        clock.Advance(TimeSpan.FromMinutes(30));
        await session.ResumeAsync();
        clock.Advance(TimeSpan.FromMinutes(5));

        await session.StopAsync();

        (await rides.ListAsync()).Single().DurationSeconds.Should().BeApproximately(TimeSpan.FromMinutes(15).TotalSeconds, 1);
    }

    [Fact]
    public async Task Average_speed_comes_from_accepted_movement_over_moving_time()
    {
        var session = await Started();
        // The first fix only establishes the origin; distance accrues between accepted fixes.
        await location.PushAsync(Fix(0, 0));
        clock.Advance(TimeSpan.FromSeconds(100));
        await location.PushAsync(Fix(100, 0.009));

        await session.StopAsync();

        var ride = (await rides.ListAsync()).Single();
        ride.TotalDistanceMeters.Should().BeApproximately(1001, 5);
        ride.AvgSpeedMps.Should().BeApproximately(ride.TotalDistanceMeters / ride.DurationSeconds, 1e-6);
    }

    [Fact]
    public async Task Invalid_transitions_are_rejected()
    {
        var session = Create();

        await session.Invoking(s => s.PauseAsync()).Should().ThrowAsync<InvalidOperationException>();
        await session.Invoking(s => s.ResumeAsync()).Should().ThrowAsync<InvalidOperationException>();
        await session.Invoking(s => s.StopAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(LocationFailure.Timeout)]
    [InlineData(LocationFailure.Unavailable)]
    [InlineData(LocationFailure.Unknown)]
    public async Task A_transient_gps_failure_does_not_end_the_ride(LocationFailure failure)
    {
        var session = await Started();

        await location.FailAsync(failure);

        session.State.Should().Be(RideSessionState.Running);
        session.Snapshot!.Error.Should().NotBeNullOrWhiteSpace();
        (await rides.ListAsync()).Single().Status.Should().Be(RideStatus.Running);
    }

    [Theory]
    [InlineData(LocationFailure.PermissionDenied)]
    [InlineData(LocationFailure.Unsupported)]
    public async Task A_denied_or_unsupported_capability_ends_the_ride_and_stops_the_browser_services(LocationFailure failure)
    {
        var session = await Started();

        await location.FailAsync(failure);

        session.State.Should().Be(RideSessionState.Faulted);
        location.Watching.Should().BeFalse();
        wakeLock.ReleaseCount.Should().BeGreaterThan(0);
        (await rides.ListAsync()).Single().Status.Should().Be(RideStatus.Interrupted);
    }

    [Fact]
    public async Task A_transient_failure_clears_once_a_fix_is_matched_again()
    {
        var session = await Started();
        await location.FailAsync(LocationFailure.Timeout);

        clock.Advance(TimeSpan.FromSeconds(1));
        await location.PushAsync(Fix(1, 0.0002));

        session.Snapshot!.Error.Should().BeNull();
    }

    [Fact]
    public async Task Wake_lock_status_reaches_the_snapshot()
    {
        var session = await Started();

        wakeLock.Revoke();

        session.Snapshot!.WakeStatus.Should().Be(WakeLockStatus.Revoked);
    }

    [Fact]
    public async Task Recovery_marks_running_and_paused_rides_as_interrupted()
    {
        var routeId = Guid.NewGuid();
        await rides.CreateAsync(new RideSummary(Guid.NewGuid(), routeId, Start, null, RideStatus.Running, 0, 0, 0));
        await rides.CreateAsync(new RideSummary(Guid.NewGuid(), routeId, Start, null, RideStatus.Paused, 0, 0, 0));
        var completed = new RideSummary(Guid.NewGuid(), routeId, Start, Start.AddHours(1), RideStatus.Completed, 10, 10, 1);
        await rides.CreateAsync(completed);

        await Create().RecoverInterruptedAsync();

        var all = await rides.ListAsync();
        all.Where(r => r.RideId != completed.RideId).Should().OnlyContain(r => r.Status == RideStatus.Interrupted && r.EndedAtUtc == Start);
        all.Single(r => r.RideId == completed.RideId).Status.Should().Be(RideStatus.Completed);
    }

    [Fact]
    public async Task Recovery_never_starts_gps_or_requests_a_wake_lock()
    {
        await rides.CreateAsync(new RideSummary(Guid.NewGuid(), Guid.NewGuid(), Start, null, RideStatus.Running, 0, 0, 0));

        await Create().RecoverInterruptedAsync();

        location.StartCount.Should().Be(0);
        wakeLock.AcquireCount.Should().Be(0);
    }
}
