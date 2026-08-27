namespace RoutePacer.Core.Tracking;

public sealed record RouteMatcherOptions(int WindowSegments = 100, double FullScanThresholdMeters = 75, double MaximumCrossTrackMeters = 250);
