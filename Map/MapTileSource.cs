namespace Chassis_Master_Test_Suite.Map;

/// <summary>
/// A slippy-map XYZ/TMS tile source. Presets mirror an Ovi / 奥维-style basemap menu:
/// OSM / Carto / Esri / Tianditu / common imagery variants, plus Custom XYZ.
/// Commercial Google/Gaode endpoints are omitted (ToS); use Custom with your own URL if needed.
/// Tianditu templates use {tk} — set MapPreferences.TiandituKey (prompted on first select).
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
    public bool NeedsTiandituKey { get; init; }

    public string DisplayName(string language) =>
        language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? DisplayNameZh
            : DisplayNameEn;
}

public static class MapTileSources
{
    public const string CustomId = "custom";

    /// <summary>Built-in Ovi-style public / key-gated sources.</summary>
    public static IReadOnlyList<MapTileSource> Presets { get; } = new List<MapTileSource>
    {
        // —— OpenStreetMap family ——
        new()
        {
            Id = "osm",
            DisplayNameEn = "OSM Standard",
            DisplayNameZh = "OSM 标准街道",
            UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap contributors",
            MaxZoom = 19
        },
        new()
        {
            Id = "osm-hot",
            DisplayNameEn = "OSM Humanitarian",
            DisplayNameZh = "OSM 人道救援",
            UrlTemplate = "https://tile-{s}.openstreetmap.fr/hot/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap, HOT",
            MaxZoom = 19
        },
        new()
        {
            Id = "opentopo",
            DisplayNameEn = "OpenTopoMap",
            DisplayNameZh = "OpenTopoMap 地形",
            UrlTemplate = "https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap, SRTM | © OpenTopoMap (CC-BY-SA)",
            MaxZoom = 17
        },
        new()
        {
            Id = "cyclosm",
            DisplayNameEn = "CyclOSM",
            DisplayNameZh = "CyclOSM 骑行",
            UrlTemplate = "https://{s}.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap, CyclOSM",
            MaxZoom = 20
        },

        // —— Carto ——
        new()
        {
            Id = "carto-light",
            DisplayNameEn = "Carto Positron (light)",
            DisplayNameZh = "Carto 浅色",
            UrlTemplate = "https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap, © CARTO",
            MaxZoom = 20
        },
        new()
        {
            Id = "carto-dark",
            DisplayNameEn = "Carto Dark Matter",
            DisplayNameZh = "Carto 深色",
            UrlTemplate = "https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap, © CARTO",
            MaxZoom = 20
        },
        new()
        {
            Id = "carto-voyager",
            DisplayNameEn = "Carto Voyager",
            DisplayNameZh = "Carto Voyager",
            UrlTemplate = "https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap, © CARTO",
            MaxZoom = 20
        },

        // —— Esri ——
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
            Id = "esri-clarity",
            DisplayNameEn = "Esri Clarity (imagery)",
            DisplayNameZh = "Esri Clarity 影像",
            UrlTemplate = "https://clarity.maptiles.arcgis.com/arcgis/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
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
            Id = "esri-gray",
            DisplayNameEn = "Esri Light Gray",
            DisplayNameZh = "Esri 浅灰底图",
            UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Light_Gray_Base/MapServer/tile/{z}/{y}/{x}",
            Attribution = "© Esri",
            MaxZoom = 16
        },

        // —— Tianditu (needs free tk key) ——
        new()
        {
            Id = "tdt-img",
            DisplayNameEn = "Tianditu Imagery",
            DisplayNameZh = "天地图 影像",
            UrlTemplate = "https://t{s}.tianditu.gov.cn/DataServer?T=img_w&x={x}&y={y}&l={z}&tk={tk}",
            Attribution = "© 国家基础地理信息中心 / 天地图",
            MaxZoom = 18,
            NeedsTiandituKey = true
        },
        new()
        {
            Id = "tdt-img-anno",
            DisplayNameEn = "Tianditu Imagery + Labels",
            DisplayNameZh = "天地图 影像注记",
            UrlTemplate = "https://t{s}.tianditu.gov.cn/DataServer?T=cia_w&x={x}&y={y}&l={z}&tk={tk}",
            Attribution = "© 天地图",
            MaxZoom = 18,
            NeedsTiandituKey = true
        },
        new()
        {
            Id = "tdt-vec",
            DisplayNameEn = "Tianditu Vector",
            DisplayNameZh = "天地图 矢量",
            UrlTemplate = "https://t{s}.tianditu.gov.cn/DataServer?T=vec_w&x={x}&y={y}&l={z}&tk={tk}",
            Attribution = "© 天地图",
            MaxZoom = 18,
            NeedsTiandituKey = true
        },
        new()
        {
            Id = "tdt-vec-anno",
            DisplayNameEn = "Tianditu Vector + Labels",
            DisplayNameZh = "天地图 矢量注记",
            UrlTemplate = "https://t{s}.tianditu.gov.cn/DataServer?T=cva_w&x={x}&y={y}&l={z}&tk={tk}",
            Attribution = "© 天地图",
            MaxZoom = 18,
            NeedsTiandituKey = true
        },
        new()
        {
            Id = "tdt-ter",
            DisplayNameEn = "Tianditu Terrain",
            DisplayNameZh = "天地图 地形",
            UrlTemplate = "https://t{s}.tianditu.gov.cn/DataServer?T=ter_w&x={x}&y={y}&l={z}&tk={tk}",
            Attribution = "© 天地图",
            MaxZoom = 14,
            NeedsTiandituKey = true
        },

        // —— USGS / Wikimedia ——
        new()
        {
            Id = "usgs-topo",
            DisplayNameEn = "USGS Topo",
            DisplayNameZh = "USGS 地形图",
            UrlTemplate = "https://basemap.nationalmap.gov/arcgis/rest/services/USGSTopo/MapServer/tile/{z}/{y}/{x}",
            Attribution = "© USGS",
            MaxZoom = 16
        },
        new()
        {
            Id = "usgs-imagery",
            DisplayNameEn = "USGS Imagery",
            DisplayNameZh = "USGS 影像",
            UrlTemplate = "https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}",
            Attribution = "© USGS",
            MaxZoom = 16
        },
        new()
        {
            Id = "wikimedia",
            DisplayNameEn = "Wikimedia Maps",
            DisplayNameZh = "Wikimedia 地图",
            UrlTemplate = "https://maps.wikimedia.org/osm-intl/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap, © Wikimedia",
            MaxZoom = 19
        },

        // —— Custom ——
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

    public static MapTileSource Resolve(string? id, string? customUrl, string? tiandituKey = null)
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

        if (src.NeedsTiandituKey)
        {
            var tk = string.IsNullOrWhiteSpace(tiandituKey) ? "" : tiandituKey.Trim();
            return new MapTileSource
            {
                Id = src.Id,
                DisplayNameEn = src.DisplayNameEn,
                DisplayNameZh = src.DisplayNameZh,
                UrlTemplate = src.UrlTemplate.Replace("{tk}", tk, StringComparison.OrdinalIgnoreCase),
                Attribution = src.Attribution,
                MaxZoom = src.MaxZoom,
                MinZoom = src.MinZoom,
                NeedsTiandituKey = true
            };
        }

        return src;
    }
}
