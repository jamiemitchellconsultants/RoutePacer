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
    private readonly InMemorySettingsRepository settings = new();
    private readonly RouteTrack track = TrackFixtures.Straight();

    private RideSessionService Create() => new(routes, rides, location, wakeLock, settings, clock);

    private async Task<RideSessionService> Started()
    {
        await routes.SaveAsync(track);
        var session = Create();
        await session.StartAsync();
        return session;
    }

    private GeoFix Fix(double seconds, double longitude, double accuracy = 5, double? speed = null)
        => new(Start.AddSeconds(seconds), 0, longitude, accuracy, speed);

    [Fact]
    public async Task Starting_with_no_route_loaded_throws_and_leaves_the_session_idle()
    {
        var session = Create();

        var act = () => session.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        session.State.Should().Be(RideSessionState.Idle);
    }

    [Fact]
    public async Task The_ride_is_persisted_before_gps_starts()
    {
        await routes.SaveAsync(track);
        var session = Create();

        await session.StartAsync();

        (await rides.GetActiveAsync())!.Summary.Status.Should().Be(RideStatus.Running);
        location.StartCount.Should().Be(1);
        session.State.Should().Be(RideSessionState.Running);
    }

    [Fact]
    public async Task A_failing_wake_lock_is_not_fatal()
    {
        await routes.SaveAsync(track);
        wakeLock.AcquireFailure = new InvalidOperationException("no wake lock");
        var session = Create();

        await session.StartAsync();

        session.State.Should().Be(RideSessionState.Running);
    }

    [Fact]
    public async Task A_gps_start_failure_finalises_the_ride_as_interrupted()
    {
        await routes.SaveAsync(track);
        location.StartFailure = new InvalidOperationException("denied");
        var session = Create();

        var act = () => session.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        session.State.Should().Be(RideSessionState.Faulted);
        // A ride that never got GPS is discarded, not kept as a record of a failure.
        (await rides.GetActiveAsync()).Should().BeNull();
        session.State.Should().Be(RideSessionState.Faulted);
    }

    [Fact]
    public async Task Starting_while_active_is_rejected()
    {
        var session = await Started();

        var act = () => session.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_second_ride_can_start_after_the_first_completes()
    {
        var session = await Started();
        await session.StopAsync();

        await session.StartAsync();

        session.State.Should().Be(RideSessionState.Running);
        // The first ride left nothing behind, and the second replaced whatever was there.
        (await rides.GetActiveAsync())!.Summary.Status.Should().Be(RideStatus.Running);
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
    public async Task A_manual_pause_keeps_the_gps_watch_up_because_movement_is_what_ends_it()
    {
        var session = await Started();

        await session.PauseAsync();

        session.State.Should().Be(RideSessionState.Paused);
        session.PauseMode.Should().Be(PauseMode.Manual);
        location.Watching.Should().BeTrue();
        wakeLock.ReleaseCount.Should().Be(0);
    }

    [Fact]
    public async Task Riding_off_ends_a_manual_pause_without_a_tap()
    {
        var session = await Started();
        await location.PushAsync(Fix(10, 0));
        await session.PauseAsync();

        // 0.00015 deg is 16.7 m, past the 15 m resume radius.
        await location.PushAsync(Fix(70, 0.00015));

        session.State.Should().Be(RideSessionState.Running);
        session.PauseMode.Should().Be(PauseMode.None);
    }

    [Fact]
    public async Task Drifting_inside_the_resume_radius_does_not_end_a_pause()
    {
        var session = await Started();
        await location.PushAsync(Fix(10, 0));
        await session.PauseAsync();

        // 0.00011 deg is 12.2 m: past the stationary radius, short of the resume radius.
        await location.PushAsync(Fix(40, 0.00011));
        await location.PushAsync(Fix(70, -0.00011));

        session.State.Should().Be(RideSessionState.Paused);
    }

    [Fact]
    public async Task A_paused_ride_records_no_points_and_accumulates_no_distance()
    {
        var session = await Started();
        await location.PushAsync(Fix(10, 0));
        var pointsAtPause = rides.Points.Count;
        var distanceAtPause = session.Snapshot!.DistanceMeters;
        await session.PauseAsync();

        for (var i = 1; i <= 5; i++) await location.PushAsync(Fix(10 + i * 10, i % 2 == 0 ? 0.00005 : -0.00005));

        rides.Points.Should().HaveCount(pointsAtPause);
        session.Snapshot!.DistanceMeters.Should().Be(distanceAtPause);
    }

    [Fact]
    public async Task Tapping_resume_ends_a_manual_pause_without_restarting_a_watch_that_never_stopped()
    {
        var session = await Started();
        await session.PauseAsync();

        await session.ResumeAsync();

        session.State.Should().Be(RideSessionState.Running);
        session.PauseMode.Should().Be(PauseMode.None);
        location.StartCount.Should().Be(1);
    }

    [Fact]
    public async Task The_snapshot_carries_the_pause_kind_and_how_long_it_has_run()
    {
        var session = await Started();
        await location.PushAsync(Fix(10, 0));
        await session.PauseAsync();

        clock.Advance(TimeSpan.FromSeconds(20));
        await location.PushAsync(Fix(30, 0.00005));

        session.Snapshot!.PauseMode.Should().Be(PauseMode.Manual);
        session.Snapshot!.PausedFor.Should().BeCloseTo(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_recovered_ride_comes_back_suspended_with_no_watch()
    {
        await routes.SaveAsync(track);
        rides.SeedActive(new RideSummary(Guid.NewGuid(), track.Summary.RouteId, Start, null, RideStatus.Running, 500, 300, 0));
        var session = Create();

        await session.RestoreActiveRideAsync();

        session.State.Should().Be(RideSessionState.Paused);
        session.PauseMode.Should().Be(PauseMode.Suspended);
        location.Watching.Should().BeFalse();
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

        session.Snapshot!.Elapsed.TotalSeconds.Should().BeApproximately(TimeSpan.FromMinutes(15).TotalSeconds, 1);
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

        // Read from the final snapshot: the ride itself is cleared on stop and never stored.
        var final = session.Snapshot!;
        final.DistanceMeters.Should().BeApproximately(1001, 5);
        final.Elapsed.TotalSeconds.Should().BeApproximately(100, 1);
        (await rides.GetActiveAsync()).Should().BeNull();
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
        (await rides.GetActiveAsync())!.Summary.Status.Should().Be(RideStatus.Running);
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
        // A ride that never got GPS is discarded, not kept as a record of a failure.
        (await rides.GetActiveAsync()).Should().BeNull();
        session.State.Should().Be(RideSessionState.Faulted);
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
    public async Task Recovery_restores_an_in_progress_ride_as_paused()
    {
        await routes.SaveAsync(track);
        rides.SeedActive(
            new RideSummary(Guid.NewGuid(), track.Summary.RouteId, Start, null, RideStatus.Running, 250, 600, 0),
            new RidePoint(Guid.NewGuid(), 41, Start.AddSeconds(600), 0, 0.002, null, 5, 250, null, null, null));
        clock.Advance(TimeSpan.FromMinutes(30));
        var session = Create();

        await session.RestoreActiveRideAsync();

        // Paused, never Running: resuming starts GPS, and permission is the rider's to grant.
        session.State.Should().Be(RideSessionState.Paused);
        session.Snapshot!.DistanceMeters.Should().BeApproximately(250, 0.001);   // progress along the route, from the last recorded point
        // Elapsed resumes from the last duration actually observed, not from wall-clock: the
        // half hour the app was gone was never measured.
        session.Snapshot!.Elapsed.TotalSeconds.Should().BeApproximately(600, 1);
        session.Snapshot!.SavedPointCount.Should().Be(42);
    }

    [Fact]
    public async Task Recovery_never_starts_gps_or_requests_a_wake_lock()
    {
        await routes.SaveAsync(track);
        rides.SeedActive(new RideSummary(Guid.NewGuid(), track.Summary.RouteId, Start, null, RideStatus.Running, 0, 0, 0));

        await Create().RestoreActiveRideAsync();

        location.StartCount.Should().Be(0);
        wakeLock.AcquireCount.Should().Be(0);
    }

    [Fact]
    public async Task A_recovered_ride_whose_route_was_replaced_is_discarded()
    {
        await routes.SaveAsync(track);
        // Recorded against a route that is no longer loaded: its distances mean nothing now.
        rides.SeedActive(new RideSummary(Guid.NewGuid(), Guid.NewGuid(), Start, null, RideStatus.Running, 100, 100, 1));
        var session = Create();

        await session.RestoreActiveRideAsync();

        session.State.Should().Be(RideSessionState.Idle);
        (await rides.GetActiveAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Nothing_is_restored_when_no_ride_was_in_progress()
    {
        await routes.SaveAsync(track);
        var session = Create();

        await session.RestoreActiveRideAsync();

        session.State.Should().Be(RideSessionState.Idle);
    }

    [Fact]
    public async Task A_pause_freezes_the_ahead_behind_reading_and_not_only_the_elapsed_clock()
    {
        var session = await Started();
        clock.Advance(TimeSpan.FromSeconds(60));
        await location.PushAsync(Fix(60, 0.005));
        var before = session.Snapshot!.Pacing!.DeltaTimeSeconds;

        await session.PauseAsync();
        clock.Advance(TimeSpan.FromMinutes(5));
        await session.ResumeAsync();
        await location.PushAsync(Fix(360, 0.005));

        session.Snapshot!.Pacing!.DeltaTimeSeconds.Should().BeApproximately(before!.Value, 1);
    }

    [Fact]
    public async Task Standing_still_past_the_threshold_pauses_the_ride_when_autopause_is_on()
    {
        settings.AutoPause = new AutoPauseSettings(true, 20);
        var session = await Started();
        await location.PushAsync(Fix(0, 0));

        await location.PushAsync(Fix(25, 0.00005));

        session.State.Should().Be(RideSessionState.Paused);
        session.PauseMode.Should().Be(PauseMode.AutoStationary);
        location.Watching.Should().BeTrue();
    }

    [Fact]
    public async Task Standing_still_changes_nothing_when_autopause_is_off()
    {
        var session = await Started();
        await location.PushAsync(Fix(0, 0));

        await location.PushAsync(Fix(600, 0.00005));

        session.State.Should().Be(RideSessionState.Running);
    }

    [Fact]
    public async Task A_stop_shorter_than_the_threshold_does_not_pause()
    {
        settings.AutoPause = new AutoPauseSettings(true, 60);
        var session = await Started();
        await location.PushAsync(Fix(0, 0));

        await location.PushAsync(Fix(45, 0.00005));

        session.State.Should().Be(RideSessionState.Running);
    }

    [Fact]
    public async Task An_autopaused_ride_resumes_when_the_rider_sets_off()
    {
        settings.AutoPause = new AutoPauseSettings(true, 20);
        var session = await Started();
        await location.PushAsync(Fix(0, 0));
        await location.PushAsync(Fix(25, 0.00005));

        await location.PushAsync(Fix(60, 0.00015));

        session.State.Should().Be(RideSessionState.Running);
        session.PauseMode.Should().Be(PauseMode.None);
    }

    // Holding the watch and the wake lock through a cafe stop is the battery cost a movement-ending
    // pause would otherwise introduce. Five minutes is past any traffic light.
    [Fact]
    public async Task A_long_stop_gives_back_the_gps_watch_and_needs_a_tap_to_come_out_of()
    {
        var session = await Started();
        await location.PushAsync(Fix(0, 0));
        await session.PauseAsync();

        await location.PushAsync(Fix(310, 0.00005));

        session.PauseMode.Should().Be(PauseMode.Suspended);
        location.Watching.Should().BeFalse();
        wakeLock.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public async Task Coming_out_of_a_long_stop_restarts_the_watch_and_forgets_the_stale_fix()
    {
        var session = await Started();
        await location.PushAsync(Fix(0, 0));
        await session.PauseAsync();
        await location.PushAsync(Fix(310, 0.00005));

        await session.ResumeAsync();

        location.StartCount.Should().Be(2);
        location.Watching.Should().BeTrue();
        session.State.Should().Be(RideSessionState.Running);

        // The first fix after the gap is far from the last one seen and must not be read as a spike.
        await location.PushAsync(Fix(320, 0.004));
        rides.Points.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Escalation_counts_from_when_the_rider_stopped_not_from_when_the_pause_began()
    {
        settings.AutoPause = new AutoPauseSettings(true, 20);
        var session = await Started();
        await location.PushAsync(Fix(0, 0));
        await location.PushAsync(Fix(25, 0.00005));
        session.PauseMode.Should().Be(PauseMode.AutoStationary);

        // Five minutes after the rider stopped, not five minutes after the pause was entered.
        await location.PushAsync(Fix(305, 0.00005));

        session.PauseMode.Should().Be(PauseMode.Suspended);
    }

    [Fact]
    public async Task Unreadable_settings_do_not_stop_a_ride_starting()
    {
        await routes.SaveAsync(track);
        var session = new RideSessionService(routes, rides, location, wakeLock, new ThrowingSettingsRepository(), clock);

        await session.StartAsync();

        session.State.Should().Be(RideSessionState.Running);
    }
}
