using System.Text;
using FluentAssertions;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Import;

namespace RoutePacer.Core.Tests.Import;

public sealed class RouteImportServiceTests
{
    private static readonly DateTimeOffset ImportedAt = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static RouteImportService Service() =>
        new([new GpxRouteParser(), new FitRouteParser()], new RouteNormalizer());

    private static Stream Gpx() => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "timed-route.gpx"));

    [Fact]
    public async Task Imports_a_gpx_file_and_derives_the_name_from_the_file_stem()
    {
        await using var content = Gpx();

        var imported = await Service().ImportAsync(new("Morning Loop.gpx", null, 512, ImportedAt), content);

        imported.OriginalFileName.Should().Be("Morning Loop.gpx");
        imported.Track.Summary.Name.Should().Be("Morning Loop");
        imported.Track.Summary.SourceType.Should().Be(RouteSourceType.Gpx);
        imported.Track.Summary.ImportedAtUtc.Should().Be(ImportedAt);
        imported.Track.HasTiming.Should().BeTrue();
    }

    [Fact]
    public async Task An_explicit_display_name_wins_over_the_file_stem()
    {
        await using var content = Gpx();

        var imported = await Service().ImportAsync(new("shared.gpx", "  Café & climb  ", 512, ImportedAt), content);

        imported.Track.Summary.Name.Should().Be("Café & climb");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RouteImportLimits.MaximumFileBytes + 1)]
    public async Task Rejects_lengths_outside_the_allowed_range_before_parsing(long length)
    {
        var content = new ThrowingStream();

        var act = () => Service().ImportAsync(new("route.gpx", null, length, ImportedAt), content);

        (await act.Should().ThrowAsync<RouteImportException>()).Which.Code.Should().Be("file-too-large");
    }

    [Fact]
    public async Task Accepts_exactly_the_maximum_length()
    {
        await using var content = Gpx();

        var act = () => Service().ImportAsync(new("route.gpx", null, RouteImportLimits.MaximumFileBytes, ImportedAt), content);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("route.tcx")]
    [InlineData("route")]
    [InlineData("route.gpx.txt")]
    public async Task Rejects_unsupported_extensions(string fileName)
    {
        var content = new ThrowingStream();

        var act = () => Service().ImportAsync(new(fileName, null, 512, ImportedAt), content);

        (await act.Should().ThrowAsync<RouteImportException>()).Which.Code.Should().Be("unsupported-file");
    }

    [Fact]
    public async Task Dispatches_on_extension_case_insensitively()
    {
        await using var content = Gpx();

        var imported = await Service().ImportAsync(new("ROUTE.GPX", null, 512, ImportedAt), content);

        imported.Track.Summary.SourceType.Should().Be(RouteSourceType.Gpx);
    }

    [Fact]
    public async Task Assigns_a_fresh_route_id_to_every_import()
    {
        await using var first = Gpx();
        await using var second = Gpx();
        var service = Service();

        var a = await service.ImportAsync(new("route.gpx", null, 512, ImportedAt), first);
        var b = await service.ImportAsync(new("route.gpx", null, 512, ImportedAt), second);

        a.Track.Summary.RouteId.Should().NotBe(b.Track.Summary.RouteId);
        a.Track.Points.Should().OnlyContain(p => p.RouteId == a.Track.Summary.RouteId);
    }

    [Fact]
    public async Task A_route_with_too_few_distinct_points_reports_a_stable_code()
    {
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(
            "<gpx><trk><trkseg><trkpt lat=\"0\" lon=\"0\"/><trkpt lat=\"0\" lon=\"0\"/></trkseg></trk></gpx>"));

        var act = () => Service().ImportAsync(new("route.gpx", null, 128, ImportedAt), content);

        (await act.Should().ThrowAsync<RouteImportException>()).Which.Code.Should().Be("too-few-points");
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The stream must not be read.");
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
