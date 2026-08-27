using FluentAssertions;
using RoutePacer.App.Rides;
using RoutePacer.Core.Domain;

namespace RoutePacer.App.Tests.Rides;

public sealed class GpsSpikeFilterTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private readonly GpsSpikeFilter _filter = new();

    private static GeoFix Fix(double seconds, double longitude, double accuracy = 5, double? speed = null)
        => new(Start.AddSeconds(seconds), 0, longitude, accuracy, speed);

    [Fact]
    public void Accepts_the_first_valid_fix() => _filter.Accept(Fix(0, 0)).Should().BeTrue();

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void Rejects_invalid_coordinates(double latitude, double longitude)
        => _filter.Accept(new GeoFix(Start, latitude, longitude, 5, null)).Should().BeFalse();

    [Fact]
    public void Rejects_accuracy_worse_than_one_hundred_metres()
    {
        _filter.Accept(Fix(0, 0, accuracy: 100)).Should().BeTrue();
        _filter.Accept(Fix(1, 0.0001, accuracy: 100.1)).Should().BeFalse();
    }

    [Fact]
    public void Rejects_non_increasing_timestamps()
    {
        _filter.Accept(Fix(10, 0)).Should().BeTrue();
        _filter.Accept(Fix(10, 0.0001)).Should().BeFalse();
        _filter.Accept(Fix(9, 0.0002)).Should().BeFalse();
    }

    [Fact]
    public void Rejects_an_implausible_jump_the_browser_speed_contradicts()
    {
        _filter.Accept(Fix(0, 0)).Should().BeTrue();
        // 5 km in one second, while the browser reports a plausible 8 m/s.
        _filter.Accept(Fix(1, 0.045, speed: 8)).Should().BeFalse();
    }

    [Fact]
    public void Rejects_an_implausible_jump_when_the_browser_reports_no_speed()
    {
        _filter.Accept(Fix(0, 0)).Should().BeTrue();
        _filter.Accept(Fix(1, 0.045)).Should().BeFalse();
    }

    [Fact]
    public void Accepts_fast_movement_the_browser_speed_corroborates()
    {
        _filter.Accept(Fix(0, 0)).Should().BeTrue();
        // About 44 m/s, and the browser agrees, so this is a descent rather than a spike.
        _filter.Accept(Fix(1, 0.0004, speed: 44)).Should().BeTrue();
    }

    [Fact]
    public void A_rejected_fix_does_not_become_the_new_reference()
    {
        _filter.Accept(Fix(0, 0)).Should().BeTrue();
        _filter.Accept(Fix(1, 0.045)).Should().BeFalse();
        // Still measured against the fix at longitude 0, so a normal step is accepted.
        _filter.Accept(Fix(2, 0.0001)).Should().BeTrue();
    }
}
