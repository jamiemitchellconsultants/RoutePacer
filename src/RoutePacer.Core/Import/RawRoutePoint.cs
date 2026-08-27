namespace RoutePacer.Core.Import;

public sealed record RawRoutePoint(
    double Latitude, double Longitude, double? ElevationMeters,
    double? ElapsedSeconds, DateTimeOffset? TimestampUtc);
