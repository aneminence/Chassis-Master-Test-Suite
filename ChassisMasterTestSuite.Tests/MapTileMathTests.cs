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
}
