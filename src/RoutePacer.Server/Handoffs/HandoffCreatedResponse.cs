namespace RoutePacer.Server.Handoffs;

public sealed record HandoffCreatedResponse(string PayloadUrl, DateTimeOffset ExpiresAt);
