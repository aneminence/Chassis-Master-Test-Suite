namespace Chassis_Master_Test_Suite.Core;

/// <summary>Immutable X coordinates shared by a file's plotted channels.</summary>
public sealed class PlotXAxisData
{
    public IReadOnlyList<VehicleSample> Samples { get; }
    public double[] Values { get; }
    public double Min { get; }
    public double Max { get; }

    public PlotXAxisData(IReadOnlyList<VehicleSample> samples, Func<VehicleSample, double> selectX)
    {
        Samples = samples;
        Values = new double[samples.Count];
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var i = 0; i < samples.Count; i++)
        {
            var value = Values[i] = selectX(samples[i]);
            if (!double.IsFinite(value)) continue;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }
        Min = min;
        Max = max;
    }

    public (double[] Xs, double[] Ys) BuildSeries(
        Func<VehicleSample, double> selectY, double left, double right, bool autoX)
    {
        var (start, end) = PlotDownsampler.FindVisibleIndexRange(
            Samples.Count, i => Values[i], left, right, autoX);
        // Include the bordering segments so clipping does not break the line.
        start = Math.Max(0, start - 1);
        end = Math.Min(Samples.Count, end + 1);
        var indices = PlotDownsampler.BuildDownsampleIndices(
            start, end, i => selectY(Samples[i]));
        var xs = new double[indices.Length];
        var ys = new double[indices.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            xs[i] = Values[indices[i]];
            ys[i] = selectY(Samples[indices[i]]);
        }
        return (xs, ys);
    }
}
