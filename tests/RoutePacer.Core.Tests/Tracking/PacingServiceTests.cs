using FluentAssertions;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Tracking;

namespace RoutePacer.Core.Tests.Tracking;

public sealed class PacingServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private readonly PacingService _pacer = new();

    [Theory]
    [InlineData(90, 100, -10)]
    [InlineData(110, 100, 10)]
    [InlineData(100, 100, 0)]
    public void Delta_time_is_live_minus_target(double live, double target, double expected)
        => PacingService.DeltaTime(live, target).Should().Be(expected);

    [Fact]
    public void Ahead_of_schedule_yields_a_negative_time_delta()
    {
        var route = RouteFixtures.Straight(metresPerSecond: 10);
        // 500 m along a 10 m/s route is due at 50 s; the rider is there after 40 s.
        var snapshot = _pacer.Calculate(route, new MatchedPosition(4, 500, 2, 0.5), Start, Fix(40));

        snapshot.TargetElapsedSeconds.Should().BeApproximately(50, 0.5);
        snapshot.DeltaTimeSeconds.Should().BeApproximately(-10, 0.5);
        snapshot.DeltaDistanceMeters.Should().BeApproximately(100, 2);
    }

    [Fact]
    public void Behind_schedule_yields_a_positive_time_delta()
    {
        var route = RouteFixtures.Straight(metresPerSecond: 10);
        var snapshot = _pacer.Calculate(route, new MatchedPosition(4, 500, 2, 0.5), Start, Fix(70));

        snapshot.DeltaTimeSeconds.Should().BeApproximately(20, 0.5);
        snapshot.DeltaDistanceMeters.Should().BeApproximately(-200, 2);
    }

    [Fact]
    public void Live_elapsed_never_goes_negative()
        => _pacer.Calculate(RouteFixtures.Straight(), new MatchedPosition(0, 0, 1, 0), Start, Fix(-30))
            .LiveElapsed.Should().Be(TimeSpan.Zero);

    [Fact]
    public void Untimed_routes_report_no_timing_fields_but_keep_match_and_speed()
    {
        var match = new MatchedPosition(4, 500, 7, 0.5);
        var snapshot = _pacer.Calculate(RouteFixtures.Straight(timed: false), match, Start, Fix(40, speed: 8));

        snapshot.TargetElapsedSeconds.Should().BeNull();
        snapshot.DeltaTimeSeconds.Should().BeNull();
        snapshot.ExpectedDistanceMeters.Should().BeNull();
        snapshot.DeltaDistanceMeters.Should().BeNull();
        snapshot.Match.Should().Be(match);
        snapshot.SpeedMps.Should().Be(8);
        snapshot.LiveElapsed.Should().Be(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void Overrunning_the_finish_clamps_the_expected_distance()
    {
        var route = RouteFixtures.Straight(metresPerSecond: 10);
        var snapshot = _pacer.Calculate(route, new MatchedPosition(9, route.Summary.TotalDistanceMeters, 1, 1), Start, Fix(100_000));

        snapshot.ExpectedDistanceMeters.Should().BeApproximately(route.Summary.TotalDistanceMeters, 0.5);
        snapshot.DeltaDistanceMeters.Should().BeApproximately(0, 0.5);
    }

    private static GeoFix Fix(double secondsFromStart, double? speed = null)
        => new(Start.AddSeconds(secondsFromStart), 0, 0, 5, speed);
}
