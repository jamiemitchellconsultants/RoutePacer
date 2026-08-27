using RoutePacer.Core.Domain;
namespace RoutePacer.App.Rides;

public sealed record TrackerSnapshot(RideSessionState State, RouteSummary Route, PacingSnapshot? Pacing, double DistanceMeters, string? Error = null);
