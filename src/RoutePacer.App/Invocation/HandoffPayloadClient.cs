namespace RoutePacer.App.Invocation;

public sealed class HandoffPayloadClient(HttpClient client)
{
    public async Task<byte[]> FetchOnceAsync(Uri payloadUri, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(payloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || !string.Equals(response.Content.Headers.ContentType?.MediaType, "application/gpx+xml", StringComparison.Ordinal)) throw new InvalidDataException("The shared route is unavailable.");
        if (response.Content.Headers.ContentLength is > 52_428_800) throw new InvalidDataException("The shared route is too large.");
        await using var bounded = new BoundedReadStream(await response.Content.ReadAsStreamAsync(cancellationToken), 52_428_800);
        await using var output = new MemoryStream(); await bounded.CopyToAsync(output, cancellationToken); if (output.Length == 0) throw new InvalidDataException("The shared route is empty."); return output.ToArray();
    }
}
