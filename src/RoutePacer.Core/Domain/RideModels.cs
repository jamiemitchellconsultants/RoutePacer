namespace RoutePacer.Core.Domain;

public enum RideStatus { Running, Paused, Completed, Interrupted }

public sealed record RideSummary(
    Guid RideId, Guid RouteId, DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc, RideStatus Status,
    double TotalDistanceMeters, double DurationSeconds, double AvgSpeedMps);

public sealed record RidePoint(
    Guid RideId, long Sequence, DateTimeOffset TimestampUtc,
    double Latitude, double Longitude, double? SpeedMps,
    double AccuracyMeters, double? ProjectedRouteDistanceMeters,
    double? DeltaDistanceMeters, double? DeltaTimeSeconds,
    double? CrossTrackErrorMeters);

/// <summary>An in-progress ride recovered from storage: its summary and the points recorded so far.</summary>
public sealed record ActiveRide(RideSummary Summary, IReadOnlyList<RidePoint> Points);
