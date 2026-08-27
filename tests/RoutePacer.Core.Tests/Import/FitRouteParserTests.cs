using FluentAssertions;
using RoutePacer.Core.Import;

namespace RoutePacer.Core.Tests.Import;

public sealed class FitRouteParserTests
{
    private readonly FitRouteParser _parser = new();

    [Theory]
    [InlineData("course.fit", true)]
    [InlineData("course.FIT", true)]
    [InlineData("course.gpx", false)]
    public void CanParse_matches_the_fit_extension_case_insensitively(string fileName, bool expected)
        => _parser.CanParse(fileName).Should().Be(expected);

    [Fact]
    public async Task Rejects_a_file_that_is_not_fit_with_a_stable_code()
    {
        await using var content = new MemoryStream("this is not a FIT file"u8.ToArray());

        var act = () => _parser.ParseAsync(content);

        (await act.Should().ThrowAsync<RouteImportException>()).Which.Code.Should().Be("malformed-fit");
    }

    [Fact]
    public async Task An_empty_stream_yields_no_points_and_is_rejected_downstream()
    {
        await using var content = new MemoryStream();

        (await _parser.ParseAsync(content)).Should().BeEmpty();

        // The normalizer, not the parser, is what refuses a route with too few points.
        var act = () => new RouteNormalizer().Normalize(Guid.NewGuid(), "empty", RoutePacer.Core.Domain.RouteSourceType.Fit, DateTimeOffset.UnixEpoch, []);
        act.Should().Throw<RouteImportException>().Which.Code.Should().Be("too-few-points");
    }

    [Fact]
    public async Task Honours_cancellation_before_decoding()
    {
        await using var content = new MemoryStream([0, 1, 2, 3]);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => _parser.ParseAsync(content, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Semicircle_conversion_matches_the_fit_specification()
    {
        // 2^31 semicircles is exactly 180 degrees.
        const double degreesPerSemicircle = 180d / 2_147_483_648d;

        (1_073_741_824 * degreesPerSemicircle).Should().BeApproximately(90, 1e-9);
        (-2_147_483_648L * degreesPerSemicircle).Should().BeApproximately(-180, 1e-9);
    }
}
