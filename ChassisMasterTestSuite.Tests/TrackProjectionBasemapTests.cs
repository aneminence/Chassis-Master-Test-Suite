using Chassis_Master_Test_Suite.Controls;
using Chassis_Master_Test_Suite.Map;

namespace ChassisMasterTestSuite.Tests;

public class TrackProjectionBasemapTests
{
    [Fact]
    public void ToMeters_MatchesLocalWebMercator()
    {
        var originLat = 31.2304;
        var originLon = 121.4737;
        var proj = new TrackProjection(originLat, originLon);

        var lat = 31.2404;
        var lon = 121.4837;
        var (x, y) = proj.ToMeters(lat, lon);

        var (mx0, my0) = TrackProjection.ToMercator(originLat, originLon);
        var (mx1, my1) = TrackProjection.ToMercator(lat, lon);
        Assert.InRange(x, mx1 - mx0 - 1e-6, mx1 - mx0 + 1e-6);
        Assert.InRange(y, my1 - my0 - 1e-6, my1 - my0 + 1e-6);
    }

    [Fact]
    public void ToLatLon_RoundTripsThroughLocalMercator()
    {
        var proj = new TrackProjection(33.0, 120.0);
        var (x, y) = proj.ToMeters(33.01, 120.02);
        var (lat, lon) = proj.ToLatLon(x, y);
        Assert.InRange(lat, 33.009999, 33.010001);
        Assert.InRange(lon, 120.019999, 120.020001);
    }

    [Fact]
    public void TileCorner_AndTrackPoint_SharePlotSpace()
    {
        // A GPS point that sits on a tile NW corner must map to the same plot
        // coordinate whether taken from the track path or from tile placement.
        var proj = new TrackProjection(31.2, 121.5);
        const int z = 15;
        var (tx, ty) = MapTileMath.LatLonToTile(31.2, 121.5, z);
        var (north, west) = MapTileMath.TileNwLatLon(tx, ty, z);

        var fromTrack = proj.ToMeters(north, west);
        var (south, west2, north2, east) = MapTileMath.TileBounds(tx, ty, z);
        Assert.Equal(north, north2);
        Assert.Equal(west, west2);

        var tileNw = proj.ToMeters(north2, west2);
        Assert.InRange(tileNw.X, fromTrack.X - 1e-6, fromTrack.X + 1e-6);
        Assert.InRange(tileNw.Y, fromTrack.Y - 1e-6, fromTrack.Y + 1e-6);

        // Tile is square in mercator / local mercator plot space.
        var tileSe = proj.ToMeters(south, east);
        var w = Math.Abs(tileSe.X - tileNw.X);
        var h = Math.Abs(tileSe.Y - tileNw.Y);
        Assert.InRange(w / h, 0.999, 1.001);
    }

    [Fact]
    public void MercatorTile_MidpointLatitude_IsNotLinearInPixelY()
    {
        // Root cause of zoom-out offset with ENU placement:
        // tile pixel rows are linear in Mercator Y, not in latitude.
        // Stretching a Mercator PNG into a lat-linear (ENU) NW–SE box
        // shifts features inside the tile; error grows as the tile spans more latitude.
        const int z = 5;
        var (tx, ty) = MapTileMath.LatLonToTile(31.2, 121.5, z);
        var (south, west, north, east) = MapTileMath.TileBounds(tx, ty, z);
        _ = (west, east);

        var (mxN, myN) = MapTileMath.LatLonToMercator(north, west);
        var (mxS, myS) = MapTileMath.LatLonToMercator(south, west);
        _ = mxN;
        var myMid = (myN + myS) / 2.0; // image vertical midpoint in Mercator
        var (latAtMercMid, _) = MapTileMath.MercatorToLatLon(mxS, myMid);
        var latLinearMid = (north + south) / 2.0;

        Assert.True(Math.Abs(latAtMercMid - latLinearMid) > 0.01,
            $"Mercator mid lat {latAtMercMid} should differ from linear mid {latLinearMid}");
    }
}
