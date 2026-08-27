using FluentAssertions;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Tracking;

namespace RoutePacer.Core.Tests.Tracking;

public sealed class RouteMatcherTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private readonly RouteMatcher _matcher = new();

    private static GeoFix Fix(double latitude, double longitude) => new(Now, latitude, longitude, 5, null);

    [Fact]
    public void Match_projects_onto_segment_not_nearest_vertex()
    {
        // Halfway along the first 111 m segment, one metre to the north.
        var match = _matcher.Match(RouteFixtures.Straight(), Fix(0.000009, 0.0005), null);

        match.Should().NotBeNull();
        match!.SegmentIndex.Should().Be(0);
        match.ProjectionRatio.Should().BeApproximately(0.5, 0.01);
        match.RouteDistanceMeters.Should().BeApproximately(55.6, 1);
        match.CrossTrackErrorMeters.Should().BeApproximately(1, 0.2);
    }

    [Fact]
    public void Match_clamps_before_the_start_of_the_route()
    {
        var match = _matcher.Match(RouteFixtures.Straight(), Fix(0, -0.0005), null);

        match!.SegmentIndex.Should().Be(0);
        match.ProjectionRatio.Should().Be(0);
        match.RouteDistanceMeters.Should().Be(0);
    }

    [Fact]
    public void Match_clamps_beyond_the_finish()
    {
        var route = RouteFixtures.Straight();
        var match = _matcher.Match(route, Fix(0, 0.0105), null);

        match!.ProjectionRatio.Should().Be(1);
        match.RouteDistanceMeters.Should().BeApproximately(route.Summary.TotalDistanceMeters, 0.5);
    }

    [Fact]
    public void Match_near_the_finish_with_a_previous_index_does_not_overrun_the_segment_array()
    {
        var route = RouteFixtures.Straight(50);
        var lastSegment = route.Points.Count - 2;

        var match = _matcher.Match(route, Fix(0, 48 * 0.001), lastSegment);

        match.Should().NotBeNull();
    }

    [Fact]
    public void Match_returns_null_when_cross_track_error_exceeds_the_maximum()
        => _matcher.Match(RouteFixtures.Straight(), Fix(0.01, 0.0005), null).Should().BeNull();

    [Fact]
    public void Match_uses_the_window_around_the_previous_segment()
    {
        var route = RouteFixtures.Straight(500);
        var match = _matcher.Match(route, Fix(0, 0.25), 249);

        match!.SegmentIndex.Should().Be(249);
    }

    [Fact]
    public void Match_falls_back_to_a_full_scan_when_the_window_is_far_away()
    {
        var route = RouteFixtures.Straight(500);

        // Physically mid-segment 400 but the last match was segment 0, well outside the ±100 window.
        var match = _matcher.Match(route, Fix(0, 400.5 * 0.001), 0);

        match!.SegmentIndex.Should().Be(400);
    }

    [Fact]
    public void Match_prefers_forward_continuity_at_a_crossing()
    {
        var route = RouteFixtures.OutAndBack();
        var onTheLine = Fix(0.00001, 0.005);

        // Both legs are within the tie tolerance; the outbound leg is the forward continuation.
        var match = _matcher.Match(route, onTheLine, 4);

        match!.SegmentIndex.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Match_skips_zero_length_segments()
    {
        var id = Guid.NewGuid();
        var points = new List<RoutePoint>
        {
            new(id, 0, 0, 0.000, null, 0, 0, null),
            new(id, 1, 0, 0.000, null, 1, 1, null),   // duplicate coordinate, non-zero cumulative distance
            new(id, 2, 0, 0.001, null, 111.2, 11, null),
        };
        var route = new RouteTrack(new RouteSummary(id, "z", RouteSourceType.Gpx, Now, 111.2, 11, 3, 0, 0, 0, 0.001), points);

        _matcher.Match(route, Fix(0, 0.0005), null).Should().NotBeNull();
    }

    [Fact]
    public void Match_honours_a_custom_maximum_cross_track()
    {
        var strict = new RouteMatcher(new RouteMatcherOptions(MaximumCrossTrackMeters: 0.5));

        strict.Match(RouteFixtures.Straight(), Fix(0.000018, 0.0005), null).Should().BeNull();
    }
}
