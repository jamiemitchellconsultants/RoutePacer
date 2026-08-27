using FluentAssertions;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Import;
namespace RoutePacer.Core.Tests;

public sealed class RouteNormalizerTests
{
    [Fact]
    public void Normalizer_drops_consecutive_duplicates_and_builds_cumulative_distance()
    {
        var raw = new[] { P(51, 0, 0), P(51, 0.001, 10), P(51, 0.001, 10), P(51, 0.002, 20) };
        var route = new RouteNormalizer().Normalize(Guid.NewGuid(), "Morning loop", RouteSourceType.Gpx, DateTimeOffset.UtcNow, raw);
        route.Points.Should().HaveCount(3); route.Summary.TotalDistanceMeters.Should().BeGreaterThan(100); route.HasTiming.Should().BeTrue();
    }
    [Fact]
    public void Normalizer_degrades_partial_timing_to_distance_only()
    {
        var raw = new[] { P(51, 0, null), P(51, 0.001, 1), P(51, 0.002, 2) };
        var route = new RouteNormalizer().Normalize(Guid.NewGuid(), "Untimed", RouteSourceType.Gpx, DateTimeOffset.UtcNow, raw);
        route.HasTiming.Should().BeFalse(); route.Summary.TotalDurationSeconds.Should().BeNull();
    }
    [Fact]
    public void Normalizer_derives_elapsed_seconds_from_timestamps()
    {
        var start = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        var raw = new[] { T(0, start), T(0.001, start.AddSeconds(10)), T(0.002, start.AddSeconds(25)) };

        var route = new RouteNormalizer().Normalize(Guid.NewGuid(), "Timed", RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, raw);

        route.HasTiming.Should().BeTrue();
        route.Points.Select(p => p.ElapsedSeconds).Should().Equal(0, 10, 25);
        route.Summary.TotalDurationSeconds.Should().Be(25);
    }

    [Fact]
    public void Normalizer_degrades_non_monotonic_timing_for_the_whole_track()
    {
        var start = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        var raw = new[] { T(0, start), T(0.001, start.AddSeconds(30)), T(0.002, start.AddSeconds(10)) };

        var route = new RouteNormalizer().Normalize(Guid.NewGuid(), "Backwards", RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, raw);

        route.HasTiming.Should().BeFalse();
        route.Points.Should().OnlyContain(p => p.ElapsedSeconds == null);
        route.Summary.TotalDurationSeconds.Should().BeNull();
    }

    [Fact]
    public void Normalizer_builds_a_bounding_box_and_cumulative_distance()
    {
        var raw = new[] { P(0, 0, 0), P(0.001, 0.002, 10), P(-0.001, 0.004, 20) };

        var route = new RouteNormalizer().Normalize(Guid.NewGuid(), "Bounds", RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, raw);

        route.Summary.MinLatitude.Should().Be(-0.001);
        route.Summary.MaxLatitude.Should().Be(0.001);
        route.Summary.MinLongitude.Should().Be(0);
        route.Summary.MaxLongitude.Should().Be(0.004);
        route.Points[0].DistanceFromStartMeters.Should().Be(0);
        route.Points.Select(p => p.DistanceFromStartMeters).Should().BeInAscendingOrder();
        route.Summary.TotalDistanceMeters.Should().Be(route.Points[^1].DistanceFromStartMeters);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void Normalizer_rejects_invalid_coordinates(double latitude, double longitude)
    {
        var raw = new[] { P(0, 0, 0), P(latitude, longitude, 10), P(0, 0.002, 20) };

        var act = () => new RouteNormalizer().Normalize(Guid.NewGuid(), "Bad", RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, raw);

        act.Should().Throw<RouteImportException>().Which.Code.Should().Be("invalid-coordinate");
    }

    [Fact]
    public void Normalizer_rejects_fewer_than_three_distinct_points()
    {
        var raw = new[] { P(0, 0, 0), P(0, 0, 10), P(0, 0.001, 20) };

        var act = () => new RouteNormalizer().Normalize(Guid.NewGuid(), "Short", RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, raw);

        act.Should().Throw<RouteImportException>().Which.Code.Should().Be("too-few-points");
    }

    [Fact]
    public void Normalizer_requires_a_name()
    {
        var raw = new[] { P(0, 0, 0), P(0, 0.001, 10), P(0, 0.002, 20) };

        var act = () => new RouteNormalizer().Normalize(Guid.NewGuid(), "   ", RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, raw);

        act.Should().Throw<ArgumentException>();
    }

    private static RawRoutePoint P(double latitude, double longitude, double? elapsed) => new(latitude, longitude, null, elapsed, null);
    private static RawRoutePoint T(double longitude, DateTimeOffset timestamp) => new(0, longitude, null, null, timestamp);
}
