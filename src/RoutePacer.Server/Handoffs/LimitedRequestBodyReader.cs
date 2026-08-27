namespace RoutePacer.Server.Handoffs;

public sealed class PayloadTooLargeException : Exception;

public static class LimitedRequestBodyReader
{
    public static async Task<byte[]> ReadAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken = default)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maximumBytes) throw new PayloadTooLargeException();
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (output.Length == 0) throw new InvalidDataException("Empty payload.");
        return output.ToArray();
    }
}
