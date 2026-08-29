using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Tracking;

public sealed class PacingService
{
    public static double DeltaTime(double liveSeconds, double targetSeconds) => liveSeconds - targetSeconds;

    /// <summary>
    /// <paramref name="pausedTotal"/> is required rather than defaulted. Live elapsed drives every
    /// delta the rider reads, so a caller that forgets to subtract a pause must not compile.
    /// </summary>
    public PacingSnapshot Calculate(RouteTrack route, MatchedPosition match, DateTimeOffset sessionStartedAtUtc, TimeSpan pausedTotal, GeoFix fix)
    {
        var live = TimeSpan.FromSeconds(Math.Max(0, (fix.TimestampUtc - sessionStartedAtUtc - pausedTotal).TotalSeconds));
        if (!route.HasTiming) return new PacingSnapshot(fix.TimestampUtc, live, match, null, null, null, null, fix.SpeedMps);
        var target = TrackInterpolator.ElapsedAtDistance(route, match.RouteDistanceMeters);
        var expected = TrackInterpolator.DistanceAtElapsed(route, live.TotalSeconds);
        return new PacingSnapshot(fix.TimestampUtc, live, match, target, target.HasValue ? DeltaTime(live.TotalSeconds, target.Value) : null, expected, expected.HasValue ? match.RouteDistanceMeters - expected.Value : null, fix.SpeedMps);
    }
}
