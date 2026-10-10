using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class GateCrossingTests
{
    [Fact]
    public void SegmentIntersect_Basic()
    {
        Assert.True(GateCrossing.TrySegmentIntersect(
            0, 0, 10, 0,
            5, -5, 5, 5,
            out var t));
        Assert.InRange(t, 0.49, 0.51);
    }

    [Fact]
    public void Gate_CrossesWhenPathGoesThrough()
    {
        // Gate at (31, 121), width 40m, heading north => gate line east-west
        var gate = new GateDefinition
        {
            Name = "A",
            Latitude = 31.0,
            Longitude = 121.0,
            WidthMeters = 40,
            HeadingDeg = 0
        };

        // Path from south of gate to north of gate, through center
        var lat0 = 31.0 - 30 / 111320.0;
        var lat1 = 31.0 + 30 / 111320.0;
        Assert.True(GateCrossing.TryCross(gate, lat0, 121.0, lat1, 121.0, out var t));
        Assert.InRange(t, 0.4, 0.6);
    }

    [Fact]
    public void Gate_MissesWhenPathBeside()
    {
        var gate = new GateDefinition
        {
            Name = "A",
            Latitude = 31.0,
            Longitude = 121.0,
            WidthMeters = 10,
            HeadingDeg = 0
        };

        // Far east of gate
        var lon = 121.0 + 100 / (111320.0 * Math.Cos(31 * Math.PI / 180));
        var lat0 = 31.0 - 30 / 111320.0;
        var lat1 = 31.0 + 30 / 111320.0;
        Assert.False(GateCrossing.TryCross(gate, lat0, lon, lat1, lon, out _));
    }

    [Fact]
    public void GateRunEngine_DetectsOneRun()
    {
        var start = new GateDefinition
        {
            Name = "S",
            Latitude = 31.0,
            Longitude = 121.0,
            WidthMeters = 30,
            HeadingDeg = 0
        };
        var end = new GateDefinition
        {
            Name = "E",
            Latitude = 31.0 + 200 / 111320.0,
            Longitude = 121.0,
            WidthMeters = 30,
            HeadingDeg = 0
        };

        var samples = new List<VehicleSample>();
        // Drive north across start then end
        for (var i = 0; i < 40; i++)
        {
            var northM = -50 + i * 10; // -50 .. 340
            var lat = 31.0 + northM / 111320.0;
            samples.Add(new VehicleSample
            {
                Timestamp = 1_700_000_000_000 + i * 100,
                Sequence = i,
                SpeedKph = 80,
                Latitude = lat,
                Longitude = 121.0,
                Channels = VehicleSample.BuildCoreChannels(80, 0, 0, 0, 0, 0, lat, 121.0, 0)
            });
        }

        var runs = GateRunEngine.Compute(samples, start, end);
        Assert.Single(runs);
        Assert.True(runs[0].Pass);
        Assert.True(runs[0].DurationSeconds > 0);
    }
}
