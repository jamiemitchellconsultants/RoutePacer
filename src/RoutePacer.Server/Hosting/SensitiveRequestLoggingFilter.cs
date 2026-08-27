using Microsoft.AspNetCore.Http.Metadata;

namespace RoutePacer.Server.Hosting;

/// <summary>
/// Records one safe line per relay request. Only the method, the literal route template, the status class
/// and an aggregate byte count are emitted: never the concrete request target, token, payload URL,
/// invocation query, signature, route name, authorization header, response body, or GPX bytes.
/// </summary>
public sealed class SensitiveRequestLoggingFilter(RequestDelegate next, ILogger<SensitiveRequestLoggingFilter> logger)
{
    private static readonly EventId RelayRequest = new(1200, "RelayRequest");

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        var path = context.Request.Path;
        var template = path.StartsWithSegments("/api/handoffs")
            ? context.GetEndpoint()?.Metadata.GetMetadata<IRouteDiagnosticsMetadata>()?.Route ?? "/api/handoffs"
            : path.StartsWithSegments("/open") ? "/open" : null;
        if (template is null) return;

        logger.LogInformation(RelayRequest, "{Method} {RouteTemplate} {StatusClass}xx {ResponseBytes}",
            context.Request.Method, template, context.Response.StatusCode / 100, context.Response.ContentLength ?? 0);
    }
}
