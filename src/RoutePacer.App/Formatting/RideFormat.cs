using RoutePacer.App.Browser;

namespace RoutePacer.App.Formatting;

public static class RideFormat
{
    public const string NoValue = "—";
    public const string TimingUnavailable = "Timing unavailable";

    /// <summary>
    /// Lead: positive means ahead of plan. Both readings are converted to a lead before they are
    /// worded, because the two underlying quantities sign themselves in OPPOSITE directions --
    /// <c>DeltaTime = live - target</c> is negative when ahead, while
    /// <c>DeltaDistance = routeDistance - expected</c> is positive when ahead. One shared helper
    /// applied to both is what made the distance tile read backwards, so the conversion happens
    /// here, once, at the boundary.
    /// </summary>
    private static string Reading(double lead, string magnitude)
        => lead > 0 ? $"{magnitude} ahead" : $"behind {magnitude}";

    private static string LeadTone(double? lead)
        => lead is null ? "neutral" : Math.Abs(lead.Value) < 0.5 ? "neutral" : lead.Value > 0 ? "ahead" : "behind";

    /// <summary>
    /// Distance delta exactly as <c>PacingService</c> produces it: <c>routeDistance - expected</c>,
    /// so POSITIVE is ahead. Ahead reads "120 m ahead", behind reads "behind 85 m".
    /// </summary>
    public static string Delta(double? value, string unit)
    {
        if (!value.HasValue) return NoValue;
        if (Math.Abs(value.Value) < 0.5) return $"0 {unit}";
        return Reading(value.Value, $"{Math.Abs(value.Value):0} {unit}");
    }

    /// <summary>
    /// Time delta exactly as <c>PacingService</c> produces it: <c>live - target</c>, so NEGATIVE is
    /// ahead. Ahead reads "2:03 ahead", behind reads "behind 0:45".
    /// </summary>
    public static string TimeDelta(double? seconds)
    {
        if (!seconds.HasValue) return TimingUnavailable;
        if (Math.Abs(seconds.Value) < 0.5) return "On pace";
        return Reading(-seconds.Value, $"{TimeSpan.FromSeconds(Math.Abs(seconds.Value)):m\\:ss}");
    }

    /// <summary>
    /// The word alone, for a caller that has already resolved which way round it is. Where the word
    /// sits relative to the number is what a rider actually reads: ahead puts it after, behind puts
    /// it before. Position survives what colour does not -- a dark screen washed out by direct
    /// sunlight, and the common colour-vision deficiencies -- which is why the tracker carries no
    /// red or green at all.
    /// </summary>
    public static string Direction(double lead) => lead > 0 ? "ahead" : "behind";

    /// <summary>CSS modifier for the time tile. Names the state for assistive technology; it selects no colour.</summary>
    public static string TimeTone(double? seconds) => LeadTone(seconds is null ? null : -seconds.Value);

    /// <summary>CSS modifier for the distance tile, which signs itself the opposite way to the time tile.</summary>
    public static string DistanceTone(double? metres) => LeadTone(metres);

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
