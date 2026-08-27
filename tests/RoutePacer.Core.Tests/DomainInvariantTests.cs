using FluentAssertions;
using RoutePacer.Core.Domain;
namespace RoutePacer.Core.Tests;

public sealed class DomainInvariantTests
{
    [Fact]
    public void RouteTrack_rejects_non_monotonic_distance()
    {
        var id = Guid.NewGuid(); var points = new[] { Point(id, 0, 0), Point(id, 1, 100), Point(id, 2, 99) };
        var act = () => new RouteTrack(Summary(id, points.Length), points);
        act.Should().Throw<ArgumentException>().WithMessage("*strictly increasing cumulative distance*");
    }
    [Fact]
    public void RouteTrack_reports_timing_only_when_every_point_is_timed()
    {
        var id = Guid.NewGuid(); var points = new[] { Point(id, 0, 0, 0), Point(id, 1, 100, 10), Point(id, 2, 200, null) };
        new RouteTrack(Summary(id, points.Length), points).HasTiming.Should().BeFalse();
    }
    private static RoutePoint Point(Guid id, int index, double distance, double? elapsed = 1) => new(id, index, 51, -0.1 + index * 0.001, null, distance, elapsed, null);
    private static RouteSummary Summary(Guid id, int count) => new(id, "Test", RouteSourceType.Gpx, DateTimeOffset.UtcNow, 200, null, count, 51, -0.1, 51, -0.098);
}
