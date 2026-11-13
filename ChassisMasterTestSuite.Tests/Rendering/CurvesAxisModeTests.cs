using ScottPlot;
using ScottPlot.AxisPanels;
using ScottPlot.TickGenerators;
using Chassis_Master_Test_Suite.Controls;
using Xunit;

namespace ChassisMasterTestSuite.RenderingTests;

/// <summary>
/// Curves X axis: Time uses DateTimeTicksBottom(); leaving Time (Test Elapsed,
/// Distance, …) must call NumericTicksBottom(). Swapping only TickGenerator onto
/// a DateTimeXAxis throws and used to exit the WPF app.
/// </summary>
public class CurvesAxisModeTests
{
    [Fact]
    public void NumericAutomaticOnDateTimeAxis_ThrowsOnAssign()
    {
        var plot = new Plot();
        plot.Add.Scatter(new double[] { 0, 1, 2 }, new double[] { 1, 2, 3 });
        plot.Axes.DateTimeTicksBottom();
        Assert.IsType<DateTimeXAxis>(plot.Axes.Bottom);

        var ex = Assert.Throws<ArgumentException>(() =>
            plot.Axes.Bottom.TickGenerator = new NumericAutomatic());
        Assert.Contains("Date axis", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NumericTicksBottom_RestoresNumericAxis_AfterDateTime()
    {
        var plot = new Plot();
        plot.Add.Scatter(new double[] { 0, 1, 2 }, new double[] { 1, 2, 3 });
        plot.Axes.DateTimeTicksBottom();
        Assert.IsType<DateTimeXAxis>(plot.Axes.Bottom);

        plot.Axes.NumericTicksBottom();

        Assert.IsType<BottomAxis>(plot.Axes.Bottom);
        Assert.IsType<NumericAutomatic>(plot.Axes.Bottom.TickGenerator);
        var img = plot.GetImage(200, 100);
        Assert.NotNull(img);
    }

    [Fact]
    public void ElapsedTicks_ReformatAfterZoom_WithoutChangingSecondsCoordinates()
    {
        var plot = new Plot();
        var xs = new double[] { 0, 60, 3600, 18000, 18000.01, 18000.02, 18001 };
        plot.Add.Scatter(xs, new double[] { 1, 2, 3, 2, 3, 2, 1 });
        var tickGenerator = new ElapsedTimeTickGenerator(pattern => plot.XLabel($"Elapsed ({pattern})"));
        plot.Axes.Bottom.TickGenerator = tickGenerator;

        foreach (var (left, right, pattern) in new[]
        {
            (0.0, 30.0, "s"),
            (60.0, 120.0, "m:ss"),
            (18000.0, 18200.0, "h:mm:ss")
        })
        {
            plot.Axes.SetLimits(left, right, 0, 4);
            Assert.NotNull(plot.GetImage(900, 300));
            Assert.Equal(pattern, tickGenerator.Format.Pattern);
            Assert.Equal($"Elapsed ({pattern})", plot.Axes.Bottom.Label.Text);
            Assert.Equal(left, plot.Axes.GetLimits().Left);
            Assert.Equal(right, plot.Axes.GetLimits().Right);
        }

        plot.Axes.SetLimits(18000, 18000.1, 0, 4);
        Assert.NotNull(plot.GetImage(900, 300));
        Assert.StartsWith("h:mm:ss.ff", tickGenerator.Format.Pattern);
        var labels = tickGenerator.Ticks.Where(t => t.IsMajor).Select(t => t.Label).ToArray();
        Assert.True(labels.Length >= 3);
        Assert.Equal(labels.Length, labels.Distinct().Count());
        Assert.All(labels, label => Assert.StartsWith("5:00:00.", label));
        Assert.Equal(new double[] { 0, 60, 3600, 18000, 18000.01, 18000.02, 18001 }, xs);
    }

    [Fact]
    public void AbsoluteTime_Elapsed_DistanceSwitch_RestoresCorrectAxisAndFormatter()
    {
        var plot = new Plot();
        plot.Add.Scatter(new double[] { 3600, 3601, 3602 }, new double[] { 1, 2, 3 });
        plot.Axes.DateTimeTicksBottom();
        plot.Axes.NumericTicksBottom();
        plot.Axes.Bottom.TickGenerator = new ElapsedTimeTickGenerator();
        plot.Axes.SetLimits(3600, 3602, 0, 4);
        Assert.NotNull(plot.GetImage(900, 300));
        Assert.IsType<BottomAxis>(plot.Axes.Bottom);
        Assert.Contains(":", plot.Axes.Bottom.TickGenerator.Ticks.First(t => t.IsMajor).Label);

        plot.Axes.NumericTicksBottom();
        Assert.NotNull(plot.GetImage(900, 300));
        Assert.IsType<NumericAutomatic>(plot.Axes.Bottom.TickGenerator);
        Assert.All(plot.Axes.Bottom.TickGenerator.Ticks, tick => Assert.DoesNotContain(":", tick.Label));
    }
}
