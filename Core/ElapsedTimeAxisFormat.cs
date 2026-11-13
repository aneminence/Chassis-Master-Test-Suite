using System.Globalization;

namespace Chassis_Master_Test_Suite.Core;

/// <summary>Display-only formatting for X coordinates stored in seconds.</summary>
public readonly record struct ElapsedTimeAxisFormat(int Components, int DecimalPlaces)
{
    public string Pattern => (Components switch
    {
        3 => "h:mm:ss",
        2 => "m:ss",
        _ => "s"
    }) + (DecimalPlaces > 0 ? "." + new string('f', DecimalPlaces) : string.Empty);

    public static ElapsedTimeAxisFormat ForRange(double left, double right, double tickSpacing)
    {
        var magnitude = Math.Max(double.IsFinite(left) ? Math.Abs(left) : 0,
            double.IsFinite(right) ? Math.Abs(right) : 0);
        var components = magnitude >= 3600 ? 3 : magnitude >= 60 ? 2 : 1;
        if (!double.IsFinite(tickSpacing) || tickSpacing <= 0)
            tickSpacing = Math.Abs(right - left) / 6;
        return new ElapsedTimeAxisFormat(components, GetDecimalPlaces(tickSpacing));
    }

    /// <summary>Readouts keep milliseconds even when the visible ticks are whole seconds.</summary>
    public static ElapsedTimeAxisFormat ForReadout(double left, double right) =>
        ForRange(left, right, 0.001);

    public string Format(double seconds)
    {
        if (!double.IsFinite(seconds))
            return string.Empty;

        var places = Math.Clamp(DecimalPlaces, 0, 6);
        // Round the complete duration before splitting it so a carry cannot produce :60.
        var rounded = Math.Round(seconds, places, MidpointRounding.AwayFromZero);
        if (Components <= 1 || Math.Abs(rounded) >= long.MaxValue)
            return (rounded == 0 ? 0 : rounded).ToString($"F{places}", CultureInfo.InvariantCulture);

        var magnitude = Math.Abs(rounded);
        var whole = (long)Math.Floor(magnitude);
        var remainder = whole % 60 + (magnitude - whole);
        var secondText = remainder.ToString(places > 0 ? "00." + new string('0', places) : "00",
            CultureInfo.InvariantCulture);
        var sign = rounded < 0 ? "-" : string.Empty;
        return Components >= 3
            ? $"{sign}{whole / 3600}:{whole / 60 % 60:00}:{secondText}"
            : $"{sign}{whole / 60}:{secondText}";
    }

    private static int GetDecimalPlaces(double spacing)
    {
        if (!double.IsFinite(spacing) || spacing <= 0)
            return 0;
        for (var places = 0; places < 6; places++)
        {
            var scaled = spacing * Math.Pow(10, places);
            if (scaled >= 1 && Math.Abs(scaled - Math.Round(scaled)) <= 1e-6)
                return places;
        }
        return 6;
    }
}
