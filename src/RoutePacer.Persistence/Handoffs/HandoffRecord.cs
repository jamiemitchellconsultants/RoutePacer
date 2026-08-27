namespace RoutePacer.Persistence.Handoffs;

public sealed class HandoffRecord
{
    public byte[] TokenHash { get; set; } = [];
    public byte[] Content { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
