namespace Chassis_Master_Test_Suite.Map;

/// <summary>
/// Web Mercator / XYZ tile math (EPSG:3857, slippy map convention).
/// Pure logic 鈥?no WPF dependency; unit-testable on Linux.
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

    /// <summary>Tile indices covering the NW corner of the tile (x west鈫抏ast, y north鈫抯outh).</summary>
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

    /// <summary>Bing Maps quadkey for tile (z,x,y).</summary>

    /// <summary>
    /// Bounding box of XYZ tile in absolute Web Mercator metres:
    /// (west, north, east, south). Tile edges are linear in Mercator — use this
    /// for overlay placement instead of converting lat/lon corners.
    /// </summary>
    public static (double West, double North, double East, double South) TileMercatorBounds(
        int x, int y, int zoom)
    {
        var n = 1 << zoom;
        var world = 2.0 * Math.PI * EarthRadius;
        var tile = world / n;
        var west = -Math.PI * EarthRadius + x * tile;
        var east = west + tile;
        var north = Math.PI * EarthRadius - y * tile;
        var south = north - tile;
        return (west, north, east, south);
    }

    public static string ToQuadKey(int x, int y, int zoom)
    {
        var chars = new char[zoom];
        for (var i = zoom; i > 0; i--)
        {
            var digit = 0;
            var mask = 1 << (i - 1);
            if ((x & mask) != 0)
                digit += 1;
            if ((y & mask) != 0)
                digit += 2;
            chars[zoom - i] = (char)('0' + digit);
        }

        return new string(chars);
    }

    /// <summary>
    /// Suggest zoom so that one tile is roughly <paramref name="targetTilePixels"/> screen pixels wide.
    /// <paramref name="mercatorMetersPerPixel"/> is plot resolution in Web Mercator meters/pixel
    /// (Track Map plot space). Web Mercator tile resolution at z=0 is 156543.03 m/px (independent of latitude).
    /// </summary>
    public static int SuggestZoom(double mercatorMetersPerPixel, double latitude, int targetTilePixels = 256)
    {
        if (mercatorMetersPerPixel <= 0 || double.IsNaN(mercatorMetersPerPixel) || double.IsInfinity(mercatorMetersPerPixel))
            return 15;

        // Keep latitude param for API stability / future use; scale is conformal in mercator metres.
        _ = latitude;
        _ = targetTilePixels;

        const double mercatorMetersPerPixelAtZ0 = 156543.03392;
        var z = Math.Log(mercatorMetersPerPixelAtZ0 / mercatorMetersPerPixel, 2.0);
        return Math.Clamp((int)Math.Round(z), 1, 19);
    }

    public static string FormatUrl(string template, int z, int x, int y)
    {
        if (string.IsNullOrWhiteSpace(template))
            return "";

        var lower = template.ToLowerInvariant();
        // Digit subdomains: Google mt{s}, Bing t{s}, Tianditu t{s}
        var useDigitSubdomain =
            lower.Contains("tianditu", StringComparison.Ordinal) ||
            lower.Contains("google.com", StringComparison.Ordinal) ||
            lower.Contains("virtualearth.net", StringComparison.Ordinal) ||
            lower.Contains("mt{s}", StringComparison.Ordinal) ||
            lower.Contains("t{s}.", StringComparison.Ordinal);

        var sLetter = ((char)('a' + (Math.Abs(x + y) % 4))).ToString();
        var sDigit = (Math.Abs(x + y) % 4).ToString(); // 0鈥? covers Google/Bing
        var s = useDigitSubdomain ? sDigit : sLetter;
        var quad = ToQuadKey(x, y, z);

        return template
            .Replace("{s}", s, StringComparison.OrdinalIgnoreCase)
            .Replace("{q}", quad, StringComparison.OrdinalIgnoreCase)
            .Replace("{quadkey}", quad, StringComparison.OrdinalIgnoreCase)
            .Replace("{z}", z.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{x}", x.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{y}", y.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{Z}", z.ToString(), StringComparison.Ordinal)
            .Replace("{X}", x.ToString(), StringComparison.Ordinal)
            .Replace("{Y}", y.ToString(), StringComparison.Ordinal);
    }
}
