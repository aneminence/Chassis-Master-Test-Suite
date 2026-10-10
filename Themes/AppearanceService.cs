using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Localization;
using Chassis_Master_Test_Suite.Map;

namespace Chassis_Master_Test_Suite.Themes;

/// <summary>
/// Loads / applies / persists language + theme + map preferences.
/// Call <see cref="Initialize"/> once at app startup (before MainWindow shows).
/// </summary>
public static class AppearanceService
{
    private static AppPreferences _prefs = new();
    private static bool _initialized;

    public static AppPreferences Preferences => _prefs;

    public static event EventHandler? PreferencesChanged;

    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        _prefs = AppPreferencesStore.Load();
        ApplyAll(persist: false);
    }

    public static void ApplyAll(bool persist = true)
    {
        Loc.SetLanguage(_prefs.Language);
        ThemeManager.Apply(ThemeManager.ParseMode(_prefs.ThemeMode), _prefs.CustomColors);
        if (persist)
            Persist();
        PreferencesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void SetLanguage(string language, bool persist = true)
    {
        _prefs.Language = Loc.Normalize(language);
        Loc.SetLanguage(_prefs.Language);
        if (persist)
            Persist();
        PreferencesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void SetTheme(AppThemeMode mode, CustomThemeColors? custom = null, bool persist = true)
    {
        if (custom is not null)
            _prefs.CustomColors = custom;
        _prefs.ThemeMode = ThemeManager.ToStorage(mode);
        ThemeManager.Apply(mode, _prefs.CustomColors);
        if (persist)
            Persist();
        PreferencesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void SetMapBasemap(bool enabled, bool persist = true)
    {
        _prefs.Map.BasemapEnabled = enabled;
        if (persist)
            Persist();
        PreferencesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void SetMapSource(string sourceId, string? customUrl = null, string? tiandituKey = null, bool persist = true)
    {
        _prefs.Map.SourceId = string.IsNullOrWhiteSpace(sourceId) ? "osm" : sourceId;
        if (customUrl is not null)
            _prefs.Map.CustomUrlTemplate = customUrl;
        if (tiandituKey is not null)
            _prefs.Map.TiandituKey = tiandituKey;
        // Validate resolves
        _ = MapTileSources.Resolve(_prefs.Map.SourceId, _prefs.Map.CustomUrlTemplate, _prefs.Map.TiandituKey);
        if (persist)
            Persist();
        PreferencesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void SetMapOpacity(double opacity, bool persist = true)
    {
        _prefs.Map.Opacity = Math.Clamp(opacity, 0.05, 1.0);
        if (persist)
            Persist();
        PreferencesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static MapTileSource CurrentMapSource() =>
        MapTileSources.Resolve(_prefs.Map.SourceId, _prefs.Map.CustomUrlTemplate, _prefs.Map.TiandituKey);

    public static void Persist() => AppPreferencesStore.Save(_prefs);
}
