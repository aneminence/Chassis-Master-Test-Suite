namespace Chassis_Master_Test_Suite.Map;

/// <summary>
/// Slippy-map XYZ/TMS tile source. Dropdown lists only HTTP-verified satellite/imagery
/// providers (probed 2026-10-10). Vector / street / topo / key-gated sources are omitted.
/// Custom XYZ remains for user-supplied Ovi-style templates ({z}/{x}/{y} or Bing {q}).
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
    public const string DefaultId = "esri-imagery";

    /// <summary>
    /// Built-in satellite/imagery sources that returned HTTP 200 image payloads in probe.
    /// </summary>
    public static IReadOnlyList<MapTileSource> Presets { get; } = new List<MapTileSource>
    {
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
            Id = "google-sat",
            DisplayNameEn = "Google Satellite",
            DisplayNameZh = "Google 卫星",
            UrlTemplate = "https://mt{s}.google.com/vt/lyrs=s&x={x}&y={y}&z={z}",
            Attribution = "© Google",
            MaxZoom = 20
        },
        new()
        {
            Id = "google-hybrid",
            DisplayNameEn = "Google Hybrid",
            DisplayNameZh = "Google 混合",
            UrlTemplate = "https://mt{s}.google.com/vt/lyrs=y&x={x}&y={y}&z={z}",
            Attribution = "© Google",
            MaxZoom = 20
        },
        new()
        {
            Id = "bing-aerial",
            DisplayNameEn = "Bing Aerial",
            DisplayNameZh = "Bing 航拍",
            // {q} = Bing quadkey; {s} = 0–3 subdomain digit
            UrlTemplate = "https://ecn.t{s}.tiles.virtualearth.net/tiles/a{q}.jpeg?g=14645&mkt=en-US",
            Attribution = "© Microsoft",
            MaxZoom = 19
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

    public static bool IsKnownId(string? id) =>
        !string.IsNullOrWhiteSpace(id) &&
        (string.Equals(id, CustomId, StringComparison.OrdinalIgnoreCase) || Find(id) is not null);

    public static MapTileSource Resolve(string? id, string? customUrl, string? tiandituKey = null)
    {
        var src = Find(id) ?? Find(DefaultId) ?? Presets[0];
        if (src.IsCustom)
        {
            return new MapTileSource
            {
                Id = CustomId,
                DisplayNameEn = src.DisplayNameEn,
                DisplayNameZh = src.DisplayNameZh,
                UrlTemplate = string.IsNullOrWhiteSpace(customUrl)
                    ? Find(DefaultId)!.UrlTemplate
                    : customUrl.Trim(),
                Attribution = "Custom",
                IsCustom = true,
                MaxZoom = 22
            };
        }

        // Tianditu key injection kept for forward-compat if a key-gated imagery source returns.
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
