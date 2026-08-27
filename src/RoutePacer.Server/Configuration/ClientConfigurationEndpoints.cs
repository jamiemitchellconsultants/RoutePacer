using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace RoutePacer.Server.Configuration;

public static class ClientConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapClientConfiguration(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/config/route-timer-invocation", (HttpResponse response, IOptions<RouteTimerInvocationOptions> options) =>
        {
            response.Headers.CacheControl = "no-store";
            if (!options.Value.Enabled) return Results.Json(new { enabled = false });
            var key = JsonNode.Parse(options.Value.PublicKeyJwk) as JsonObject ?? throw new InvalidOperationException("Public JWK is invalid.");
            return Results.Json(new { enabled = true, publicKeyJwk = key });
        });
        return endpoints;
    }
}
