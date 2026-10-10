using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Localization;
using Chassis_Master_Test_Suite.Map;

namespace ChassisMasterTestSuite.Tests;

public class AppPreferencesStoreTests
{
    [Fact]
    public void RoundTrip_LanguageThemeMap()
    {
        var path = Path.Combine(Path.GetTempPath(), "cmts-prefs-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AppPreferencesStore.StoragePathOverride = path;
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
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Loc_SwitchesChineseAndEnglish()
    {
        Loc.SetLanguage(Loc.En);
        Assert.Equal("Load", Loc.T("Toolbar.Load"));
        Loc.SetLanguage(Loc.ZhCn);
        Assert.Equal("加载", Loc.T("Toolbar.Load"));
    }

    [Fact]
    public void MapTileSources_ResolveCustom()
    {
        var src = MapTileSources.Resolve("custom", "https://tiles.example/{z}/{x}/{y}.png");
        Assert.True(src.IsCustom);
        Assert.Contains("{z}", src.UrlTemplate);
    }

    [Fact]
    public void MapTileSources_PresetsIncludeOsmAndEsri()
    {
        Assert.Contains(MapTileSources.Presets, s => s.Id == "osm");
        Assert.Contains(MapTileSources.Presets, s => s.Id == "esri-imagery");
        Assert.Contains(MapTileSources.Presets, s => s.Id == "opentopo");
    }
}
