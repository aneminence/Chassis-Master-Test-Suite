using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class SlopeEnricherTests
{
    private static VehicleSample SampleAt(
        int index,
        double latDeg,
        double lonDeg,
        double altM,
        long timestampMs = 0)
    {
        var channels = VehicleSample.BuildCoreChannels(
            speedKph: 0,
            longitudinalAcceleration: 0,
            lateralAcceleration: 0,
            verticalAcceleration: 0,
            yawRate: 0,
            heading: 0,
            latitude: latDeg,
            longitude: lonDeg,
            altitude: altM);

        return new VehicleSample
        {
            Timestamp = timestampMs == 0 ? index * 1000L : timestampMs,
            Sequence = index,
            SpeedKph = 0,
            Latitude = latDeg,
            Longitude = lonDeg,
            Altitude = altM,
            Channels = channels
        };
    }

    /// <summary>
    /// ~10 m north, +1 m altitude → about 10% grade after SampleEnricher distance.
    /// 1 deg latitude ≈ 111_320 m → 10 m ≈ 10/111320 deg.
    /// </summary>
    [Fact]
    public void EnrichAll_WindowedGrade_ApproximatelyTenPercent()
    {
        const double metersPerDegLat = 111_320.0;
        var lat0 = 39.0;
        var lon0 = 117.0;
        var raw = new List<VehicleSample>();
        // Dense samples every 1 m for 20 m climb of 2 m total (10% average).
        for (var i = 0; i <= 20; i++)
        {
            var northM = i;
            var alt = 100.0 + i * 0.1; // 0.1 m rise per 1 m run = 10%
            raw.Add(SampleAt(
                i,
                lat0 + northM / metersPerDegLat,
                lon0,
                alt));
        }

        var enriched = SlopeEnricher.EnrichAll(SampleEnricher.EnrichAll(raw));

        Assert.Equal(raw.Count, enriched.Count);
        Assert.True(enriched[^1].Channels.ContainsKey(ChannelIds.SlopePct));

        // After ~10 m window, grade should be near 10%.
        var mid = enriched[15];
        Assert.True(mid.Channels.TryGetValue(ChannelIds.SlopePct, out var grade));
        Assert.False(double.IsNaN(grade));
        Assert.InRange(grade, 8.0, 12.0);
    }

    [Fact]
    public void EnrichAll_FlatTrack_YieldsNearZeroGrade()
    {
        const double metersPerDegLat = 111_320.0;
        var raw = new List<VehicleSample>();
        for (var i = 0; i <= 20; i++)
        {
            raw.Add(SampleAt(
                i,
                39.0 + i / metersPerDegLat,
                117.0,
                altM: 200.0));
        }

        var enriched = SlopeEnricher.EnrichAll(SampleEnricher.EnrichAll(raw));
        var grade = enriched[^1].Channels[ChannelIds.SlopePct];
        Assert.False(double.IsNaN(grade));
        Assert.InRange(grade, -0.5, 0.5);
    }

    [Fact]
    public void TryComputeGrade_ZeroDeltaS_ReturnsFalse()
    {
        var distances = new[] { 5.0, 5.0, 5.0 };
        var heights = new[] { 10.0, 11.0, 12.0 };

        var ok = SlopeEnricher.TryComputeGrade(
            distances,
            heights,
            index: 2,
            windowMeters: 10.0,
            out var grade);

        Assert.False(ok);
        Assert.True(double.IsNaN(grade));
    }

    [Fact]
    public void EnrichAll_ZeroDeltaS_HoldsLastGood()
    {
        // First move 10 m with +1 m alt (10%), then duplicate position (Δs=0).
        const double metersPerDegLat = 111_320.0;
        var raw = new List<VehicleSample>
        {
            SampleAt(0, 39.0, 117.0, 100.0),
            SampleAt(1, 39.0 + 10.0 / metersPerDegLat, 117.0, 101.0),
            SampleAt(2, 39.0 + 10.0 / metersPerDegLat, 117.0, 101.0), // stationary
        };

        var enriched = SlopeEnricher.EnrichAll(SampleEnricher.EnrichAll(raw));
        var g1 = enriched[1].Channels[ChannelIds.SlopePct];
        var g2 = enriched[2].Channels[ChannelIds.SlopePct];

        Assert.False(double.IsNaN(g1));
        Assert.InRange(g1, 8.0, 12.0);
        // Hold last good when Δs≈0
        Assert.Equal(g1, g2);
    }

    [Fact]
    public void EnrichAll_WritesSlopePctChannelId()
    {
        const double metersPerDegLat = 111_320.0;
        var raw = new List<VehicleSample>
        {
            SampleAt(0, 39.0, 117.0, 100.0),
            SampleAt(1, 39.0 + 12.0 / metersPerDegLat, 117.0, 101.2),
        };

        var enriched = SlopeEnricher.EnrichAll(SampleEnricher.EnrichAll(raw));
        Assert.All(enriched, s => Assert.Contains(ChannelIds.SlopePct, s.Channels.Keys));
        Assert.Equal("slope_pct", ChannelIds.SlopePct);
        Assert.Equal("Slope %", ChannelRegistry.SlopePct.DisplayName);
        Assert.Equal("%", ChannelRegistry.SlopePct.Unit);
    }
}
