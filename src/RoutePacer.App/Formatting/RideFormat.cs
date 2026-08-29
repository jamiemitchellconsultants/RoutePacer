using RoutePacer.App.Browser;

namespace RoutePacer.App.Formatting;

public static class RideFormat
{
    public const string NoValue = "—";
    public const string TimingUnavailable = "Timing unavailable";

    /// <summary>
    /// Signed distance delta. Negative is ahead of the planned route position, positive is behind.
    /// The word's POSITION carries the meaning: ahead reads "120 m ahead", behind reads
    /// "behind 85 m". See <see cref="Direction"/>.
    /// </summary>
    public static string Delta(double? value, string unit)
    {
        if (!value.HasValue) return NoValue;
        if (Math.Abs(value.Value) < 0.5) return $"0 {unit}";
        var magnitude = $"{Math.Abs(value.Value):0} {unit}";
        return value.Value < 0 ? $"{magnitude} ahead" : $"behind {magnitude}";
    }

    /// <summary>
    /// Signed time delta. Negative is ahead of the planned schedule, positive is behind. Ahead reads
    /// "2:03 ahead", behind reads "behind 0:45". See <see cref="Direction"/>.
    /// </summary>
    public static string TimeDelta(double? seconds)
    {
        if (!seconds.HasValue) return TimingUnavailable;
        if (Math.Abs(seconds.Value) < 0.5) return "On pace";
        var magnitude = $"{TimeSpan.FromSeconds(Math.Abs(seconds.Value)):m\\:ss}";
        return seconds.Value < 0 ? $"{magnitude} ahead" : $"behind {magnitude}";
    }

    /// <summary>
    /// The word alone. Where it sits relative to the number is what a rider actually reads: ahead
    /// puts it after ("2:03 ahead"), behind puts it before ("behind 0:45"). Position survives what
    /// colour does not -- a dark screen washed out by direct sunlight, and the common colour-vision
    /// deficiencies -- which is why the tracker carries no red or green at all.
    /// </summary>
    public static string Direction(double value) => value < 0 ? "ahead" : "behind";

    /// <summary>
    /// A CSS modifier. It no longer selects a colour -- the tracker is monochrome -- but it still
    /// names the state for assistive technology and for anyone styling the panel.
    /// </summary>
    public static string DeltaTone(double? value) => value is null ? "neutral" : Math.Abs(value.Value) < 0.5 ? "neutral" : value.Value < 0 ? "ahead" : "behind";

    public static string Speed(double? metresPerSecond) => metresPerSecond.HasValue ? $"{metresPerSecond.Value * 3.6:0.0} km/h" : NoValue;

    public static string Elapsed(TimeSpan value) => value.ToString(@"h\:mm\:ss");

    public static string Accuracy(double? metres) => metres is not { } m ? NoValue : m <= 10 ? "Good" : m <= 30 ? "Fair" : "Poor";

    public static string CrossTrack(double? metres) => metres is { } m ? $"{m:0} m off line" : NoValue;

    public static string Progress(double distanceMeters, double totalDistanceMeters)
        => totalDistanceMeters <= 0 ? NoValue : $"{Math.Clamp(distanceMeters / totalDistanceMeters * 100, 0, 100):0}%";

    /// <summary>
    /// Points recorded so far in this ride. Deliberately not "saved": nothing about a finished ride
    /// is kept, and the old wording promised a durability the application no longer offers.
    /// </summary>
    public static string Points(long points) => points == 1 ? "1 point this ride" : $"{points} points this ride";

    public static string Distance(double metres) => metres >= 1000 ? $"{metres / 1000:0.0} km" : $"{metres:0} m";

    public static string Wake(WakeLockStatus status) => status switch
    {
        WakeLockStatus.Acquired => "Screen kept awake",
        WakeLockStatus.Revoked => "Screen lock released — tap to wake",
        WakeLockStatus.Released => "Screen lock released",
        WakeLockStatus.Failed => "Could not keep the screen awake",
        _ => "Screen wake lock unavailable"
    };
}
