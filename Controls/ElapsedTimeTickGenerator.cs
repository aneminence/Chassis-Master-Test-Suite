using Chassis_Master_Test_Suite.Core;
using ScottPlot;
using ScottPlot.TickGenerators;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>Uses numeric tick positions without changing the seconds-based plot coordinates.</summary>
public sealed class ElapsedTimeTickGenerator(Action<string>? updateAxisLabel = null) : ITickGenerator
{
    private readonly NumericAutomatic _numeric = new();
    public Tick[] Ticks { get; private set; } = [];
    public int MaxTickCount { get => _numeric.MaxTickCount; set => _numeric.MaxTickCount = value; }
    public ElapsedTimeAxisFormat Format { get; private set; }

    public void Regenerate(CoordinateRange range, Edge edge, PixelLength size, Paint paint, LabelStyle labelStyle)
    {
        Format = ElapsedTimeAxisFormat.ForRange(range.Min, range.Max, range.Span / 6);
        _numeric.LabelFormatter = Format.Format;
        _numeric.Regenerate(range, edge, size, paint, labelStyle);

        // NumericAutomatic chooses its spacing using the rendered label widths. Refine the
        // precision from that actual spacing, then let it measure the refined labels once.
        var actual = ElapsedTimeAxisFormat.ForRange(range.Min, range.Max, GetMajorSpacing());
        if (actual != Format)
        {
            Format = actual;
            _numeric.LabelFormatter = Format.Format;
            _numeric.Regenerate(range, edge, size, paint, labelStyle);
        }
        Format = ElapsedTimeAxisFormat.ForRange(range.Min, range.Max, GetMajorSpacing());
        Ticks = _numeric.Ticks.Select(t => t.IsMajor
            ? new Tick(t.Position, Format.Format(t.Position), true)
            : t).ToArray();
        updateAxisLabel?.Invoke(Format.Pattern);
    }

    private double GetMajorSpacing()
    {
        var previous = double.NaN;
        var spacing = double.PositiveInfinity;
        foreach (var tick in _numeric.Ticks)
        {
            if (!tick.IsMajor) continue;
            if (double.IsFinite(previous) && tick.Position != previous)
                spacing = Math.Min(spacing, Math.Abs(tick.Position - previous));
            previous = tick.Position;
        }
        return spacing;
    }
}
