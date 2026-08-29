using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Tracking;

/// <summary>
/// Decides whether a rider is standing still, from position alone.
///
/// The two radii differ deliberately. One radius would let a phone drifting on GPS noise at the
/// boundary flap between paused and running, which a rider reads as the number flickering for no
/// reason they can see. Speed is not consulted: <see cref="GeoFix.SpeedMps"/> is optional, and
/// phones report it unreliably or not at all at exactly the speeds this has to tell apart.
/// </summary>
public sealed class StationaryDetector
{
    public const double StationaryRadiusMeters = 10;
    public const double ResumeRadiusMeters = 15;

    private double latitude, longitude;
    private DateTimeOffset anchoredAt;

    public bool IsAnchored { get; private set; }

    public void Reset() => IsAnchored = false;

    /// <summary>Time spent at the anchor, re-anchoring when the fix has left the stationary radius.</summary>
    public TimeSpan Observe(GeoFix fix)
    {
        if (IsAnchored && MetersFromAnchor(fix) <= StationaryRadiusMeters) return StationaryTime(fix);
        latitude = fix.Latitude;
        longitude = fix.Longitude;
        anchoredAt = fix.TimestampUtc;
        IsAnchored = true;
        return TimeSpan.Zero;
    }

    /// <summary>Displacement from the anchor, leaving the anchor where it is.</summary>
    public double MetersFromAnchor(GeoFix fix)
        => IsAnchored ? GeoMath.HaversineMeters(latitude, longitude, fix.Latitude, fix.Longitude) : 0;

    /// <summary>Time at the anchor as of this fix, leaving the anchor where it is.</summary>
    public TimeSpan StationaryTime(GeoFix fix)
    {
        if (!IsAnchored) return TimeSpan.Zero;
        var elapsed = fix.TimestampUtc - anchoredAt;
        return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
    }
}
