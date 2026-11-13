using System.Collections;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class SlopeEnricherPerformanceTests
{
    [Fact]
    public void EnrichAll_MovingStationaryAndInvalidDistances_MatchesOriginalScan()
    {
        var random = new Random(5271);
        var distances = new double[1500];
        for (var i = 1; i < distances.Length; i++)
            distances[i] = distances[i - 1] + (i % 200 < 100 ? 0 : random.NextDouble());

        foreach (var window in new[] { 10.0, 0.0, -1.0, double.NaN, double.PositiveInfinity })
            VerifyOriginal(distances, window);

        distances[800] = distances[799] - 10; // preserve legacy fallback for malformed inputs
        VerifyOriginal(distances, 10);
        distances[700] = double.NaN;
        VerifyOriginal(distances, 10);
        VerifyOriginal(new[] { double.NegativeInfinity, double.NegativeInfinity, 0, 10,
            double.PositiveInfinity, double.PositiveInfinity }, 10);
        VerifyOriginal(new[] { 0.0, 0, 0, double.NaN, double.NaN }, 10);
    }

    [Fact]
    public void TryComputeGrade_LongStationaryHistory_UsesLogarithmicDistanceReads()
    {
        var distances = new CountingDistances(1_000_000);
        var heights = new double[distances.Count];
        Assert.False(SlopeEnricher.TryComputeGrade(distances, heights, distances.Count - 1,
            10, out var grade, distanceIsMonotonic: true));
        Assert.True(double.IsNaN(grade));
        Assert.InRange(distances.ReadCount, 1, 32);
    }

    [Fact]
    public void EnrichAll_LongStationaryRecord_LeavesEveryGradeUndefined()
    {
        var sample = new VehicleSample
        {
            Altitude = 100,
            Channels = new Dictionary<string, double> { [ChannelIds.DistanceTraveled] = 0 }
        };
        var result = SlopeEnricher.EnrichAll(Enumerable.Repeat(sample, 100_000).ToArray());
        Assert.Equal(100_000, result.Count);
        Assert.All(result, row => Assert.True(double.IsNaN(row.Channels[ChannelIds.SlopePct])));
    }

    private static void VerifyOriginal(double[] distances, double window)
    {
        var heights = distances.Select((_, i) => 100 + Math.Sin(i / 10.0)).ToArray();
        var samples = distances.Select((d, i) => new VehicleSample
        {
            Altitude = heights[i],
            Channels = new Dictionary<string, double> { [ChannelIds.DistanceTraveled] = d }
        }).ToArray();
        var result = SlopeEnricher.EnrichAll(samples, window);
        var lastGood = double.NaN;
        for (var i = 0; i < samples.Length; i++)
        {
            if (Original(distances, heights, i, window, out var grade)) lastGood = grade;
            Assert.Equal(lastGood, result[i].Channels[ChannelIds.SlopePct]);
        }
    }

    // Golden behavior from the pre-optimization implementation.
    private static bool Original(double[] distances, double[] heights, int index, double window, out double grade)
    {
        grade = double.NaN;
        if (index == 0) return false;
        var j = index;
        for (var k = index - 1; k >= 0; k--)
        {
            j = k;
            if (distances[index] - distances[k] >= window) break;
        }
        var delta = distances[index] - distances[j];
        if (delta < SlopeEnricher.MinDeltaMeters)
        {
            j = index - Math.Min(SlopeEnricher.FallbackSampleCount, index);
            delta = distances[index] - distances[j];
        }
        if (delta < SlopeEnricher.MinDeltaMeters) return false;
        grade = 100 * (heights[index] - heights[j]) / delta;
        return true;
    }

    private sealed class CountingDistances(int count) : IReadOnlyList<double>
    {
        public long ReadCount;
        public int Count => count;
        public double this[int index] { get { ReadCount++; return 0; } }
        public IEnumerator<double> GetEnumerator() => Enumerable.Repeat(0.0, count).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
