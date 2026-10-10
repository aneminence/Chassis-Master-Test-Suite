using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class SelectionMeasureTests
{
    [Fact]
    public void Compute_MinMaxAvg()
    {
        var samples = new List<VehicleSample>();
        for (var i = 0; i < 10; i++)
        {
            samples.Add(new VehicleSample
            {
                Timestamp = i * 1000,
                Sequence = i,
                SpeedKph = i * 10,
                Channels = VehicleSample.BuildCoreChannels(i * 10, 0, 0, 0, 0, 0, 0, 0, 0)
            });
        }

        var result = SelectionMeasure.Compute(
            samples,
            s => s.Timestamp,
            2000,
            5000,
            new[] { ChannelIds.Velocity });

        Assert.NotNull(result);
        Assert.Equal(2, result!.StartIndex);
        Assert.Equal(5, result.EndIndex);
        var c = Assert.Single(result.Channels);
        Assert.Equal(20, c.Min);
        Assert.Equal(50, c.Max);
        Assert.Equal(35, c.Avg);
    }
}
