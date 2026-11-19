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
        Assert.Equal("SpeedIntegral", run.DistanceMethod);
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
        Assert.Equal(-100, results[0].DeltaSpeedKph);
        Assert.Equal("SpeedIntegral", results[0].DistanceMethod);
        // 0.8 km/h floor ends the run slightly before the sample that reaches 0.
        Assert.InRange(results[0].DurationSeconds, 1.9, 1.999);
        Assert.True(results[0].DistanceMeters > 0);
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

    [Fact]
    public void Decel_DipThenReaccelerate_StartsAtCommittedCrossing()
    {
        const long origin = 1_700_000_000_000;
        var samples = BuildProfile(origin, new (int T, double V)[]
        {
            (0, 85),
            (100, 75),   // first 80-down (must be discarded)
            (200, 82),   // re-crosses upward through 80
            (300, 88),
            (400, 88),
            (500, 70),   // committed 80-down
            (600, 50),
            (700, 30),
            (800, 10),
            (900, 0.5),
            (1000, 0)
        });

        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Decel(80, 0));

        Assert.Single(results);
        var run = results[0];
        Assert.Equal(80, run.StartSpeedKph);
        Assert.Equal(0, run.EndSpeedKph);
        Assert.Equal(-80, run.DeltaSpeedKph);
        Assert.Equal("SpeedIntegral", run.DistanceMethod);

        // First dip interpolates to origin+50; committed start is between 400 and 500.
        Assert.InRange(run.StartTimestampMs, origin + 400, origin + 500);
        Assert.True(run.StartTimestampMs > origin + 100);
        // End at 0.8 is between 800 (10 kph) and 900 (0.5 kph).
        Assert.InRange(run.EndTimestampMs, origin + 800, origin + 900);
        Assert.InRange(run.DurationSeconds, 0.35, 0.55);
    }

    [Fact]
    public void Decel_SpeedCrawlsNearZero_EndsAtZeroFloor()
    {
        const long origin = 1_700_000_000_000;
        var points = new List<(int T, double V)>
        {
            (0, 110),
            (100, 90),
            (200, 70),
            (300, 50),
            (400, 30),
            (500, 10),
            (600, 0.5)
        };
        // Crawl at 0.05–0.2 km/h for 7 s before finally hitting 0.
        for (var t = 700; t <= 7600; t += 100)
        {
            var crawl = 0.05 + ((t / 100) % 2) * 0.15;
            points.Add((t, crawl));
        }

        points.Add((7700, 0));

        var samples = BuildProfile(origin, points);
        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Decel(100, 0));

        Assert.Single(results);
        var run = results[0];
        Assert.Equal(100, run.StartSpeedKph);
        Assert.Equal(0, run.EndSpeedKph);
        Assert.Equal("SpeedIntegral", run.DistanceMethod);
        // 0.8 floor is crossed between t=500 (10 kph) and t=600 (0.5 kph).
        Assert.InRange(run.EndTimestampMs, origin + 500, origin + 600);
        Assert.InRange(run.DurationSeconds, 0.4, 0.7);
        Assert.True(run.DurationSeconds < 2.0, "Must not wait through the near-zero crawl for exact 0.");
    }

    [Fact]
    public void Decel_ConfiguredEndAboveFloor_UsesExactThreshold()
    {
        const long origin = 1_700_000_000_000;
        var samples = BuildProfile(origin, new (int T, double V)[]
        {
            (0, 90),
            (100, 70),
            (200, 20),
            (300, 6),
            (400, 4),
            (500, 0)
        });

        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Decel(80, 5));

        Assert.Single(results);
        var run = results[0];
        Assert.Equal(80, run.StartSpeedKph);
        Assert.Equal(5, run.EndSpeedKph);
        Assert.Equal(-75, run.DeltaSpeedKph);
        // 5 km/h is between t=300 (6) and t=400 (4); must not end at the 0.8 floor near t=500.
        Assert.InRange(run.EndTimestampMs, origin + 300, origin + 400);
    }

    [Fact]
    public void Accel_CreepBelowStartFloor_StartsAtCommittedLeave()
    {
        const long origin = 1_700_000_000_000;
        var samples = BuildProfile(origin, new (int T, double V)[]
        {
            (0, 0),
            (100, 0.2),  // twitch off 0, still below 0.5
            (200, 0.4),
            (300, 0.3),
            (400, 0.1),
            (500, 0),
            (600, 0.2),
            (700, 1.0),  // committed 0.5-up
            (800, 10),
            (900, 20),
            (1000, 30),
            (1100, 40),
            (1200, 55)
        });

        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Accel(0, 50));

        Assert.Single(results);
        var run = results[0];
        Assert.Equal(0, run.StartSpeedKph);
        Assert.Equal(50, run.EndSpeedKph);
        Assert.Equal(50, run.DeltaSpeedKph);
        Assert.Equal("SpeedIntegral", run.DistanceMethod);
        // Must start at the later 0.5 leave (600–700), not the first twitch off 0.
        Assert.InRange(run.StartTimestampMs, origin + 600, origin + 700);
        Assert.True(run.StartTimestampMs > origin + 100);
        Assert.InRange(run.EndTimestampMs, origin + 1100, origin + 1200);
    }

    [Fact]
    public void Accel_LeaveThenFallBack_StartsAtLaterCrossing()
    {
        const long origin = 1_700_000_000_000;
        var samples = BuildProfile(origin, new (int T, double V)[]
        {
            (0, 0),
            (100, 0.2),
            (200, 2.0),  // first 0.5-up (must be discarded)
            (300, 3.0),
            (400, 0.3),  // falls back through 0.5
            (500, 0.1),
            (600, 0.2),
            (700, 1.0),  // committed 0.5-up
            (800, 15),
            (900, 30),
            (1000, 45),
            (1100, 55)
        });

        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Accel(0, 50));

        Assert.Single(results);
        var run = results[0];
        Assert.Equal(0, run.StartSpeedKph);
        Assert.Equal(50, run.EndSpeedKph);
        Assert.Equal("SpeedIntegral", run.DistanceMethod);
        Assert.InRange(run.StartTimestampMs, origin + 600, origin + 700);
        Assert.True(run.StartTimestampMs > origin + 200);
        Assert.InRange(run.EndTimestampMs, origin + 1000, origin + 1100);
    }

    [Fact]
    public void Accel_ConfiguredStartAboveFloor_UsesExactThreshold()
    {
        const long origin = 1_700_000_000_000;
        var samples = BuildProfile(origin, new (int T, double V)[]
        {
            (0, 0),
            (100, 0.8),  // above 0.5 floor, still below configured 5
            (200, 2),
            (300, 4),
            (400, 8),    // 5-up between 300 and 400
            (500, 20),
            (600, 40),
            (700, 55)
        });

        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Accel(5, 50));

        Assert.Single(results);
        var run = results[0];
        Assert.Equal(5, run.StartSpeedKph);
        Assert.Equal(50, run.EndSpeedKph);
        Assert.Equal(45, run.DeltaSpeedKph);
        Assert.Equal("SpeedIntegral", run.DistanceMethod);
        Assert.InRange(run.StartTimestampMs, origin + 300, origin + 400);
        Assert.True(run.StartTimestampMs > origin + 100);
        Assert.InRange(run.EndTimestampMs, origin + 600, origin + 700);
    }

    [Fact]
    public void Accel_TwoRuns_RearmBelowEffectiveStart()
    {
        const long origin = 1_700_000_000_000;
        var samples = BuildProfile(origin, new (int T, double V)[]
        {
            (0, 0),
            (100, 1),
            (200, 25),
            (300, 55),
            (400, 20),
            (500, 0.2),  // below 0.5 floor, not exact 0
            (600, 0.2),
            (700, 1),
            (800, 25),
            (900, 55)
        });

        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Accel(0, 50));
        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.True(r.Pass);
            Assert.Equal(0, r.StartSpeedKph);
            Assert.Equal(50, r.EndSpeedKph);
            Assert.Equal("SpeedIntegral", r.DistanceMethod);
        });
    }

    [Fact]
    public void Decel_TwoBrakes_WithCooldown_TwoRuns()
    {
        var a = BuildRamp(120, 0, 20, 100, 1_700_000_000_000);
        var b = BuildRamp(0, 120, 20, 100, a[^1].Timestamp + 100);
        var c = BuildRamp(120, 0, 20, 100, b[^1].Timestamp + 100);
        var samples = a.Concat(b).Concat(c).ToList();

        var results = SpeedToSpeedEngine.Compute(samples, TestDefinition.Decel(100, 0));
        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.True(r.Pass);
            Assert.Equal(100, r.StartSpeedKph);
            Assert.Equal(0, r.EndSpeedKph);
            Assert.Equal("SpeedIntegral", r.DistanceMethod);
        });
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

    private static List<VehicleSample> BuildProfile(
        long originMs,
        IReadOnlyList<(int T, double V)> points)
    {
        var list = new List<VehicleSample>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var (t, speed) = points[i];
            list.Add(new VehicleSample
            {
                Timestamp = originMs + t,
                Sequence = i + 1,
                SpeedKph = speed,
                Channels = VehicleSample.BuildCoreChannels(
                    speed, 0, 0, 0, 0, 0, 0, 0, 0)
            });
        }

        return list;
    }
}
