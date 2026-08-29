using FluentAssertions;
using RoutePacer.App.Browser;
using RoutePacer.App.Formatting;

namespace RoutePacer.App.Tests.Formatting;

public sealed class RideFormatTests
{
    [Theory]
    [InlineData(-123, "2:03 ahead")]
    [InlineData(45, "behind 0:45")]
    [InlineData(0, "On pace")]
    [InlineData(0.4, "On pace")]
    [InlineData(-0.4, "On pace")]
    public void Time_delta_is_signed_and_labelled(double seconds, string expected)
        => RideFormat.TimeDelta(seconds).Should().Be(expected);

    [Fact]
    public void A_null_time_delta_says_timing_is_unavailable()
        => RideFormat.TimeDelta(null).Should().Be("Timing unavailable");

    [Theory]
    [InlineData(-120, "120 m ahead")]
    [InlineData(85, "behind 85 m")]
    [InlineData(0, "0 m")]
    public void Distance_delta_is_signed_and_labelled(double metres, string expected)
        => RideFormat.Delta(metres, "m").Should().Be(expected);

    [Fact]
    public void A_null_distance_delta_is_a_dash_not_a_timing_message()
        => RideFormat.Delta(null, "m").Should().Be("—");

    [Theory]
    [InlineData(null, "neutral")]
    [InlineData(0.0, "neutral")]
    [InlineData(-5.0, "ahead")]
    [InlineData(5.0, "behind")]
    public void Tone_matches_the_written_label(double? value, string expected)
        => RideFormat.DeltaTone(value).Should().Be(expected);

    [Theory]
    [InlineData(10, "36.0 km/h")]
    [InlineData(0, "0.0 km/h")]
    public void Speed_converts_to_kilometres_per_hour(double metresPerSecond, string expected)
        => RideFormat.Speed(metresPerSecond).Should().Be(expected);

    [Fact]
    public void Speed_without_a_value_is_a_dash() => RideFormat.Speed(null).Should().Be("—");

    [Theory]
    [InlineData(0, "0:00:00")]
    [InlineData(63, "0:01:03")]
    [InlineData(3723, "1:02:03")]
    public void Elapsed_uses_hours_minutes_and_seconds(int seconds, string expected)
        => RideFormat.Elapsed(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Theory]
    [InlineData(5, "Good")]
    [InlineData(10, "Good")]
    [InlineData(10.1, "Fair")]
    [InlineData(30, "Fair")]
    [InlineData(30.1, "Poor")]
    public void Accuracy_uses_the_documented_thresholds(double metres, string expected)
        => RideFormat.Accuracy(metres).Should().Be(expected);

    [Fact]
    public void Accuracy_without_a_value_is_a_dash() => RideFormat.Accuracy(null).Should().Be("—");

    [Theory]
    [InlineData(-100, 1000, "0%")]
    [InlineData(500, 1000, "50%")]
    [InlineData(5000, 1000, "100%")]
    public void Progress_is_clamped_to_the_route(double distance, double total, string expected)
        => RideFormat.Progress(distance, total).Should().Be(expected);

    [Fact]
    public void Progress_on_a_zero_length_route_is_a_dash()
        => RideFormat.Progress(10, 0).Should().Be("—");

    [Theory]
    [InlineData(1, "1 point this ride")]
    [InlineData(42, "42 points this ride")]
    public void Saved_point_count_is_pluralised(long points, string expected)
        => RideFormat.Points(points).Should().Be(expected);

    [Theory]
    [InlineData(WakeLockStatus.Acquired, "Screen kept awake")]
    [InlineData(WakeLockStatus.Unsupported, "Screen wake lock unavailable")]
    public void Wake_status_is_described_in_words(WakeLockStatus status, string expected)
        => RideFormat.Wake(status).Should().Be(expected);

    // The tracker carries no red or green, so this placement is the only thing telling a rider
    // which side of the plan they are on. If both ever read the same way round, the screen stops
    // distinguishing ahead from behind at all.
    [Fact]
    public void Ahead_trails_the_number_and_behind_leads_it()
    {
        RideFormat.TimeDelta(-123).Should().EndWith("ahead");
        RideFormat.TimeDelta(45).Should().StartWith("behind");
        RideFormat.Delta(-120, "m").Should().EndWith("ahead");
        RideFormat.Delta(85, "m").Should().StartWith("behind");
    }

    [Fact]
    public void Neither_reading_leans_on_a_sign_character()
    {
        // A minus sign is easy to miss at a glance and is not what the rider is being asked to read.
        RideFormat.TimeDelta(-123).Should().NotContain("-");
        RideFormat.Delta(-120, "m").Should().NotContain("-");
    }
}
