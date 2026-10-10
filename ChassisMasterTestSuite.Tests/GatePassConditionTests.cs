using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class GatePassConditionTests
{
    private static (GateDefinition start, GateDefinition mid, GateDefinition end, List<VehicleSample> samples)
        BuildScenario(double speedKph)
    {
        var start = new GateDefinition
        {
            Name = "S", Latitude = 31.0, Longitude = 121.0, WidthMeters = 30, HeadingDeg = 0, ColorHex = "#111111"
        };
        var mid = new GateDefinition
        {
            Name = "Gate 2", Latitude = 31.0 + 100 / 111320.0, Longitude = 121.0, WidthMeters = 30, HeadingDeg = 0, ColorHex = "#222222"
        };
        var end = new GateDefinition
        {
            Name = "E", Latitude = 31.0 + 200 / 111320.0, Longitude = 121.0, WidthMeters = 30, HeadingDeg = 0, ColorHex = "#333333"
        };

        var samples = new List<VehicleSample>();
        for (var i = 0; i < 40; i++)
        {
            var lat = 31.0 + (i - 5) * 10 / 111320.0;
            samples.Add(new VehicleSample
            {
                Timestamp = 1_000_000 + i * 100,
                Sequence = i,
                SpeedKph = speedKph,
                Latitude = lat,
                Longitude = 121.0,
                Channels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    [ChannelIds.Velocity] = speedKph
                }
            });
        }

        return (start, mid, end, samples);
    }

    [Fact]
    public void Pass_WhenSpeedAtGateInRange()
    {
        var (start, mid, end, samples) = BuildScenario(80);

        var pass = new[]
        {
            new GatePassCondition
            {
                ChannelId = ChannelIds.Velocity,
                Min = 78,
                Max = 83,
                AtGateId = mid.Id
            }
        };

        var gates = new Dictionary<Guid, GateDefinition>
        {
            [start.Id] = start,
            [mid.Id] = mid,
            [end.Id] = end
        };

        var results = GateRunEngine.Compute(
            samples, start, end, pass, id => gates.GetValueOrDefault(id));
        Assert.NotEmpty(results);
        Assert.True(results[0].Pass);
        Assert.Single(results[0].PassMeasurements);
        var m = results[0].PassMeasurements[0];
        Assert.Equal("velocity@Gate 2", m.Header);
        Assert.True(m.InRange);
        Assert.NotNull(m.Value);
        Assert.InRange(m.Value!.Value, 78, 83);
    }

    [Fact]
    public void Fail_WhenSpeedAtGateOutOfRange()
    {
        var (start, mid, end, samples) = BuildScenario(90);

        var pass = new[]
        {
            new GatePassCondition
            {
                ChannelId = ChannelIds.Velocity,
                Min = 78,
                Max = 83,
                AtGateId = mid.Id
            }
        };
        var gates = new Dictionary<Guid, GateDefinition>
        {
            [start.Id] = start, [mid.Id] = mid, [end.Id] = end
        };

        var results = GateRunEngine.Compute(
            samples, start, end, pass, id => gates.GetValueOrDefault(id));
        Assert.NotEmpty(results);
        Assert.False(results[0].Pass);
        Assert.Contains("Gate 2", results[0].FailReason);
        Assert.Single(results[0].PassMeasurements);
        var m = results[0].PassMeasurements[0];
        Assert.False(m.InRange);
        Assert.NotNull(m.Value);
        Assert.Equal(90, m.Value!.Value, precision: 1);
    }

    [Fact]
    public void Csv_IncludesPassMeasurementColumns()
    {
        var (start, mid, end, samples) = BuildScenario(80);
        var pass = new[]
        {
            new GatePassCondition
            {
                ChannelId = ChannelIds.Velocity,
                Min = 60,
                Max = 83,
                AtGateId = mid.Id
            }
        };
        var gates = new Dictionary<Guid, GateDefinition>
        {
            [start.Id] = start, [mid.Id] = mid, [end.Id] = end
        };
        var results = GateRunEngine.Compute(
            samples, start, end, pass, id => gates.GetValueOrDefault(id));
        Assert.NotEmpty(results);

        var csv = TestResultsCsvExporter.BuildCsv(results);
        Assert.Contains("velocity@Gate 2", csv);
        Assert.Contains("velocity@Gate 2_InRange", csv);
        Assert.Contains(",1", csv); // InRange flag
    }
}
