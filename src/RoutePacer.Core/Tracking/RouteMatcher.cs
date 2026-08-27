using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Tracking;

public sealed class RouteMatcher(RouteMatcherOptions? options = null)
{
    /// <summary>
    /// Added to a candidate that lies behind the previous match. It makes the forward leg win wherever the two
    /// are within this distance of each other, which is what keeps an out-and-back crossing from snapping
    /// backward, while leaving a clearly closer segment free to win outright.
    /// </summary>
    private const double BackwardPenaltyMeters = 3;
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
        MatchedPosition? best = null; var bestScore = double.PositiveInfinity;
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
            var score = cross + (previous is { } from && i < from ? BackwardPenaltyMeters : 0);
            if (score < bestScore)
            {
                bestScore = score;
                best = new MatchedPosition(i, p0.DistanceFromStartMeters + t * (p1.DistanceFromStartMeters - p0.DistanceFromStartMeters), cross, t);
            }
        }
        return best;
    }
}
