using System.Windows;
using System.Windows.Media;

namespace Chassis_Master_Test_Suite.Themes;

/// <summary>
/// Resolve theme brushes/colors from Application resources at call time
/// (so Light/Dark/Custom swaps take effect without frozen hex literals).
/// </summary>
public static class ThemeBrushes
{
    public static SolidColorBrush Brush(string key, string fallbackHex = "#808080")
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush b)
            return b;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(fallbackHex);
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Gray;
        }
    }

    public static Color ColorOf(string key, string fallbackHex = "#808080")
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush b)
            return b.Color;
        if (Application.Current?.TryFindResource(key) is Color c)
            return c;
        try
        {
            return (Color)ColorConverter.ConvertFromString(fallbackHex);
        }
        catch
        {
            return Colors.Gray;
        }
    }

    public static string Hex(string key, string fallbackHex = "#808080")
    {
        var c = ColorOf(key, fallbackHex);
        return c.A < 255
            ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
            : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static ScottPlot.Color ScottPlot(string key, string fallbackHex = "#C8A34A")
    {
        var c = ColorOf(key, fallbackHex);
        return new ScottPlot.Color(c.R, c.G, c.B, c.A);
    }

    public static SolidColorBrush Accent() => Brush("AccentBrush", "#C8A34A");
    public static SolidColorBrush TextPrimary() => Brush("TextPrimary", "#E6EAF0");
    public static SolidColorBrush TextSecondary() => Brush("TextSecondary", "#8A94A6");
    public static SolidColorBrush TextMuted() => Brush("TextMuted", "#5A6474");
    public static SolidColorBrush TextBright() => Brush("TextBright", "#C3CBD8");
    public static SolidColorBrush Panel() => Brush("PanelBackground", "#12171E");
    public static SolidColorBrush Card() => Brush("CardBackground", "#0E131A");
    public static SolidColorBrush Border() => Brush("BorderBrushSoft", "#1E2530");
    public static SolidColorBrush Success() => Brush("SuccessBrush", "#3FBF6F");
    public static SolidColorBrush Danger() => Brush("DangerBrush", "#E05252");
    public static SolidColorBrush SuccessSoft() => Brush("SuccessSoftBackground", "#333DDC97");
    public static SolidColorBrush DangerSoft() => Brush("DangerSoftBackground", "#33E05A5A");
    public static SolidColorBrush RecordActive() => Brush("RecordActiveForeground", "#E08A8A");
}
