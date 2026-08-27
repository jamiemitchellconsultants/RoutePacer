using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Tracking;

public static class TrackInterpolator
{
    public static double? ElapsedAtDistance(RouteTrack route, double distance)
    {
        if (!route.HasTiming) return null;
        var points = route.Points; var index = FindDistance(points, Math.Clamp(distance, 0, points[^1].DistanceFromStartMeters));
        if (index == points.Count - 1) return points[^1].ElapsedSeconds;
        return Interpolate(points[index].DistanceFromStartMeters, points[index + 1].DistanceFromStartMeters, points[index].ElapsedSeconds!.Value, points[index + 1].ElapsedSeconds!.Value, distance);
    }

    public static double? DistanceAtElapsed(RouteTrack route, double elapsed)
    {
        if (!route.HasTiming) return null;
        var points = route.Points; var value = Math.Clamp(elapsed, 0, points[^1].ElapsedSeconds!.Value); var index = FindElapsed(points, value);
        if (index == points.Count - 1) return points[^1].DistanceFromStartMeters;
        return Interpolate(points[index].ElapsedSeconds!.Value, points[index + 1].ElapsedSeconds!.Value, points[index].DistanceFromStartMeters, points[index + 1].DistanceFromStartMeters, value);
    }

    private static int FindDistance(IReadOnlyList<RoutePoint> points, double value) { var lo = 0; var hi = points.Count - 1; while (lo < hi) { var mid = (lo + hi) / 2; if (points[mid].DistanceFromStartMeters < value) lo = mid + 1; else hi = mid; } return Math.Max(0, lo - (lo > 0 && points[lo].DistanceFromStartMeters > value ? 1 : 0)); }
    private static int FindElapsed(IReadOnlyList<RoutePoint> points, double value) { var lo = 0; var hi = points.Count - 1; while (lo < hi) { var mid = (lo + hi) / 2; if (points[mid].ElapsedSeconds < value) lo = mid + 1; else hi = mid; } return Math.Max(0, lo - (lo > 0 && points[lo].ElapsedSeconds > value ? 1 : 0)); }
    private static double Interpolate(double x0, double x1, double y0, double y1, double x) => x1 == x0 ? y1 : y0 + (y1 - y0) * ((x - x0) / (x1 - x0));
}
