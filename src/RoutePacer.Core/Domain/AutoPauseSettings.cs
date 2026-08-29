namespace RoutePacer.Core.Domain;

/// <summary>
/// A standing rider preference, not a property of the route: it outlives any one import, so a rider
/// who always wants the same autopause sets it once.
/// </summary>
public sealed record AutoPauseSettings(bool Enabled, int ThresholdSeconds)
{
    public const int MinimumSeconds = 5;
    public const int MaximumSeconds = 300;
    public const int DefaultSeconds = 15;

    public static AutoPauseSettings Default { get; } = new(false, DefaultSeconds);

    public AutoPauseSettings Clamped()
        => this with { ThresholdSeconds = Math.Clamp(ThresholdSeconds, MinimumSeconds, MaximumSeconds) };
}
