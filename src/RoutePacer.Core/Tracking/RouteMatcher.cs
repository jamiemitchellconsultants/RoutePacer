using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Tracking;

public sealed class RouteMatcher(RouteMatcherOptions? options = null)
{
    private const double TieToleranceMeters = 3;
    private readonly RouteMatcherOptions _options = options ?? new();

    public MatchedPosition? Match(RouteTrack route, GeoFix fix, int? previousSegmentIndex)
    {
        ArgumentNullException.ThrowIfNull(route); ArgumentNullException.ThrowIfNull(fix);
        var lastSegment = route.Points.Count - 2;
        MatchedPosition? best;
        if (previousSegmentIndex.HasValue)
        {
            var (first, last) = CandidateRange(lastSegment, previousSegmentIndex.Value - _options.WindowSegments, previousSegmentIndex.Value + _options.WindowSegments);
            best = Find(route, fix, first, last, previousSegmentIndex);
            if (best is null || best.CrossTrackErrorMeters > _options.FullScanThresholdMeters) best = Find(route, fix, 0, lastSegment, previousSegmentIndex);
        }
        else best = Find(route, fix, 0, lastSegment, null);
        return best is not null && best.CrossTrackErrorMeters <= _options.MaximumCrossTrackMeters ? best : null;
    }

    private static (int First, int Last) CandidateRange(int lastSegment, int start, int end) => (Math.Max(0, start), Math.Min(lastSegment, end));

    private static MatchedPosition? Find(RouteTrack route, GeoFix fix, int first, int last, int? previous)
    {
        MatchedPosition? best = null; var bestCross = double.PositiveInfinity;
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
            if (best is null || cross < bestCross - TieToleranceMeters || (cross <= bestCross + TieToleranceMeters && PrefersCandidate(i, best.SegmentIndex, previous)))
                best = new MatchedPosition(i, p0.DistanceFromStartMeters + t * (p1.DistanceFromStartMeters - p0.DistanceFromStartMeters), cross, t);
            bestCross = Math.Min(bestCross, cross);
        }
        return best;
    }

    // Within the tie tolerance the plan prefers the smallest non-negative change in segment index so an
    // out-and-back crossing does not snap the rider backward onto the returning leg.
    private static bool PrefersCandidate(int candidate, int current, int? previous)
    {
        if (previous is not { } from) return false;
        int candidateDelta = candidate - from, currentDelta = current - from;
        if (candidateDelta >= 0 != currentDelta >= 0) return candidateDelta >= 0;
        return Math.Abs(candidateDelta) < Math.Abs(currentDelta);
    }
}
