using Chassis_Master_Test_Suite.Map;

namespace ChassisMasterTestSuite.Tests;

public class MapTileMathTests
{
    [Fact]
    public void LatLonToTile_KnownOsmCenter()
    {
        // Roughly central London at z=12
        var (x, y) = MapTileMath.LatLonToTile(51.5074, -0.1278, 12);
        Assert.InRange(x, 2000, 2100);
        Assert.InRange(y, 1300, 1400);
    }

    [Fact]
    public void TileBounds_RoundTripNwCorner()
    {
        var (lat, lon) = MapTileMath.TileNwLatLon(2048, 1361, 12);
        var (x, y) = MapTileMath.LatLonToTile(lat - 0.0001, lon + 0.0001, 12);
        Assert.Equal(2048, x);
        Assert.Equal(1361, y);
    }

    [Fact]
    public void FormatUrl_ReplacesSubdomain()
    {
        var url = MapTileMath.FormatUrl("https://{s}.example/{z}/{x}/{y}.png", 3, 4, 5);
        Assert.DoesNotContain("{s}", url);
        Assert.Contains("/3/4/5.png", url);
    }

    [Fact]
    public void FormatUrl_ReplacesTokens()
    {
        var url = MapTileMath.FormatUrl("https://example/{z}/{x}/{y}.png", 3, 4, 5);
        Assert.Equal("https://example/3/4/5.png", url);
    }

    [Fact]
    public void SuggestZoom_IncreasesWhenZoomedIn()
    {
        var far = MapTileMath.SuggestZoom(metersPerPixel: 50, latitude: 30);
        var near = MapTileMath.SuggestZoom(metersPerPixel: 0.5, latitude: 30);
        Assert.True(near > far);
    }

    [Fact]
    public void Mercator_RoundTrip()
    {
        var (x, y) = MapTileMath.LatLonToMercator(31.2, 121.5);
        var (lat, lon) = MapTileMath.MercatorToLatLon(x, y);
        Assert.InRange(lat, 31.19, 31.21);
        Assert.InRange(lon, 121.49, 121.51);
    }

    [Fact]
    public void FormatUrl_BingQuadKey()
    {
        var url = MapTileMath.FormatUrl(
            "https://ecn.t{s}.tiles.virtualearth.net/tiles/a{q}.jpeg?g=1",
            4, 12, 6);
        Assert.DoesNotContain("{q}", url);
        Assert.DoesNotContain("{s}", url);
        Assert.Contains("/tiles/a", url);
        Assert.Contains(".jpeg", url);
        Assert.Equal(MapTileMath.ToQuadKey(12, 6, 4), url.Split("/tiles/a")[1].Split('.')[0]);
    }

    [Fact]
    public void FormatUrl_GoogleUsesDigitSubdomain()
    {
        var url = MapTileMath.FormatUrl(
            "https://mt{s}.google.com/vt/lyrs=s&x={x}&y={y}&z={z}",
            4, 12, 6);
        Assert.StartsWith("https://mt", url);
        Assert.Contains(".google.com", url);
        Assert.Contains("x=12", url);
        Assert.Contains("y=6", url);
        Assert.Contains("z=4", url);
        var afterMt = url["https://mt".Length];
        Assert.InRange(afterMt, '0', '3');
    }

    [Fact]
    public void ToQuadKey_KnownValue()
    {
        Assert.Equal("0", MapTileMath.ToQuadKey(0, 0, 1));
        Assert.Equal(3, MapTileMath.ToQuadKey(1, 2, 3).Length);
    }

    [Fact]
    public void LooksLikeImage_JpegAndPng()
    {
        var jpeg = new byte[32];
        jpeg[0] = 0xFF; jpeg[1] = 0xD8; jpeg[2] = 0xFF; jpeg[3] = 0xE0;
        Assert.True(MapTileCache.LooksLikeImage(jpeg));

        var png = new byte[32];
        png[0] = 0x89; png[1] = 0x50; png[2] = 0x4E; png[3] = 0x47;
        Assert.True(MapTileCache.LooksLikeImage(png));

        Assert.False(MapTileCache.LooksLikeImage(
            System.Text.Encoding.ASCII.GetBytes("<html>error page not an image!!")));
    }
}
