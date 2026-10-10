namespace Chassis_Master_Test_Suite.Map;

/// <summary>
/// Web Mercator / XYZ tile math (EPSG:3857, slippy map convention).
/// Pure logic — no WPF dependency; unit-testable on Linux.
/// </summary>
public static class MapTileMath
{
    public const int TileSize = 256;
    public const double EarthRadius = 6378137.0;
    public const double MaxLatitude = 85.05112878;

    public static (double X, double Y) LatLonToMercator(double latitude, double longitude)
    {
        var clamped = Math.Clamp(latitude, -MaxLatitude, MaxLatitude);
        var x = longitude * Math.PI / 180.0 * EarthRadius;
        var y = Math.Log(Math.Tan((90.0 + clamped) * Math.PI / 360.0)) * EarthRadius;
        return (x, y);
    }

    public static (double Latitude, double Longitude) MercatorToLatLon(double x, double y)
    {
        var longitude = x / EarthRadius * 180.0 / Math.PI;
        var latitude = 90.0 - 2.0 * Math.Atan(Math.Exp(-y / EarthRadius)) * 180.0 / Math.PI;
        return (latitude, longitude);
    }

    /// <summary>Tile indices covering the NW corner of the tile (x west→east, y north→south).</summary>
    public static (int X, int Y) LatLonToTile(double latitude, double longitude, int zoom)
    {
        var n = 1 << zoom;
        var x = (int)Math.Floor((longitude + 180.0) / 360.0 * n);
        var latRad = Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180.0;
        var y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        x = ((x % n) + n) % n;
        y = Math.Clamp(y, 0, n - 1);
        return (x, y);
    }

    /// <summary>NW corner of tile (z,x,y) in WGS84 degrees.</summary>
    public static (double Latitude, double Longitude) TileNwLatLon(int x, int y, int zoom)
    {
        var n = 1 << zoom;
        var lon = x / (double)n * 360.0 - 180.0;
        var latRad = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * y / n)));
        var lat = latRad * 180.0 / Math.PI;
        return (lat, lon);
    }

    /// <summary>Bounding box of tile in WGS84: (south, west, north, east).</summary>
    public static (double South, double West, double North, double East) TileBounds(
        int x, int y, int zoom)
    {
        var (north, west) = TileNwLatLon(x, y, zoom);
        var (south, east) = TileNwLatLon(x + 1, y + 1, zoom);
        return (south, west, north, east);
    }

    /// <summary>
    /// Suggest zoom so that one tile is roughly <paramref name="targetTilePixels"/> screen pixels wide.
    /// </summary>
    public static int SuggestZoom(double metersPerPixel, double latitude, int targetTilePixels = 256)
    {
        if (metersPerPixel <= 0 || double.IsNaN(metersPerPixel) || double.IsInfinity(metersPerPixel))
            return 15;

        var cos = Math.Max(0.05, Math.Cos(latitude * Math.PI / 180.0));
        // meters per pixel at equator for zoom z ≈ 156543.03 / 2^z
        // at latitude: / cos(lat)
        var metersPerPixelAtZ0 = 156543.03392 / cos;
        var desiredMpp = metersPerPixel; // local plot meters ≈ ground meters for short tracks
        var z = Math.Log(metersPerPixelAtZ0 / desiredMpp, 2.0);
        return Math.Clamp((int)Math.Round(z), 1, 19);
    }

    public static string FormatUrl(string template, int z, int x, int y)
    {
        if (string.IsNullOrWhiteSpace(template))
            return "";

        return template
            .Replace("{z}", z.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{x}", x.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{y}", y.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{Z}", z.ToString(), StringComparison.Ordinal)
            .Replace("{X}", x.ToString(), StringComparison.Ordinal)
            .Replace("{Y}", y.ToString(), StringComparison.Ordinal);
    }
}
