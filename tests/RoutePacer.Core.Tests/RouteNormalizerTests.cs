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
    private static RawRoutePoint P(double latitude, double longitude, double? elapsed) => new(latitude, longitude, null, elapsed, null);
}
