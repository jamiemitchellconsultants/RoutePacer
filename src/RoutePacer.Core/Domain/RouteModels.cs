namespace RoutePacer.Core.Domain;

public enum RouteSourceType { Gpx, Fit }

public sealed record RouteSummary(
    Guid RouteId, string Name, RouteSourceType SourceType,
    DateTimeOffset ImportedAtUtc, double TotalDistanceMeters,
    double? TotalDurationSeconds, int PointCount,
    double MinLatitude, double MinLongitude,
    double MaxLatitude, double MaxLongitude);

public sealed record RoutePoint(
    Guid RouteId, int Index, double Latitude, double Longitude,
    double? ElevationMeters, double DistanceFromStartMeters,
    double? ElapsedSeconds, DateTimeOffset? TimestampUtc);

public sealed class RouteTrack
{
    public RouteSummary Summary { get; }
    public IReadOnlyList<RoutePoint> Points { get; }
    public bool HasTiming { get; }

    public RouteTrack(RouteSummary summary, IReadOnlyList<RoutePoint> points)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(points);
        Summary = summary;
        if (points.Count < 3) throw new ArgumentException("A route must contain at least 3 points.", nameof(points));
        if (summary.PointCount != points.Count) throw new ArgumentException("Summary point count must match points.", nameof(points));

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.RouteId != summary.RouteId) throw new ArgumentException("All points must use the route ID.", nameof(points));
            if (point.Index != i) throw new ArgumentException("Point indices must be contiguous and monotonic.", nameof(points));
            if (i > 0 && point.DistanceFromStartMeters <= points[i - 1].DistanceFromStartMeters)
                throw new ArgumentException("Route points must have strictly increasing cumulative distance.", nameof(points));
            if (point.ElapsedSeconds is < 0 || (i > 0 && point.ElapsedSeconds is not null && points[i - 1].ElapsedSeconds is not null && point.ElapsedSeconds < points[i - 1].ElapsedSeconds))
                throw new ArgumentException("Route timing must be non-negative and monotonic.", nameof(points));
        }

        Points = points.ToArray();
        HasTiming = Points.All(p => p.ElapsedSeconds.HasValue);
    }
}
