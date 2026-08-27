using FluentAssertions;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Tracking;

namespace RoutePacer.Core.Tests.Tracking;

public sealed class TrackInterpolatorTests
{
    private static readonly RouteTrack Timed = RouteFixtures.Straight(points: 11, metresPerSecond: 10);
    private static readonly RouteTrack Untimed = RouteFixtures.Straight(points: 11, timed: false);

    [Fact]
    public void Elapsed_at_an_exact_point_returns_that_points_time()
        => TrackInterpolator.ElapsedAtDistance(Timed, Timed.Points[3].DistanceFromStartMeters)
            .Should().BeApproximately(Timed.Points[3].ElapsedSeconds!.Value, 1e-6);

    [Fact]
    public void Elapsed_between_points_is_linear()
    {
        var midpoint = (Timed.Points[3].DistanceFromStartMeters + Timed.Points[4].DistanceFromStartMeters) / 2;
        var expected = (Timed.Points[3].ElapsedSeconds!.Value + Timed.Points[4].ElapsedSeconds!.Value) / 2;

        TrackInterpolator.ElapsedAtDistance(Timed, midpoint).Should().BeApproximately(expected, 1e-6);
    }

    [Fact]
    public void Elapsed_clamps_before_the_start_and_beyond_the_finish()
    {
        TrackInterpolator.ElapsedAtDistance(Timed, -500).Should().Be(0);
        TrackInterpolator.ElapsedAtDistance(Timed, Timed.Summary.TotalDistanceMeters + 500)
            .Should().Be(Timed.Points[^1].ElapsedSeconds);
    }

    [Fact]
    public void Distance_at_an_exact_elapsed_value_returns_that_points_distance()
        => TrackInterpolator.DistanceAtElapsed(Timed, Timed.Points[7].ElapsedSeconds!.Value)
            .Should().BeApproximately(Timed.Points[7].DistanceFromStartMeters, 1e-6);

    [Fact]
    public void Distance_clamps_before_zero_and_beyond_the_finish()
    {
        TrackInterpolator.DistanceAtElapsed(Timed, -10).Should().Be(0);
        TrackInterpolator.DistanceAtElapsed(Timed, 1_000_000).Should().Be(Timed.Points[^1].DistanceFromStartMeters);
    }

    [Fact]
    public void Repeated_elapsed_values_return_the_upper_bracket()
    {
        // A stationary stretch: two points share an elapsed value, so the denominator is zero.
        var id = Guid.NewGuid();
        var points = new List<RoutePoint>
        {
            new(id, 0, 0, 0.000, null, 0, 0, null),
            new(id, 1, 0, 0.001, null, 100, 10, null),
            new(id, 2, 0, 0.002, null, 200, 10, null),
            new(id, 3, 0, 0.003, null, 300, 20, null),
        };
        var route = new RouteTrack(new RouteSummary(id, "r", RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, 300, 20, 4, 0, 0, 0, 0.003), points);

        TrackInterpolator.DistanceAtElapsed(route, 10).Should().Be(200);
    }

    [Fact]
    public void Untimed_routes_return_null_for_both_lookups()
    {
        TrackInterpolator.ElapsedAtDistance(Untimed, 50).Should().BeNull();
        TrackInterpolator.DistanceAtElapsed(Untimed, 50).Should().BeNull();
    }
}
