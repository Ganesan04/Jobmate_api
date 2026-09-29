namespace Jobmate.Domain.ValueObjects;

/// <summary>
/// Great-circle distance helper. Used for in-process fallbacks and unit tests; the
/// production job search performs the same maths in SQL (see
/// <c>JobSearchService</c>) so distances are filtered by the index, not in memory.
/// </summary>
public static class GeoDistance
{
    /// <summary>Mean Earth radius in kilometres.</summary>
    public const double EarthRadiusKm = 6371.0088;

    /// <summary>Haversine distance in kilometres between two WGS-84 points.</summary>
    public static double Between(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) +
                (Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                 Math.Sin(dLon / 2) * Math.Sin(dLon / 2));

        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static bool IsValidLatitude(double value) => value is >= -90 and <= 90;

    public static bool IsValidLongitude(double value) => value is >= -180 and <= 180;

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
