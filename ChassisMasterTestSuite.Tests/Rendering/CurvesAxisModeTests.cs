using ScottPlot;
using ScottPlot.AxisPanels;
using ScottPlot.TickGenerators;
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
}
