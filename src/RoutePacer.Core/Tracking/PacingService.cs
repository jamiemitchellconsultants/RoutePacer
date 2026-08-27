using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Tracking;

public sealed class PacingService
{
    public static double DeltaTime(double liveSeconds, double targetSeconds) => liveSeconds - targetSeconds;

    public PacingSnapshot Calculate(RouteTrack route, MatchedPosition match, DateTimeOffset sessionStartedAtUtc, GeoFix fix)
    {
        var live = TimeSpan.FromSeconds(Math.Max(0, (fix.TimestampUtc - sessionStartedAtUtc).TotalSeconds));
        if (!route.HasTiming) return new PacingSnapshot(fix.TimestampUtc, live, match, null, null, null, null, fix.SpeedMps);
        var target = TrackInterpolator.ElapsedAtDistance(route, match.RouteDistanceMeters);
        var expected = TrackInterpolator.DistanceAtElapsed(route, live.TotalSeconds);
        return new PacingSnapshot(fix.TimestampUtc, live, match, target, target.HasValue ? DeltaTime(live.TotalSeconds, target.Value) : null, expected, expected.HasValue ? match.RouteDistanceMeters - expected.Value : null, fix.SpeedMps);
    }
}
