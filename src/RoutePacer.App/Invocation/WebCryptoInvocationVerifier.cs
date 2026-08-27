using Microsoft.JSInterop;
namespace RoutePacer.App.Invocation;

public sealed class WebCryptoInvocationVerifier(IJSRuntime js) : IInvocationVerifier
{
    public async Task<bool> VerifyAsync(InvocationRequest request, byte[] canonicalBytes, string publicJwk, CancellationToken cancellationToken = default)
    {
        var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/invocation.js");
        try { return await module.InvokeAsync<bool>("verifySignature", cancellationToken, [publicJwk, request.Signature, canonicalBytes]); }
        finally { await module.DisposeAsync(); }
    }
}
