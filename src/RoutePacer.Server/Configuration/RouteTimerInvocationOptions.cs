namespace RoutePacer.Server.Configuration;

public sealed class RouteTimerInvocationOptions
{
    public bool Enabled { get; init; }
    public string PublicKeyJwk { get; init; } = "";
}
