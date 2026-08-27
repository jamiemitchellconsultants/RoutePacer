using RoutePacer.App.Browser;
using RoutePacer.Core.Domain;

namespace RoutePacer.App.Rides;

public sealed record TrackerSnapshot(
    RideSessionState State,
    RouteSummary Route,
    PacingSnapshot? Pacing,
    double DistanceMeters,
    TimeSpan Elapsed,
    bool RouteHasTiming,
    long SavedPointCount,
    double? AccuracyMeters,
    WakeLockStatus WakeStatus,
    string? Error = null);
