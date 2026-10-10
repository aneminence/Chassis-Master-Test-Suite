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
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // OSM requires a descriptive User-Agent.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("CMTS/0.2 (Chassis Master Test Suite; map basemap)");
        return c;
    }

    public string CacheKey(string sourceId, int z, int x, int y) =>
        $"{sourceId}/{z}/{x}/{y}";

    public string DiskPath(string sourceId, int z, int x, int y) =>
        Path.Combine(_root, sourceId, z.ToString(), x.ToString(), $"{y}.png");

    public bool TryGetFromMemory(string key, out byte[] bytes) =>
        _memory.TryGetValue(key, out bytes!);

    public byte[]? TryReadDisk(string sourceId, int z, int x, int y)
    {
        try
        {
            var path = DiskPath(sourceId, z, x, y);
            if (!File.Exists(path))
                return null;
            var bytes = File.ReadAllBytes(path);
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
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length < 32)
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _memory.Clear();
    }
}
