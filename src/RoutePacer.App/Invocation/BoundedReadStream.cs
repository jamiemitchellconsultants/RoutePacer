namespace RoutePacer.App.Invocation;

public sealed class BoundedReadStream(Stream inner, long maximum) : Stream
{
    private long total;
    public override bool CanRead => inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => total; public override long Position { get => total; set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { var read = await inner.ReadAsync(buffer, cancellationToken); total += read; if (total > maximum) throw new InvalidDataException("Payload too large."); return read; }
    public override int Read(byte[] buffer, int offset, int count) { var read = inner.Read(buffer, offset, count); total += read; if (total > maximum) throw new InvalidDataException("Payload too large."); return read; }
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    public override ValueTask DisposeAsync() => inner.DisposeAsync();
    public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
