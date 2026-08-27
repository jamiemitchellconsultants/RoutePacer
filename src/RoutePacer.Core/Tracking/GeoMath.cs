namespace RoutePacer.Core.Tracking;

public static class GeoMath
{
    public const double EarthRadiusMeters = 6_371_008.8;

    public static double HaversineMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        var lat1 = double.DegreesToRadians(latitude1);
        var lat2 = double.DegreesToRadians(latitude2);
        var dLat = lat2 - lat1;
        var dLon = double.DegreesToRadians(longitude2 - longitude1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static (double X, double Y) ToLocalMeters(double latitude, double longitude, double originLatitude, double originLongitude)
    {
        var lat = double.DegreesToRadians(latitude);
        var originLat = double.DegreesToRadians(originLatitude);
        var dLon = double.DegreesToRadians(((longitude - originLongitude) + 540) % 360 - 180);
        var dLat = lat - originLat;
        return (dLon * Math.Cos(originLat) * EarthRadiusMeters, dLat * EarthRadiusMeters);
    }
}
