namespace RoutePacer.App.Formatting;

public static class RideFormat
{
    public static string Delta(double? value, string unit)
    {
        if (!value.HasValue) return "Timing unavailable";
        if (Math.Abs(value.Value) < 0.5) return $"0 {unit}";
        return $"{Math.Abs(value.Value):0} {unit} {(value.Value < 0 ? "ahead" : "behind")}";
    }
    public static string TimeDelta(double? seconds) => !seconds.HasValue ? "Timing unavailable" : DeltaTime(seconds.Value);
    private static string DeltaTime(double seconds) => Math.Abs(seconds) < 0.5 ? "On pace" : $"{TimeSpan.FromSeconds(Math.Abs(seconds)).ToString(@"m\:ss")} {(seconds < 0 ? "ahead" : "behind")}";
    public static string Speed(double? metresPerSecond) => metresPerSecond.HasValue ? $"{metresPerSecond.Value * 3.6:0.0} km/h" : "—";
    public static string Elapsed(TimeSpan value) => value.ToString(value.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
    public static string Accuracy(double metres) => metres <= 10 ? "Good" : metres <= 30 ? "Fair" : "Poor";
}
