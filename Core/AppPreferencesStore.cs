using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// Persisted UI preferences: language, theme, custom colors, map basemap.
/// Stored under %LocalAppData%\CMTS\app-preferences.json.
/// </summary>
public sealed class AppPreferences
{
    public int Version { get; set; } = 1;

    /// <summary>zh-CN | en</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>Dark | Light | Custom</summary>
    public string ThemeMode { get; set; } = "Dark";

    public CustomThemeColors? CustomColors { get; set; }

    public MapPreferences Map { get; set; } = new();
}

public sealed class CustomThemeColors
{
    public string Accent { get; set; } = "#C8A34A";
    public string AppBackground { get; set; } = "#0B0E12";
    public string PanelBackground { get; set; } = "#12171E";
    public string CardBackground { get; set; } = "#0E131A";
    public string Border { get; set; } = "#1E2530";
    public string TextPrimary { get; set; } = "#E6EAF0";
    public string TextSecondary { get; set; } = "#8A94A6";
    public string TextMuted { get; set; } = "#5A6474";
}

public sealed class MapPreferences
{
    public bool BasemapEnabled { get; set; }

    /// <summary>Id of a preset source, or "custom".</summary>
    public string SourceId { get; set; } = "osm";

    /// <summary>XYZ template with {z}/{x}/{y} or {Z}/{X}/{Y}; used when SourceId == custom.</summary>
    public string? CustomUrlTemplate { get; set; }

    /// <summary>天地图 tk key for tdt-* sources (console.tianditu.gov.cn).</summary>
    public string? TiandituKey { get; set; }

    /// <summary>0..1 opacity of basemap tiles.</summary>
    public double Opacity { get; set; } = 0.92;
}

public static class AppPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string? StoragePathOverride { get; set; }

    public static string StoragePath =>
        !string.IsNullOrWhiteSpace(StoragePathOverride)
            ? StoragePathOverride!
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CMTS",
                "app-preferences.json");

    public static AppPreferences Load()
    {
        try
        {
            var path = StoragePath;
            if (!File.Exists(path))
                return new AppPreferences();

            var json = File.ReadAllText(path);
            var prefs = JsonSerializer.Deserialize<AppPreferences>(json, JsonOptions);
            if (prefs is null)
                return new AppPreferences();

            prefs.Map ??= new MapPreferences();
            if (string.IsNullOrWhiteSpace(prefs.Language))
                prefs.Language = "zh-CN";
            if (string.IsNullOrWhiteSpace(prefs.ThemeMode))
                prefs.ThemeMode = "Dark";
            if (string.IsNullOrWhiteSpace(prefs.Map.SourceId))
                prefs.Map.SourceId = "osm";
            if (prefs.Map.Opacity is < 0.05 or > 1.0)
                prefs.Map.Opacity = 0.92;

            return prefs;
        }
        catch
        {
            return new AppPreferences();
        }
    }

    public static void Save(AppPreferences preferences)
    {
        try
        {
            var dir = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            preferences.Map ??= new MapPreferences();
            var json = JsonSerializer.Serialize(preferences, JsonOptions);
            File.WriteAllText(StoragePath, json);
        }
        catch
        {
            // Best-effort.
        }
    }
}
