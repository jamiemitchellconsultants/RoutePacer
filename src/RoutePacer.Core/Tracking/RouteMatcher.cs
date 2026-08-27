using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Tracking;

public sealed class RouteMatcher(RouteMatcherOptions? options = null)
{
    private readonly RouteMatcherOptions _options = options ?? new();

    public MatchedPosition? Match(RouteTrack route, GeoFix fix, int? previousSegmentIndex)
    {
        ArgumentNullException.ThrowIfNull(route); ArgumentNullException.ThrowIfNull(fix);
        var window = previousSegmentIndex.HasValue ? CandidateRange(route.Points.Count - 1, previousSegmentIndex.Value - _options.WindowSegments, previousSegmentIndex.Value + _options.WindowSegments) : (0, route.Points.Count - 2);
        var best = Find(route, fix, window.Item1, window.Item2, previousSegmentIndex);
        if (!previousSegmentIndex.HasValue || best is null || best.CrossTrackErrorMeters > _options.FullScanThresholdMeters)
            best = Find(route, fix, 0, route.Points.Count - 2, previousSegmentIndex);
        return best is not null && best.CrossTrackErrorMeters <= _options.MaximumCrossTrackMeters ? best : null;
    }

    private static (int, int) CandidateRange(int maximum, int start, int end) => (Math.Max(0, start), Math.Min(maximum, end));

    private static MatchedPosition? Find(RouteTrack route, GeoFix fix, int first, int last, int? previous)
    {
        MatchedPosition? best = null; var bestDistance = double.PositiveInfinity;
        for (var i = first; i <= last; i++)
        {
            var p0 = route.Points[i]; var p1 = route.Points[i + 1];
            var a = GeoMath.ToLocalMeters(p0.Latitude, p0.Longitude, fix.Latitude, fix.Longitude);
            var b = GeoMath.ToLocalMeters(p1.Latitude, p1.Longitude, fix.Latitude, fix.Longitude);
            var dx = b.X - a.X; var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0) continue;
            var t = Math.Clamp((-(a.X * dx + a.Y * dy)) / lengthSquared, 0, 1);
            var x = a.X + t * dx; var y = a.Y + t * dy;
            var cross = Math.Sqrt(x * x + y * y);
            var routeDistance = p0.DistanceFromStartMeters + t * (p1.DistanceFromStartMeters - p0.DistanceFromStartMeters);
            var continuityPenalty = previous.HasValue && i < previous.Value ? 0.0001 : 0;
            if (cross < bestDistance - 3 || (Math.Abs(cross - bestDistance) <= 3 && continuityPenalty == 0))
            { bestDistance = cross; best = new MatchedPosition(i, routeDistance, cross, t); }
        }
        return best;
    }
}
