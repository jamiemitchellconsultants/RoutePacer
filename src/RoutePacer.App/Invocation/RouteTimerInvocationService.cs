using RoutePacer.App.Routes;
using RoutePacer.Core.Domain;
namespace RoutePacer.App.Invocation;

public sealed class RouteTimerInvocationService(InvocationParser parser, IInvocationSettingsProvider settings, IInvocationVerifier verifier, HandoffPayloadClient payloads, RouteCatalogService catalog, TimeProvider clock)
{
    public async Task<RouteSummary> ImportAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var request = parser.Parse(url, clock.GetUtcNow()); var config = await settings.GetAsync(cancellationToken); if (!config.Enabled || config.PublicKeyJwk is null) throw new InvalidOperationException("Route sharing is unavailable.");
        if (!await verifier.VerifyAsync(request, InvocationCanonicalizer.GetBytes(request), config.PublicKeyJwk, cancellationToken)) throw new InvalidOperationException("The shared route could not be verified.");
        var bytes = await payloads.FetchOnceAsync(request.PayloadUri, cancellationToken); return await catalog.ImportAsync("shared.gpx", request.Name, bytes.LongLength, new MemoryStream(bytes), clock.GetUtcNow(), cancellationToken);
    }
}
