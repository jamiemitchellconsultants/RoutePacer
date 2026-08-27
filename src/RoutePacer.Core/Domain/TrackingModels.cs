namespace RoutePacer.Core.Domain;

public sealed record GeoFix(
    DateTimeOffset TimestampUtc, double Latitude, double Longitude,
    double AccuracyMeters, double? SpeedMps);

public sealed record MatchedPosition(
    int SegmentIndex, double RouteDistanceMeters,
    double CrossTrackErrorMeters, double ProjectionRatio);

public sealed record PacingSnapshot(
    DateTimeOffset TimestampUtc, TimeSpan LiveElapsed,
    MatchedPosition Match, double? TargetElapsedSeconds,
    double? DeltaTimeSeconds, double? ExpectedDistanceMeters,
    double? DeltaDistanceMeters, double? SpeedMps);
