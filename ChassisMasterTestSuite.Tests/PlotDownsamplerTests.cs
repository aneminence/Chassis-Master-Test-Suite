using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class PlotDownsamplerTests
{
    [Fact]
    public void BuildDownsampleIndices_CountAtOrBelowMax_ReturnsAll()
    {
        const int count = 100;
        var indices = PlotDownsampler.BuildDownsampleIndices(
            0,
            count,
            i => i,
            maxPoints: PlotDownsampler.MaxPlotPoints);

        Assert.Equal(count, indices.Length);
        Assert.Equal(0, indices[0]);
        Assert.Equal(count - 1, indices[^1]);
        for (var i = 0; i < count; i++)
            Assert.Equal(i, indices[i]);
    }

    [Fact]
    public void BuildDownsampleIndices_OverMax_ReturnsAtMostMax_AndPreservesEndpoints()
    {
        const int count = 50_000;
        var indices = PlotDownsampler.BuildDownsampleIndices(
            0,
            count,
            i => Math.Sin(i * 0.01) * 100 + (i == 12345 ? 9999 : 0),
            maxPoints: PlotDownsampler.MaxPlotPoints);

        Assert.True(indices.Length <= PlotDownsampler.MaxPlotPoints);
        Assert.True(indices.Length >= 2);
        Assert.Equal(0, indices[0]);
        Assert.Equal(count - 1, indices[^1]);

        // Indices stay ordered (non-decreasing).
        for (var i = 1; i < indices.Length; i++)
            Assert.True(indices[i] >= indices[i - 1]);
    }

    [Fact]
    public void BuildDownsampleIndices_MinMax_KeepsSpikeInBucket()
    {
        // Small max so we force bucketing; put a spike in the middle.
        const int count = 1000;
        const int spikeAt = 500;
        const int maxPoints = 40;

        var indices = PlotDownsampler.BuildDownsampleIndices(
            0,
            count,
            i => i == spikeAt ? 1e6 : (double)i,
            maxPoints: maxPoints);

        Assert.True(indices.Length <= maxPoints);
        Assert.Contains(spikeAt, indices);
        Assert.Equal(0, indices[0]);
        Assert.Equal(count - 1, indices[^1]);
    }

    [Fact]
    public void FindVisibleIndexRange_UseFullRange_ReturnsAll()
    {
        var (start, end) = PlotDownsampler.FindVisibleIndexRange(
            200,
            i => i,
            left: 10,
            right: 50,
            useFullRange: true);

        Assert.Equal(0, start);
        Assert.Equal(200, end);
    }
}
