using FluentAssertions;
using RoutePacer.Server.Handoffs;

namespace RoutePacer.Server.Tests.Handoffs;

public sealed class LimitedRequestBodyReaderTests
{
    private const int Maximum = 52_428_800;

    /// <summary>A stream whose reported length lies, to prove the reader trusts only what it reads.</summary>
    private sealed class LyingLengthStream(byte[] content, long? reportedLength) : Stream
    {
        private readonly MemoryStream inner = new(content);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => reportedLength ?? throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Returns_the_exact_bytes()
    {
        var expected = "<gpx>exact</gpx>"u8.ToArray();

        var bytes = await LimitedRequestBodyReader.ReadAsync(new MemoryStream(expected), Maximum);

        bytes.Should().Equal(expected);
    }

    [Fact]
    public async Task Reads_a_body_larger_than_one_buffer()
    {
        var expected = new byte[300_000];
        Random.Shared.NextBytes(expected);

        (await LimitedRequestBodyReader.ReadAsync(new MemoryStream(expected), Maximum)).Should().Equal(expected);
    }

    [Fact]
    public async Task Rejects_an_empty_body()
    {
        var act = () => LimitedRequestBodyReader.ReadAsync(new MemoryStream(), Maximum);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Accepts_exactly_the_maximum()
    {
        var content = new byte[1024];

        var bytes = await LimitedRequestBodyReader.ReadAsync(new MemoryStream(content), 1024);

        bytes.Should().HaveCount(1024);
    }

    [Fact]
    public async Task Rejects_one_byte_beyond_the_maximum()
    {
        var act = () => LimitedRequestBodyReader.ReadAsync(new MemoryStream(new byte[1025]), 1024);

        await act.Should().ThrowAsync<PayloadTooLargeException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(100_000L)]
    public async Task An_absent_or_false_declared_length_does_not_change_the_outcome(long? reportedLength)
    {
        var content = new byte[1025];

        var act = () => LimitedRequestBodyReader.ReadAsync(new LyingLengthStream(content, reportedLength), 1024);

        await act.Should().ThrowAsync<PayloadTooLargeException>();
    }

    [Fact]
    public async Task Honours_cancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => LimitedRequestBodyReader.ReadAsync(new MemoryStream(new byte[16]), Maximum, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
