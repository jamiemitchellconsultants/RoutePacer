using FluentAssertions;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Tracking;

namespace RoutePacer.Core.Tests.Tracking;

public sealed class StationaryDetectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    private readonly StationaryDetector _detector = new();

    private static GeoFix Fix(double seconds, double longitude)
        => new(Start.AddSeconds(seconds), 0, longitude, 5, null);

    [Fact]
    public void A_fresh_detector_is_not_anchored_and_reports_nothing()
    {
        _detector.IsAnchored.Should().BeFalse();
        _detector.MetersFromAnchor(Fix(0, 0)).Should().Be(0);
        _detector.StationaryTime(Fix(0, 0)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void The_first_fix_anchors_and_reports_no_time_yet()
    {
        _detector.Observe(Fix(0, 0)).Should().Be(TimeSpan.Zero);
        _detector.IsAnchored.Should().BeTrue();
    }

    [Fact]
    public void Time_accumulates_while_the_rider_stays_inside_the_stationary_radius()
    {
        _detector.Observe(Fix(0, 0));

        // 0.00005 deg is 5.6 m, inside the 10 m radius.
        _detector.Observe(Fix(30, 0.00005)).Should().Be(TimeSpan.FromSeconds(30));
        _detector.Observe(Fix(90, -0.00005)).Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void Leaving_the_stationary_radius_re_anchors_and_restarts_the_clock()
    {
        _detector.Observe(Fix(0, 0));
        _detector.Observe(Fix(60, 0.00005)).Should().Be(TimeSpan.FromSeconds(60));

        // 0.00015 deg is 16.7 m, outside the 10 m radius.
        _detector.Observe(Fix(90, 0.00015)).Should().Be(TimeSpan.Zero);
        _detector.Observe(Fix(120, 0.00015)).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Displacement_is_measured_from_the_anchor_without_moving_it()
    {
        _detector.Observe(Fix(0, 0));

        // 0.00011 deg is 12.2 m: past the stationary radius, short of the resume radius.
        _detector.MetersFromAnchor(Fix(30, 0.00011)).Should().BeApproximately(12.2, 0.3);
        _detector.MetersFromAnchor(Fix(60, 0.00015)).Should().BeApproximately(16.7, 0.3);

        // Neither reading disturbed the anchor, so time still runs from the original fix.
        _detector.StationaryTime(Fix(60, 0.00015)).Should().Be(TimeSpan.FromSeconds(60));
    }

    // The gap between the two radii is what stops a phone drifting on GPS noise from flapping
    // between paused and running, which a rider reads as the number flickering for no reason.
    [Fact]
    public void The_band_between_the_two_radii_is_neither_moving_nor_a_reason_to_re_anchor()
    {
        _detector.Observe(Fix(0, 0));
        var drifting = Fix(45, 0.00011);

        _detector.MetersFromAnchor(drifting).Should().BeGreaterThan(StationaryDetector.StationaryRadiusMeters);
        _detector.MetersFromAnchor(drifting).Should().BeLessThan(StationaryDetector.ResumeRadiusMeters);
        _detector.StationaryTime(drifting).Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Stationary_time_never_goes_negative_when_a_fix_arrives_out_of_order()
    {
        _detector.Observe(Fix(60, 0));

        _detector.StationaryTime(Fix(10, 0)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Reset_forgets_the_anchor()
    {
        _detector.Observe(Fix(0, 0));

        _detector.Reset();

        _detector.IsAnchored.Should().BeFalse();
        _detector.Observe(Fix(30, 0)).Should().Be(TimeSpan.Zero);
    }
}
