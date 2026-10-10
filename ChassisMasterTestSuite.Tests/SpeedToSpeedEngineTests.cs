using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class SpeedToSpeedEngineTests
{
    [Fact]
    public void Accel_0_to_100_DetectsSingleRun()
    {
        var samples = BuildRamp(
            startKph: 0,
            endKph: 120,
            points: 25,
            dtMs: 100,
            originMs: 1_700_000_000_000);

        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Accel(0, 100));

        Assert.Single(results);
        var run = results[0];
        Assert.Equal(1, run.RunNumber);
        Assert.True(run.Pass);
        Assert.InRange(run.DurationSeconds, 1.5, 2.4);
        Assert.Equal(0, run.StartSpeedKph);
        Assert.Equal(100, run.EndSpeedKph);
        Assert.True(run.DistanceMeters > 0);
    }

    [Fact]
    public void Decel_100_to_0_DetectsSingleRun()
    {
        var samples = BuildRamp(
            startKph: 120,
            endKph: 0,
            points: 25,
            dtMs: 100,
            originMs: 1_700_000_000_000);

        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Decel(100, 0));

        Assert.Single(results);
        Assert.True(results[0].Pass);
        Assert.Equal(100, results[0].StartSpeedKph);
        Assert.Equal(0, results[0].EndSpeedKph);
        Assert.True(results[0].DeltaSpeedKph < 0);
    }

    [Fact]
    public void Custom_Rising_UsesChannel()
    {
        var samples = BuildRamp(0, 80, 20, 50, 1_700_000_000_000);
        var def = TestDefinition.CustomSpeed(10, 60);
        var results = SpeedToSpeedEngine.Compute(samples, def);
        Assert.Single(results);
        Assert.Equal(10, results[0].StartSpeedKph);
        Assert.Equal(60, results[0].EndSpeedKph);
    }

    [Fact]
    public void Accel_TwoPeaks_WithCooldown_TwoRuns()
    {
        // up 0→120, down to 0, up 0→120 again
        var a = BuildRamp(0, 120, 20, 100, 1_700_000_000_000);
        var b = BuildRamp(120, 0, 20, 100, a[^1].Timestamp + 100);
        var c = BuildRamp(0, 120, 20, 100, b[^1].Timestamp + 100);
        var samples = a.Concat(b).Concat(c).ToList();

        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Accel(0, 100));
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        var results = SpeedToSpeedEngine.Compute(
            Array.Empty<VehicleSample>(),
            TestDefinition.Accel());
        Assert.Empty(results);
    }

    private static List<VehicleSample> BuildRamp(
        double startKph,
        double endKph,
        int points,
        int dtMs,
        long originMs)
    {
        var list = new List<VehicleSample>(points);
        for (var i = 0; i < points; i++)
        {
            var t = points == 1 ? 0 : (double)i / (points - 1);
            var speed = startKph + (endKph - startKph) * t;
            list.Add(new VehicleSample
            {
                Timestamp = originMs + i * dtMs,
                Sequence = i + 1,
                SpeedKph = speed,
                Channels = VehicleSample.BuildCoreChannels(
                    speed, 0, 0, 0, 0, 0, 0, 0, 0)
            });
        }

        return list;
    }
}