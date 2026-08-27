using RoutePacer.Core.Domain;
using RoutePacer.Core.Tracking;

namespace RoutePacer.Core.Import;

public sealed class RouteNormalizer
{
    public RouteTrack Normalize(Guid routeId, string name, RouteSourceType sourceType, DateTimeOffset importedAtUtc, IReadOnlyList<RawRoutePoint> rawPoints)
    {
        ArgumentNullException.ThrowIfNull(rawPoints);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A route name is required.", nameof(name));

        var points = new List<RawRoutePoint>(rawPoints.Count);
        foreach (var point in rawPoints)
        {
            if (!double.IsFinite(point.Latitude) || !double.IsFinite(point.Longitude) || point.Latitude is < -90 or > 90 || point.Longitude is < -180 or > 180)
                throw new RouteImportException("invalid-coordinate", "The route contains an invalid coordinate.");
            if (points.Count == 0 || points[^1].Latitude != point.Latitude || points[^1].Longitude != point.Longitude)
                points.Add(point);
        }

        if (points.Count < 3) throw new RouteImportException("too-few-points", "The route needs at least three distinct points.");

        var hasTimestamps = points.All(p => p.TimestampUtc.HasValue);
        var hasElapsed = points.All(p => p.ElapsedSeconds.HasValue);
        var timingValid = hasTimestamps || hasElapsed;
        var elapsed = new double?[points.Count];
        DateTimeOffset? firstTimestamp = hasTimestamps ? points[0].TimestampUtc : null;
        for (var i = 0; i < points.Count && timingValid; i++)
        {
            elapsed[i] = hasTimestamps ? (points[i].TimestampUtc!.Value - firstTimestamp!.Value).TotalSeconds : points[i].ElapsedSeconds;
            timingValid = elapsed[i] is >= 0 && (i == 0 || elapsed[i] >= elapsed[i - 1]);
        }
        if (!timingValid) Array.Fill(elapsed, null);

        var routePoints = new List<RoutePoint>(points.Count);
        var totalDistance = 0d;
        var minLat = double.PositiveInfinity;
        var minLon = double.PositiveInfinity;
        var maxLat = double.NegativeInfinity;
        var maxLon = double.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0) totalDistance += GeoMath.HaversineMeters(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
            minLat = Math.Min(minLat, points[i].Latitude); minLon = Math.Min(minLon, points[i].Longitude);
            maxLat = Math.Max(maxLat, points[i].Latitude); maxLon = Math.Max(maxLon, points[i].Longitude);
            routePoints.Add(new RoutePoint(routeId, i, points[i].Latitude, points[i].Longitude, points[i].ElevationMeters, totalDistance, elapsed[i], points[i].TimestampUtc));
        }
        if (totalDistance <= 0) throw new RouteImportException("zero-length-route", "The route has no measurable distance.");

        var summary = new RouteSummary(routeId, name.Trim(), sourceType, importedAtUtc, totalDistance,
            timingValid ? elapsed[^1] : null, routePoints.Count, minLat, minLon, maxLat, maxLon);
        return new RouteTrack(summary, routePoints);
    }
}
