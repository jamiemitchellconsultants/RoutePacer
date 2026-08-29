using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using RoutePacer.App.Rides;
using RoutePacer.Core.Domain;

namespace RoutePacer.App.Tests.Rides;

[Trait("Category", "Performance")]
public sealed class LongRideStabilityTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 27, 6, 0, 0, TimeSpan.Zero);
    private const int Seconds = 21_600; // six hours at one fix per second

    [Fact]
    public async Task A_six_hour_ride_persists_every_accepted_fix_and_throttles_the_ui()
    {
        var routes = new InMemoryRouteRepository();
        var rides = new InMemoryRideRepository();
        var location = new FakeLocationService();
        var wakeLock = new FakeWakeLockService();
        var clock = new FakeTimeProvider(Start);
        var track = TrackFixtures.Straight(points: 4_000, metresPerSecond: 10);
        await routes.SaveAsync(track);

        var session = new RideSessionService(routes, rides, location, wakeLock, clock);
        await session.StartAsync();

        var published = 0;
        session.SnapshotChanged += _ => published++;

        // About 5.6 m per second along the route, which the spike filter accepts.
        for (var second = 1; second <= Seconds; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await location.PushAsync(new GeoFix(Start.AddSeconds(second), 0, second * 0.00005, 5, 5.6));
        }

        // Asserted before stopping: every accepted fix is written while the ride runs, which is what
        // makes recovery possible. Stopping then discards all of it.
        rides.Points.Should().HaveCount(Seconds);
        rides.Points.Select(p => p.Sequence).Should().BeInAscendingOrder();
        rides.Points.Select(p => p.Sequence).Should().Equal(Enumerable.Range(0, Seconds).Select(i => (long)i));
        published.Should().BeLessThanOrEqualTo(Seconds * 4, "snapshots are capped at four per second of elapsed time");

        await session.StopAsync();

        session.Snapshot!.Elapsed.TotalSeconds.Should().BeApproximately(Seconds, 1);
        (await rides.GetActiveAsync()).Should().BeNull("a finished ride is not kept");
        rides.Points.Should().BeEmpty();
    }

    [Fact]
    public async Task A_burst_of_fixes_inside_one_window_publishes_once_but_persists_all_of_them()
    {
        var routes = new InMemoryRouteRepository();
        var rides = new InMemoryRideRepository();
        var location = new FakeLocationService();
        var clock = new FakeTimeProvider(Start);
        var track = TrackFixtures.Straight(points: 200, metresPerSecond: 10);
        await routes.SaveAsync(track);

        var session = new RideSessionService(routes, rides, location, new FakeWakeLockService(), clock);
        await session.StartAsync();

        var published = 0;
        session.SnapshotChanged += _ => published++;

        // Twenty fixes 10 ms apart: 200 ms in total, inside a single 250 ms publish window.
        for (var i = 1; i <= 20; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(10));
            await location.PushAsync(new GeoFix(Start.AddMilliseconds(i * 10), 0, i * 0.000002, 5, 0.2));
        }

        rides.Points.Should().HaveCount(20);
        published.Should().BeLessThanOrEqualTo(1);
    }
}
