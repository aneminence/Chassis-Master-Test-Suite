namespace Chassis_Master_Test_Suite.Map;

/// <summary>
/// A slippy-map XYZ tile source. Presets mirror common Ovi / 奥维-style public layers
/// that are legally redistributable as URL templates (no Google/Gaode/Tianditu keys).
/// </summary>
public sealed class MapTileSource
{
    public required string Id { get; init; }
    public required string DisplayNameEn { get; init; }
    public required string DisplayNameZh { get; init; }
    public required string UrlTemplate { get; init; }
    public string Attribution { get; init; } = "";
    public int MaxZoom { get; init; } = 19;
    public int MinZoom { get; init; } = 1;
    public bool IsCustom { get; init; }

    public string DisplayName(string language) =>
        language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? DisplayNameZh
            : DisplayNameEn;
}

public static class MapTileSources
{
    public const string CustomId = "custom";

    /// <summary>Built-in sources (OSM + Esri + Carto + OpenTopo — Ovi-style public set).</summary>
    public static IReadOnlyList<MapTileSource> Presets { get; } = new List<MapTileSource>
    {
        new()
        {
            Id = "osm",
            DisplayNameEn = "OpenStreetMap",
            DisplayNameZh = "OpenStreetMap 街道",
            UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap contributors",
            MaxZoom = 19
        },
        new()
        {
            Id = "opentopo",
            DisplayNameEn = "OpenTopoMap",
            DisplayNameZh = "OpenTopoMap 地形",
            UrlTemplate = "https://a.tile.opentopomap.org/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap, SRTM | © OpenTopoMap (CC-BY-SA)",
            MaxZoom = 17
        },
        new()
        {
            Id = "esri-imagery",
            DisplayNameEn = "Esri World Imagery",
            DisplayNameZh = "Esri 卫星影像",
            UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
            Attribution = "© Esri",
            MaxZoom = 19
        },
        new()
        {
            Id = "esri-street",
            DisplayNameEn = "Esri World Street",
            DisplayNameZh = "Esri 街道",
            UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}",
            Attribution = "© Esri",
            MaxZoom = 19
        },
        new()
        {
            Id = "esri-topo",
            DisplayNameEn = "Esri World Topo",
            DisplayNameZh = "Esri 地形",
            UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}",
            Attribution = "© Esri",
            MaxZoom = 19
        },
        new()
        {
            Id = "carto-light",
            DisplayNameEn = "Carto Positron",
            DisplayNameZh = "Carto 浅色",
            UrlTemplate = "https://basemaps.cartocdn.com/light_all/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap, © CARTO",
            MaxZoom = 20
        },
        new()
        {
            Id = "carto-dark",
            DisplayNameEn = "Carto Dark Matter",
            DisplayNameZh = "Carto 深色",
            UrlTemplate = "https://basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap, © CARTO",
            MaxZoom = 20
        },
        new()
        {
            Id = CustomId,
            DisplayNameEn = "Custom XYZ…",
            DisplayNameZh = "自定义 XYZ…",
            UrlTemplate = "",
            Attribution = "",
            IsCustom = true,
            MaxZoom = 22
        }
    };

    public static MapTileSource? Find(string? id) =>
        Presets.FirstOrDefault(s =>
            string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public static MapTileSource Resolve(string? id, string? customUrl)
    {
        var src = Find(id) ?? Presets[0];
        if (src.IsCustom)
        {
            return new MapTileSource
            {
                Id = CustomId,
                DisplayNameEn = src.DisplayNameEn,
                DisplayNameZh = src.DisplayNameZh,
                UrlTemplate = string.IsNullOrWhiteSpace(customUrl)
                    ? "https://tile.openstreetmap.org/{z}/{x}/{y}.png"
                    : customUrl.Trim(),
                Attribution = "Custom",
                IsCustom = true,
                MaxZoom = 22
            };
        }

        return src;
    }
}
