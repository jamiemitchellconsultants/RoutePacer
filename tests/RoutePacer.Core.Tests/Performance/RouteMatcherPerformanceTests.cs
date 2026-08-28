using System.Diagnostics;
using FluentAssertions;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Tracking;

namespace RoutePacer.Core.Tests.Performance;

[Trait("Category", "Performance")]
public sealed class RouteMatcherPerformanceTests
{
    private const int RoutePoints = 250_000;
    private const int Matches = 1_000;

    private static RouteTrack LargeRoute()
    {
        var id = Guid.NewGuid();
        var points = new List<RoutePoint>(RoutePoints);
        var distance = 0d;
        // A gently meandering diagonal spanning 2.5 degrees: continuous, non-degenerate, and strictly increasing.
        static double Latitude(int i) => 0.00001 * i;
        static double Longitude(int i) => 0.00001 * i + 0.000001 * Math.Sin(i / 500d);

        for (var i = 0; i < RoutePoints; i++)
        {
            if (i > 0) distance += GeoMath.HaversineMeters(Latitude(i - 1), Longitude(i - 1), Latitude(i), Longitude(i));
            points.Add(new RoutePoint(id, i, Latitude(i), Longitude(i), null, distance, i, null));
        }
        return new RouteTrack(
            new RouteSummary(id, "Large", RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, distance, RoutePoints - 1, RoutePoints,
                points.Min(p => p.Latitude), points.Min(p => p.Longitude), points.Max(p => p.Latitude), points.Max(p => p.Longitude)),
            points);
    }

    [Fact]
    public void A_quarter_million_point_route_matches_a_thousand_windowed_fixes_quickly()
    {
        var route = LargeRoute();
        var matcher = new RouteMatcher();

        // Warm the matcher and establish a previous index so the window policy applies.
        var previous = matcher.Match(route, Fix(route, 1_000), null)!.SegmentIndex;

        // Thread-scoped, not process-wide. GC.GetTotalAllocatedBytes counts every thread, and xUnit
        // runs collections in parallel, so this budget was being charged for whatever the GPX and
        // FIT parser tests allocated alongside it -- measured at up to 8.4 MB of contamination
        // locally, and enough to breach 25 MiB on a shared runner. The matcher is single-threaded,
        // so the current thread's counter measures it and nothing else.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < Matches; i++)
        {
            var match = matcher.Match(route, Fix(route, 1_000 + i), previous);
            if (match is not null) previous = match.SegmentIndex;
        }
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        allocated.Should().BeLessThan(25 * 1024 * 1024);
    }

    private static GeoFix Fix(RouteTrack route, int index)
    {
        var point = route.Points[index];
        return new GeoFix(DateTimeOffset.UnixEpoch.AddSeconds(index), point.Latitude + 0.00001, point.Longitude, 5, 10);
    }
}
