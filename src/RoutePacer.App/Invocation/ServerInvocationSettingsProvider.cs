using System.Text.Json;
namespace RoutePacer.App.Invocation;

public sealed class ServerInvocationSettingsProvider(HttpClient client) : IInvocationSettingsProvider
{
    public async Task<(bool Enabled, string? PublicKeyJwk)> GetAsync(CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(await (await client.GetAsync("/api/config/route-timer-invocation", cancellationToken)).Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement; return (root.GetProperty("enabled").GetBoolean(), root.TryGetProperty("publicKeyJwk", out var key) ? key.GetRawText() : null);
    }
}
