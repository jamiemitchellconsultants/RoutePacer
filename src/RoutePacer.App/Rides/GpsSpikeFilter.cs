using RoutePacer.Core.Domain;
namespace RoutePacer.App.Rides;

public sealed class GpsSpikeFilter
{
    private GeoFix? previous;
    public bool Accept(GeoFix fix)
    {
        if (!double.IsFinite(fix.Latitude) || !double.IsFinite(fix.Longitude) || fix.Latitude is < -90 or > 90 || fix.Longitude is < -180 or > 180 || fix.AccuracyMeters > 100) return false;
        if (previous is null) { previous = fix; return true; }
        var seconds = (fix.TimestampUtc - previous.TimestampUtc).TotalSeconds;
        if (seconds <= 0) return false;
        var implied = RoutePacer.Core.Tracking.GeoMath.HaversineMeters(previous.Latitude, previous.Longitude, fix.Latitude, fix.Longitude) / seconds;
        if (implied > 35 && (!fix.SpeedMps.HasValue || Math.Abs(implied - fix.SpeedMps.Value) > 10)) return false;
        previous = fix; return true;
    }
}
