using RoutePacer.App.Browser;

namespace RoutePacer.App.Formatting;

public static class RideFormat
{
    public const string NoValue = "—";
    public const string TimingUnavailable = "Timing unavailable";

    /// <summary>Signed distance delta. Negative is ahead of the planned route position, positive is behind.</summary>
    public static string Delta(double? value, string unit)
    {
        if (!value.HasValue) return NoValue;
        return Math.Abs(value.Value) < 0.5 ? $"0 {unit}" : $"{Math.Abs(value.Value):0} {unit} {Direction(value.Value)}";
    }

    /// <summary>Signed time delta. Negative is ahead of the planned schedule, positive is behind.</summary>
    public static string TimeDelta(double? seconds)
    {
        if (!seconds.HasValue) return TimingUnavailable;
        return Math.Abs(seconds.Value) < 0.5 ? "On pace" : $"{TimeSpan.FromSeconds(Math.Abs(seconds.Value)):m\\:ss} {Direction(seconds.Value)}";
    }

    public static string Direction(double value) => value < 0 ? "ahead" : "behind";

    /// <summary>A CSS modifier so lead and lag are not conveyed by colour alone; the label carries the meaning too.</summary>
    public static string DeltaTone(double? value) => value is null ? "neutral" : Math.Abs(value.Value) < 0.5 ? "neutral" : value.Value < 0 ? "ahead" : "behind";

    public static string Speed(double? metresPerSecond) => metresPerSecond.HasValue ? $"{metresPerSecond.Value * 3.6:0.0} km/h" : NoValue;

    public static string Elapsed(TimeSpan value) => value.ToString(@"h\:mm\:ss");

    public static string Accuracy(double? metres) => metres is not { } m ? NoValue : m <= 10 ? "Good" : m <= 30 ? "Fair" : "Poor";

    public static string CrossTrack(double? metres) => metres is { } m ? $"{m:0} m off line" : NoValue;

    public static string Progress(double distanceMeters, double totalDistanceMeters)
        => totalDistanceMeters <= 0 ? NoValue : $"{Math.Clamp(distanceMeters / totalDistanceMeters * 100, 0, 100):0}%";

    public static string Saved(long points) => points == 1 ? "1 point saved on this device" : $"{points} points saved on this device";

    public static string Wake(WakeLockStatus status) => status switch
    {
        WakeLockStatus.Acquired => "Screen kept awake",
        WakeLockStatus.Revoked => "Screen lock released — tap to wake",
        WakeLockStatus.Released => "Screen lock released",
        WakeLockStatus.Failed => "Could not keep the screen awake",
        _ => "Screen wake lock unavailable"
    };
}
