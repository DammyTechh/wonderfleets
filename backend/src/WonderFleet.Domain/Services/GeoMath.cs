namespace WonderFleet.Domain.Services;

public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double deg) => deg * Math.PI / 180d;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// The firmware reports 0,0 when it has no GPS fix; that point is in the Gulf of Guinea, never on a Nigerian road.
    public static bool IsValidFix(double? lat, double? lng) =>
        lat is not null && lng is not null
        && !(Math.Abs(lat.Value) < 0.0001 && Math.Abs(lng.Value) < 0.0001)
        && lat is >= -90 and <= 90 && lng is >= -180 and <= 180;
}
