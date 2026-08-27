namespace RoutePacer.Server.Handoffs;

public sealed class HandoffRelayOptions
{
    public bool UploadsEnabled { get; init; }
    public Uri PublicOrigin { get; init; } = new("https://pacetracking.tqaentry.com");
    public string UploadCredential { get; init; } = "";
    public int MaximumUploadBytes { get; init; } = 52_428_800;
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(10);
}
