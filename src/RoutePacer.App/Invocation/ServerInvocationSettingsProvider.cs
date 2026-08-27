using System.Net.Http.Json;
using System.Text.Json;

namespace RoutePacer.App.Invocation;

public sealed class ServerInvocationSettingsProvider(HttpClient client) : IInvocationSettingsProvider
{
    public async Task<(bool Enabled, string? PublicKeyJwk)> GetAsync(CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("/api/config/route-timer-invocation", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException("Unexpected invocation configuration payload.");
        return (enabled.GetBoolean(), root.TryGetProperty("publicKeyJwk", out var key) && key.ValueKind == JsonValueKind.Object ? key.GetRawText() : null);
    }
}
