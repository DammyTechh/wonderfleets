using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Application.Common.Helpers;

/// Google encoded polyline algorithm.
public static class Polyline
{
    public static IReadOnlyList<GeoPoint> Decode(string? encoded)
    {
        var points = new List<GeoPoint>();
        if (string.IsNullOrEmpty(encoded)) return points;

        int index = 0, lat = 0, lng = 0;
        while (index < encoded.Length)
        {
            lat += Next(encoded, ref index);
            if (index >= encoded.Length) break;
            lng += Next(encoded, ref index);
            points.Add(new GeoPoint(lat / 1e5, lng / 1e5));
        }
        return points;
    }

    /// Evenly spaced sample (always includes first and last point).
    public static IReadOnlyList<GeoPoint> Sample(IReadOnlyList<GeoPoint> points, int count)
    {
        if (points.Count <= count) return points;
        var result = new List<GeoPoint>(count);
        for (var i = 0; i < count; i++)
            result.Add(points[(int)Math.Round(i * (points.Count - 1) / (double)(count - 1))]);
        return result;
    }

    private static int Next(string encoded, ref int index)
    {
        int result = 0, shift = 0, b;
        do
        {
            b = encoded[index++] - 63;
            result |= (b & 0x1f) << shift;
            shift += 5;
        } while (b >= 0x20 && index < encoded.Length);
        return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
    }
}
