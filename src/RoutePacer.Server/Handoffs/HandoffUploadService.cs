using Microsoft.Extensions.Options;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.Server.Handoffs;

public sealed class HandoffUploadService(IHandoffStore store, IOptions<HandoffRelayOptions> options, TimeProvider clock)
{
    public async Task<HandoffCreatedResponse> CreateAsync(byte[] content, CancellationToken cancellationToken)
    {
        var token = HandoffToken.Create(); var now = clock.GetUtcNow(); var expires = now.AddMinutes(10);
        await store.InsertAsync(token.Sha256, content, now, expires, cancellationToken);
        var url = new Uri(options.Value.PublicOrigin, $"/api/handoffs/{token.Plaintext}");
        return new HandoffCreatedResponse(url.AbsoluteUri, expires);
    }
}
