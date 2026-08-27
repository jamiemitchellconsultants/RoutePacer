using System.Text;
using FluentAssertions;
using RoutePacer.Core.Import;

namespace RoutePacer.Core.Tests.Import;

public sealed class GpxRouteParserTests
{
    private readonly GpxRouteParser _parser = new();

    private static Stream Fixture(string name) => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    private static Stream Xml(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Theory]
    [InlineData("route.gpx", true)]
    [InlineData("route.GPX", true)]
    [InlineData("route.fit", false)]
    [InlineData("route.xml", false)]
    public void CanParse_matches_the_gpx_extension_case_insensitively(string fileName, bool expected)
        => _parser.CanParse(fileName).Should().Be(expected);

    [Fact]
    public async Task Parses_namespaced_gpx_11_track_points_with_elevation_and_time()
    {
        await using var content = Fixture("timed-route.gpx");

        var points = await _parser.ParseAsync(content);

        points.Should().HaveCount(4);
        points[0].Latitude.Should().Be(0);
        points[0].ElevationMeters.Should().Be(10);
        points[0].TimestampUtc.Should().Be(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
        points[3].TimestampUtc.Should().Be(new DateTimeOffset(2026, 8, 27, 12, 0, 30, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("<gpx><trk><trkseg><trkpt lat=\"0\" lon=\"0\"><ele>10</ele><time>2026-08-27T12:00:00Z</time></trkpt></trkseg></trk></gpx>")]
    [InlineData("<gpx><trk><trkseg><trkpt lat=\"0\" lon=\"0\"><time>2026-08-27T12:00:00Z</time><ele>10</ele></trkpt></trkseg></trk></gpx>")]
    public async Task Reads_elevation_and_time_in_either_order(string xml)
    {
        await using var content = Xml(xml);

        var point = (await _parser.ParseAsync(content)).Single();

        point.ElevationMeters.Should().Be(10);
        point.TimestampUtc.Should().Be(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Falls_back_to_route_points_and_tolerates_missing_timing()
    {
        await using var content = Fixture("untimed-route.gpx");

        var points = await _parser.ParseAsync(content);

        points.Should().HaveCount(3);
        points.Should().OnlyContain(p => p.TimestampUtc == null && p.ElevationMeters == null);
    }

    [Fact]
    public async Task Rejects_malformed_xml()
    {
        await using var content = Xml("<gpx><trk><trkseg></gpx>");

        var act = () => _parser.ParseAsync(content);

        (await act.Should().ThrowAsync<RouteImportException>()).Which.Code.Should().Be("malformed-gpx");
    }

    [Fact]
    public async Task Rejects_a_document_type_definition()
    {
        await using var content = Fixture("external-entity.gpx");

        var act = () => _parser.ParseAsync(content);

        (await act.Should().ThrowAsync<RouteImportException>()).Which.Code.Should().Be("malformed-gpx");
    }

    [Theory]
    [InlineData("<gpx><trk><trkseg><trkpt lat=\"abc\" lon=\"0\"/></trkseg></trk></gpx>")]
    [InlineData("<gpx><trk><trkseg><trkpt lon=\"0\"/></trkseg></trk></gpx>")]
    [InlineData("<gpx><trk><trkseg><trkpt lat=\"0\" lon=\"0\"><ele>high</ele></trkpt></trkseg></trk></gpx>")]
    [InlineData("<gpx><trk><trkseg><trkpt lat=\"0\" lon=\"0\"><time>yesterday</time></trkpt></trkseg></trk></gpx>")]
    public async Task Rejects_invalid_numeric_and_temporal_values(string xml)
    {
        await using var content = Xml(xml);

        var act = () => _parser.ParseAsync(content);

        (await act.Should().ThrowAsync<RouteImportException>()).Which.Code.Should().Be("invalid-gpx-value");
    }

    [Fact]
    public async Task Honours_cancellation()
    {
        await using var content = Fixture("timed-route.gpx");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => _parser.ParseAsync(content, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Rejects_more_than_the_maximum_point_count()
    {
        var builder = new StringBuilder("<gpx><trk><trkseg>");
        for (var i = 0; i <= RouteImportLimits.MaximumPoints; i++) builder.Append("<trkpt lat=\"0\" lon=\"0\"/>");
        builder.Append("</trkseg></trk></gpx>");
        await using var content = Xml(builder.ToString());

        var act = () => _parser.ParseAsync(content);

        (await act.Should().ThrowAsync<RouteImportException>()).Which.Code.Should().Be("too-many-points");
    }

    [Fact]
    public async Task Accepts_exactly_the_maximum_point_count()
    {
        var builder = new StringBuilder("<gpx><trk><trkseg>");
        for (var i = 0; i < RouteImportLimits.MaximumPoints; i++) builder.Append("<trkpt lat=\"0\" lon=\"0\"/>");
        builder.Append("</trkseg></trk></gpx>");
        await using var content = Xml(builder.ToString());

        (await _parser.ParseAsync(content)).Should().HaveCount(RouteImportLimits.MaximumPoints);
    }
}
