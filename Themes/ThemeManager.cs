using System.Windows;
using System.Windows.Media;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Themes;

public enum AppThemeMode
{
    Dark,
    Light,
    Custom
}

/// <summary>
/// Swaps the theme ResourceDictionary at Application.Resources.MergedDictionaries[0].
/// Custom mode starts from Dark and overrides brush colors from <see cref="CustomThemeColors"/>.
/// </summary>
public static class ThemeManager
{
    public const string DarkUri = "pack://application:,,,/Themes/Theme.Dark.xaml";
    public const string LightUri = "pack://application:,,,/Themes/Theme.Light.xaml";

    public static AppThemeMode CurrentMode { get; private set; } = AppThemeMode.Dark;

    public static event EventHandler? ThemeChanged;

    public static AppThemeMode ParseMode(string? mode)
    {
        if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase))
            return AppThemeMode.Light;
        if (string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase))
            return AppThemeMode.Custom;
        return AppThemeMode.Dark;
    }

    public static string ToStorage(AppThemeMode mode) => mode switch
    {
        AppThemeMode.Light => "Light",
        AppThemeMode.Custom => "Custom",
        _ => "Dark"
    };

    public static void Apply(AppThemeMode mode, CustomThemeColors? custom = null)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var dict = mode == AppThemeMode.Light
            ? LoadDictionary(LightUri)
            : LoadDictionary(DarkUri);

        if (mode == AppThemeMode.Custom && custom is not null)
            ApplyCustomOverrides(dict, custom);

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count == 0)
            merged.Add(dict);
        else
            merged[0] = dict;

        CurrentMode = mode;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static ResourceDictionary LoadDictionary(string packUri)
    {
        var dict = new ResourceDictionary { Source = new Uri(packUri, UriKind.Absolute) };
        // Clone into a mutable dictionary so Custom overrides don't dirty the cached source.
        var clone = new ResourceDictionary();
        foreach (var key in dict.Keys)
            clone[key] = dict[key];
        return clone;
    }

    private static void ApplyCustomOverrides(ResourceDictionary dict, CustomThemeColors c)
    {
        SetBrush(dict, "AccentBrush", c.Accent);
        SetBrush(dict, "AppBackground", c.AppBackground);
        SetBrush(dict, "PanelBackground", c.PanelBackground);
        SetBrush(dict, "CardBackground", c.CardBackground);
        SetBrush(dict, "BorderBrushSoft", c.Border);
        SetBrush(dict, "TextPrimary", c.TextPrimary);
        SetBrush(dict, "TextSecondary", c.TextSecondary);
        SetBrush(dict, "TextMuted", c.TextMuted);
        SetBrush(dict, "ScrollBarThumbDragBrush", c.Accent);
        SetBrush(dict, "ToolButtonBorder", c.Border);
        SetBrush(dict, "SplitterBackground", c.Border);
        SetBrush(dict, "ScrollBarGlyphBrush", c.TextSecondary);
    }

    private static void SetBrush(ResourceDictionary dict, string key, string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            dict[key] = new SolidColorBrush(color);
        }
        catch
        {
            // keep previous
        }
    }

    public static CustomThemeColors CaptureCurrentAsCustom()
    {
        var app = Application.Current;
        string Hex(string key, string fallback)
        {
            if (app?.TryFindResource(key) is SolidColorBrush b)
                return $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}";
            return fallback;
        }

        return new CustomThemeColors
        {
            Accent = Hex("AccentBrush", "#C8A34A"),
            AppBackground = Hex("AppBackground", "#0B0E12"),
            PanelBackground = Hex("PanelBackground", "#12171E"),
            CardBackground = Hex("CardBackground", "#0E131A"),
            Border = Hex("BorderBrushSoft", "#1E2530"),
            TextPrimary = Hex("TextPrimary", "#E6EAF0"),
            TextSecondary = Hex("TextSecondary", "#8A94A6"),
            TextMuted = Hex("TextMuted", "#5A6474")
        };
    }
}
