using Chassis_Master_Test_Suite.Core;
using Xunit;

namespace ChassisMasterTestSuite.Tests;

public class ElapsedTimeAxisFormatTests
{
    [Theory]
    [InlineData(0, 59, 10, "s")]
    [InlineData(0, 60, 10, "m:ss")]
    [InlineData(0, 3599, 10, "m:ss")]
    [InlineData(0, 3600, 10, "h:mm:ss")]
    [InlineData(18000, 18001, 0.25, "h:mm:ss.ff")]
    [InlineData(18000, 18001, 0.01, "h:mm:ss.ff")]
    [InlineData(18000, 18001, 0.0025, "h:mm:ss.ffff")]
    [InlineData(0, 30, 2.5, "s.f")]
    [InlineData(-120, -60, 10, "m:ss")]
    [InlineData(3601, 3600, 0.5, "h:mm:ss.f")]
    public void RangeAndActualSpacing_SelectReadablePattern(double left, double right, double step, string expected)
    {
        Assert.Equal(expected, ElapsedTimeAxisFormat.ForRange(left, right, step).Pattern);
    }

    [Theory]
    [InlineData(59.9996, 0, 120, "1:00.000")]
    [InlineData(3599.9996, 0, 7200, "1:00:00.000")]
    [InlineData(90061.123, 0, 90062, "25:01:01.123")]
    [InlineData(-60.125, -120, 0, "-1:00.125")]
    [InlineData(-0.0001, 0, 120, "0:00.000")]
    [InlineData(-0.0001, 0, 30, "0.000")]
    [InlineData(12.345, 0, 30, "12.345")]
    [InlineData(18000.01, 18000, 18001, "5:00:00.010")]
    [InlineData(18000.02, 18000, 18001, "5:00:00.020")]
    public void Readout_RoundsBeforeSplitting_AndPreservesMilliseconds(
        double seconds, double left, double right, string expected)
    {
        Assert.Equal(expected, ElapsedTimeAxisFormat.ForReadout(left, right).Format(seconds));
    }

    [Fact]
    public void InvalidValues_DoNotThrowOrRenderMisleadingNumbers()
    {
        var format = ElapsedTimeAxisFormat.ForRange(double.NaN, double.PositiveInfinity, 0);
        Assert.Equal("s", format.Pattern);
        Assert.Equal(string.Empty, format.Format(double.NaN));
        Assert.Equal(string.Empty, format.Format(double.NegativeInfinity));
        Assert.Equal("s.f", ElapsedTimeAxisFormat.ForRange(0, 3, double.NaN).Pattern);
    }

    [Fact]
    public void SmallFloatingPointSpacingError_DoesNotAddSpuriousDecimals()
    {
        var format = ElapsedTimeAxisFormat.ForRange(18000, 18001, 0.01 + 1e-12);
        Assert.Equal("h:mm:ss.ff", format.Pattern);
        Assert.NotEqual(format.Format(18000.01), format.Format(18000.02));
    }
}
