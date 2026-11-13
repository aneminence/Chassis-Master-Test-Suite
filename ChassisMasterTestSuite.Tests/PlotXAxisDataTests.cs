using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class PlotXAxisDataTests
{
    [Fact]
    public void ChannelsKeepTheirOwnSpikesAndReuseXCoordinates()
    {
        var samples = Enumerable.Range(0, 50_000).Select(i => new VehicleSample
        {
            Timestamp = i, SpeedKph = i == 12345 ? 9999 : i % 2,
            YawRate = i == 35123 ? -9999 : i % 3
        }).ToArray();
        var xEvaluations = 0;
        var axis = new PlotXAxisData(samples, s => { xEvaluations++; return s.Timestamp; });
        var speed = axis.BuildSeries(s => s.SpeedKph, 0, 50_000, true);
        var yaw = axis.BuildSeries(s => s.YawRate, 0, 50_000, true);
        Assert.Equal(samples.Length, xEvaluations);
        Assert.Contains(9999, speed.Ys);
        Assert.Contains(-9999, yaw.Ys);
        Assert.InRange(speed.Xs.Length, 2, PlotDownsampler.MaxPlotPoints);
        Assert.InRange(yaw.Xs.Length, 2, PlotDownsampler.MaxPlotPoints);
        Assert.Equal(12345, speed.Xs[Array.IndexOf(speed.Ys, 9999)]);
        Assert.Equal(35123, yaw.Xs[Array.IndexOf(yaw.Ys, -9999)]);
        Assert.Same(samples, axis.Samples);
        Assert.Equal(0, axis.Min);
        Assert.Equal(49_999, axis.Max);
    }

    [Fact]
    public void ZoomUsesVisibleWindowIncludingBorderSegments()
    {
        var samples = Enumerable.Range(0, 50_000)
            .Select(i => new VehicleSample { Timestamp = i, SpeedKph = i }).ToArray();
        var axis = new PlotXAxisData(samples, s => s.Timestamp);
        var series = axis.BuildSeries(s => s.SpeedKph, 20_000, 20_100, false);
        Assert.InRange(series.Xs.Length, 100, 120);
        Assert.True(series.Xs[0] < 20_000);
        Assert.True(series.Xs[^1] > 20_100);
        Assert.Equal(series.Xs, series.Ys);
    }

    [Fact]
    public void EndpointBudgetDoesNotTrimBucketExtrema()
    {
        var indices = PlotDownsampler.BuildDownsampleIndices(0, 1000,
            i => i == 501 ? -10_000 : i % 2, maxPoints: 40);
        Assert.Contains(501, indices);
        Assert.InRange(indices.Length, 2, 40);
        Assert.Equal(0, indices[0]);
        Assert.Equal(999, indices[^1]);
    }
}
