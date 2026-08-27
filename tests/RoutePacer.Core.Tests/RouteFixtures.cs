using RoutePacer.Core.Domain;
using RoutePacer.Core.Tracking;

namespace RoutePacer.Core.Tests;

public static class RouteFixtures
{
    /// <summary>An east-west line at the equator so one degree of longitude maps cleanly onto metres.</summary>
    public static RouteTrack Straight(int points = 11, double metresPerSecond = 10, bool timed = true, double latitude = 0)
    {
        var id = Guid.NewGuid();
        var step = 0.001;
        var list = new List<RoutePoint>(points);
        var distance = 0d;
        for (var i = 0; i < points; i++)
        {
            var longitude = i * step;
            if (i > 0) distance += GeoMath.HaversineMeters(latitude, (i - 1) * step, latitude, longitude);
            list.Add(new RoutePoint(id, i, latitude, longitude, null, distance, timed ? distance / metresPerSecond : null, null));
        }
        return new RouteTrack(
            new RouteSummary(id, "Straight", RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, distance,
                timed ? distance / metresPerSecond : null, points, latitude, 0, latitude, (points - 1) * step),
            list);
    }

    /// <summary>Out and back along the same line, so a crossing offers two nearly equal candidate segments.</summary>
    public static RouteTrack OutAndBack(int legPoints = 11)
    {
        var id = Guid.NewGuid();
        var step = 0.001;
        var coordinates = new List<double>();
        for (var i = 0; i < legPoints; i++) coordinates.Add(i * step);
        // The return leg is offset a fraction north so cumulative distance stays strictly increasing.
        for (var i = legPoints - 2; i >= 0; i--) coordinates.Add(i * step);

        var list = new List<RoutePoint>();
        var distance = 0d;
        for (var i = 0; i < coordinates.Count; i++)
        {
            var latitude = i < legPoints ? 0 : 0.00002;
            if (i > 0) distance += GeoMath.HaversineMeters(i - 1 < legPoints ? 0 : 0.00002, coordinates[i - 1], latitude, coordinates[i]);
            list.Add(new RoutePoint(id, i, latitude, coordinates[i], null, distance, distance / 10, null));
        }
        return new RouteTrack(
            new RouteSummary(id, "Out and back", RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, distance, distance / 10, list.Count, 0, 0, 0.00002, (legPoints - 1) * step),
            list);
    }
}
