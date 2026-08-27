using FluentAssertions;
using RoutePacer.Core.Tracking;

namespace RoutePacer.Core.Tests.Tracking;

public sealed class GeoMathTests
{
    [Theory]
    [InlineData(51.5074, -0.1278, 51.5074, -0.1278, 0)]
    [InlineData(0, 0, 0, 0.001, 111.195)]
    [InlineData(0, 0, 0.001, 0, 111.195)]
    public void Haversine_returns_expected_metres(double lat1, double lon1, double lat2, double lon2, double expected)
        => GeoMath.HaversineMeters(lat1, lon1, lat2, lon2).Should().BeApproximately(expected, 0.2);

    [Fact]
    public void Haversine_is_symmetric()
        => GeoMath.HaversineMeters(51.5, -0.12, 48.85, 2.35)
            .Should().BeApproximately(GeoMath.HaversineMeters(48.85, 2.35, 51.5, -0.12), 1e-6);

    [Fact]
    public void Local_frame_origin_maps_to_zero()
    {
        var (x, y) = GeoMath.ToLocalMeters(51.5, -0.12, 51.5, -0.12);
        x.Should().BeApproximately(0, 1e-9);
        y.Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void Local_frame_wraps_across_the_antimeridian()
    {
        // One degree apart across 180°, not 359 degrees the long way round.
        var (x, _) = GeoMath.ToLocalMeters(0, -179.999, 0, 179.999);
        Math.Abs(x).Should().BeLessThan(500);
    }

    [Fact]
    public void Local_frame_shrinks_longitude_with_latitude()
    {
        var equator = Math.Abs(GeoMath.ToLocalMeters(0, 0.01, 0, 0).X);
        var high = Math.Abs(GeoMath.ToLocalMeters(60, 0.01, 60, 0).X);
        high.Should().BeApproximately(equator * Math.Cos(double.DegreesToRadians(60)), 1);
    }
}
