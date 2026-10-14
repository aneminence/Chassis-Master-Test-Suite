using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class LapTimeEngineTests
{
    [Fact]
    public void CompleteLap_ProducesOrderedSectorTimes()
    {
        var finish = new GateDefinition
        {
            Name = "Finish", Latitude = 31.0, Longitude = 121.0,
            WidthMeters = 100, HeadingDeg = 0
        };
        var split = new GateDefinition
        {
            Name = "Split 1", Latitude = 31.001, Longitude = 121.0,
            WidthMeters = 100, HeadingDeg = 0
        };
        var samples = new[]
        {
            Sample(0, 30.9999, 121.0000),
            Sample(1000, 31.0001, 121.0000),
            Sample(2000, 31.0009, 121.0000),
            Sample(3000, 31.0011, 121.0000),
            Sample(4000, 31.0015, 121.0020),
            Sample(5000, 30.9995, 121.0020),
            Sample(6000, 30.9995, 121.0000),
            Sample(7000, 30.9999, 121.0000),
            Sample(8000, 31.0001, 121.0000)
        };

        var laps = LapTimeEngine.Compute(samples, finish, new[] { split });

        var lap = Assert.Single(laps);
        Assert.Equal(7.0, lap.DurationSeconds, precision: 3);
        Assert.Equal(2, lap.SectorTimes.Count);
        Assert.Equal("Split 1", lap.SectorTimes[0].Name);
        Assert.Equal(2.0, lap.SectorTimes[0].DurationSeconds, precision: 3);
        Assert.Equal(5.0, lap.SectorTimes[1].DurationSeconds, precision: 3);
    }

    private static VehicleSample Sample(long timestamp, double latitude, double longitude) => new()
    {
        Timestamp = timestamp,
        Latitude = latitude,
        Longitude = longitude,
        SpeedKph = 60,
        Channels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [ChannelIds.DistanceTraveled] = timestamp / 10.0
        }
    };
}
