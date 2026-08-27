using RoutePacer.App.Routes;
using RoutePacer.Core.Domain;

namespace RoutePacer.App.Invocation;

public sealed class RouteTimerInvocationService(
    InvocationParser parser, IInvocationSettingsProvider settings, IInvocationVerifier verifier,
    HandoffPayloadClient payloads, RouteCatalogService catalog, TimeProvider clock)
{
    public async Task<RouteSummary> ImportAsync(Uri url, CancellationToken cancellationToken = default)
    {
        InvocationRequest request;
        try { request = parser.Parse(url, clock.GetUtcNow()); }
        catch (FormatException ex) { throw new InvocationFailedException("This link is not valid, or it has expired.", true, ex); }

        (bool Enabled, string? PublicKeyJwk) config;
        try { config = await settings.GetAsync(cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new InvocationFailedException("RoutePacer could not reach the server to check this link.", true, ex); }
        if (!config.Enabled || config.PublicKeyJwk is null) throw new InvocationFailedException("Shared route links are not available right now.", false);

        bool verified;
        try { verified = await verifier.VerifyAsync(request, InvocationCanonicalizer.GetBytes(request), config.PublicKeyJwk, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new InvocationFailedException("RoutePacer could not check this link's signature.", true, ex); }
        if (!verified) throw new InvocationFailedException("This link could not be verified.", false);

        // The relay deletes the row in the statement that returns it, so nothing beyond this point is retryable.
        byte[] bytes;
        try { bytes = await payloads.FetchOnceAsync(request.PayloadUri, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new InvocationFailedException("The shared route could not be downloaded. It may have expired or already been opened.", false, ex); }

        try
        {
            await using var content = new MemoryStream(bytes);
            return await catalog.ImportAsync("shared.gpx", request.Name, bytes.LongLength, content, clock.GetUtcNow(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new InvocationFailedException("The shared route arrived but could not be imported.", false, ex); }
    }
}
