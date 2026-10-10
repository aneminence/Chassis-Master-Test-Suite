using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;

namespace Chassis_Master_Test_Suite.Map;

/// <summary>
/// Disk + memory cache for map tiles. Files under LocalAppData\CMTS\map-tiles.
/// </summary>
public sealed class MapTileCache : IDisposable
{
    private static readonly HttpClient Http = CreateClient();

    private readonly ConcurrentDictionary<string, byte[]> _memory = new(StringComparer.Ordinal);
    private readonly string _root;
    private bool _disposed;

    public MapTileCache(string? rootOverride = null)
    {
        _root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CMTS",
            "map-tiles");
        Directory.CreateDirectory(_root);
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            AllowAutoRedirect = true
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        // OSM / Esri / Google tolerate a descriptive UA; some CDNs 403 anonymous defaults.
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (compatible; CMTS/0.2; Chassis Master Test Suite; +https://github.com/aneminence/Chassis-Master-Test-Suite)");
        c.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept",
            "image/avif,image/webp,image/apng,image/jpeg,image/png,image/*,*/*;q=0.8");
        return c;
    }

    public string CacheKey(string sourceId, int z, int x, int y) =>
        $"{sourceId}/{z}/{x}/{y}";

    public string DiskPath(string sourceId, int z, int x, int y) =>
        Path.Combine(_root, sourceId, z.ToString(), x.ToString(), $"{y}.tile");

    public bool TryGetFromMemory(string key, out byte[] bytes) =>
        _memory.TryGetValue(key, out bytes!);

    public byte[]? TryReadDisk(string sourceId, int z, int x, int y)
    {
        try
        {
            var path = DiskPath(sourceId, z, x, y);
            // Migrate old .png cache files if present
            if (!File.Exists(path))
            {
                var legacy = Path.Combine(
                    Path.GetDirectoryName(path) ?? _root,
                    $"{y}.png");
                if (File.Exists(legacy))
                    path = legacy;
                else
                    return null;
            }

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 32 || !LooksLikeImage(bytes))
                return null;
            _memory[CacheKey(sourceId, z, x, y)] = bytes;
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]?> GetOrDownloadAsync(
        string sourceId,
        string urlTemplate,
        int z,
        int x,
        int y,
        CancellationToken ct = default)
    {
        var key = CacheKey(sourceId, z, x, y);
        if (_memory.TryGetValue(key, out var cached))
            return cached;

        var disk = TryReadDisk(sourceId, z, x, y);
        if (disk is not null)
            return disk;

        var url = MapTileMath.FormatUrl(urlTemplate, z, x, y);
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // ArcGIS / Clarity often expect a browser-like Referer.
            if (url.Contains("arcgis", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("arcgisonline", StringComparison.OrdinalIgnoreCase))
            {
                req.Headers.TryAddWithoutValidation("Referer", "https://www.arcgis.com/");
            }
            else if (url.Contains("google.com", StringComparison.OrdinalIgnoreCase))
            {
                req.Headers.TryAddWithoutValidation("Referer", "https://maps.google.com/");
            }
            else if (url.Contains("virtualearth.net", StringComparison.OrdinalIgnoreCase))
            {
                req.Headers.TryAddWithoutValidation("Referer", "https://www.bing.com/");
            }

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length < 32 || !LooksLikeImage(bytes))
                return null;

            _memory[key] = bytes;
            try
            {
                var path = DiskPath(sourceId, z, x, y);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            }
            catch
            {
                // memory hit is enough
            }

            return bytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>PNG / JPEG / GIF / WebP magic — rejects HTML error pages saved as tiles.</summary>
    internal static bool LooksLikeImage(byte[] bytes)
    {
        if (bytes.Length < 12)
            return false;
        // PNG
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return true;
        // JPEG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return true;
        // GIF
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return true;
        // WebP: RIFF....WEBP
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return true;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _memory.Clear();
    }
}
