using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Map;

namespace ChassisMasterTestSuite.Tests;

public class AppPreferencesStoreTests
{
    [Fact]
    public void RoundTrip_LanguageAndTheme()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cmts-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AppPreferencesStore.StoragePathOverride = Path.Combine(dir, "app-preferences.json");
            var prefs = new AppPreferences
            {
                Language = "en",
                ThemeMode = "Light",
                Map = new MapPreferences
                {
                    BasemapEnabled = true,
                    SourceId = "esri-imagery",
                    Opacity = 0.8
                }
            };
            AppPreferencesStore.Save(prefs);
            var loaded = AppPreferencesStore.Load();
            Assert.Equal("en", loaded.Language);
            Assert.Equal("Light", loaded.ThemeMode);
            Assert.True(loaded.Map.BasemapEnabled);
            Assert.Equal("esri-imagery", loaded.Map.SourceId);
            Assert.Equal(0.8, loaded.Map.Opacity, 3);
        }
        finally
        {
            AppPreferencesStore.StoragePathOverride = null;
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Load_MigratesUnknownBasemapSourceToDefault()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cmts-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "app-preferences.json");
            AppPreferencesStore.StoragePathOverride = path;
            File.WriteAllText(path, """
                {"version":1,"language":"zh-CN","themeMode":"Dark","map":{"basemapEnabled":true,"sourceId":"osm","opacity":0.92}}
                """);
            var loaded = AppPreferencesStore.Load();
            Assert.Equal(MapTileSources.DefaultId, loaded.Map.SourceId);
        }
        finally
        {
            AppPreferencesStore.StoragePathOverride = null;
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void MapTileSources_ResolveCustomUsesTemplate()
    {
        var src = MapTileSources.Resolve("custom", "https://example/{z}/{x}/{y}.png");
        Assert.True(src.IsCustom);
        Assert.Contains("{z}", src.UrlTemplate);
    }

    [Fact]
    public void MapTileSources_PresetsAreSatelliteOnly()
    {
        Assert.Contains(MapTileSources.Presets, s => s.Id == "esri-imagery");
        Assert.Contains(MapTileSources.Presets, s => s.Id == "esri-clarity");
        Assert.Contains(MapTileSources.Presets, s => s.Id == "google-sat");
        Assert.Contains(MapTileSources.Presets, s => s.Id == "google-hybrid");
        Assert.Contains(MapTileSources.Presets, s => s.Id == "bing-aerial");
        Assert.Contains(MapTileSources.Presets, s => s.Id == "custom");
        Assert.DoesNotContain(MapTileSources.Presets, s => s.Id == "osm");
        Assert.DoesNotContain(MapTileSources.Presets, s => s.Id == "carto-voyager");
        Assert.DoesNotContain(MapTileSources.Presets, s => s.Id == "tdt-vec");
        Assert.DoesNotContain(MapTileSources.Presets, s => s.Id == "opentopo");
        Assert.True(MapTileSources.Presets.Count <= 8);
    }

    [Fact]
    public void MapTileSources_ResolveFallsBackToDefault()
    {
        var src = MapTileSources.Resolve("osm", null);
        Assert.Equal(MapTileSources.DefaultId, src.Id);
    }
}
