using System.IO;
using System.Text.Json;
using System.Diagnostics;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Map;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// Owns the WebView2/Cesium boundary. The rest of the application continues to
/// deal only in VehicleSample objects and does not depend on Cesium concepts.
/// </summary>
internal sealed class TrackMap3DController
{
    private const int MaxPointsPerTrack = 20_000;
    private const string VirtualHost = "cmts-trackmap.local";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly WebView2 _webView;
    private bool _initialized;
    private bool _pageReady;
    private bool _active;
    private IReadOnlyList<TrackMapPanel.TrackLayer> _tracks = Array.Empty<TrackMapPanel.TrackLayer>();
    private VehicleSample? _cursor;
    private string _altitudeMode = "recorded";
    private double _exaggeration = 1.0;
    private readonly Stopwatch _sendStopwatch = Stopwatch.StartNew();
    private int[] _lastSentCounts = Array.Empty<int>();
    private (double Latitude, double Longitude)? _lastSentAnchor;
    private MapTileSource _mapSource = MapTileSources.Resolve(MapTileSources.DefaultId, null);

    public TrackMap3DController(WebView2 webView)
    {
        _webView = webView;
    }

    public event Action<string>? StatusChanged;
    public event Action? Ready;

    public async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        _initialized = true;
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.CoreWebView2.ProcessFailed += (_, e) =>
                StatusChanged?.Invoke($"3D renderer stopped ({e.ProcessFailedKind}). Switch to 2D and try again.");

            var contentFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "TrackMap3D");
            if (!Directory.Exists(contentFolder))
                throw new DirectoryNotFoundException($"3D map assets were not found: {contentFolder}");

            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                contentFolder,
                CoreWebView2HostResourceAccessKind.DenyCors);
            _webView.Source = new Uri($"https://{VirtualHost}/index.html");
        }
        catch (Exception ex)
        {
            _initialized = false;
            StatusChanged?.Invoke($"3D map is unavailable. Check the WebView2 Runtime.\n{ex.Message}");
        }
    }

    public void SetTracks(IReadOnlyList<TrackMapPanel.TrackLayer> tracks)
    {
        _tracks = tracks;
        if (!_active)
            return;

        var anchor = tracks.SelectMany(track => track.Samples)
            .FirstOrDefault(TrackProjection.IsValidGps);
        var counts = tracks.Select(track => track.Samples.Count).ToArray();
        var changedSource = anchor is not null &&
            (_lastSentAnchor is null
             || Math.Abs(_lastSentAnchor.Value.Latitude - anchor.Latitude) > 1e-9
             || Math.Abs(_lastSentAnchor.Value.Longitude - anchor.Longitude) > 1e-9
             || counts.Length != _lastSentCounts.Length
             || counts.Zip(_lastSentCounts, (current, previous) => current < previous || current - previous > 500).Any(changed => changed));

        if (!changedSource && _sendStopwatch.ElapsedMilliseconds < 1_000)
            return;

        _lastSentCounts = counts;
        _lastSentAnchor = anchor is null ? null : (anchor.Latitude, anchor.Longitude);
        _sendStopwatch.Restart();
        SendScene();
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (active)
        {
            _lastSentCounts = Array.Empty<int>();
            _lastSentAnchor = null;
            SendImagery();
            SendScene();
            SetCursor(_cursor);
        }
    }

    public void Clear()
    {
        _tracks = Array.Empty<TrackMapPanel.TrackLayer>();
        _cursor = null;
        _lastSentCounts = Array.Empty<int>();
        _lastSentAnchor = null;
        Post(new { type = "clear" });
    }

    public void SetCursor(VehicleSample? sample)
    {
        _cursor = sample;
        if (!_pageReady || !_active)
            return;

        Post(new
        {
            type = "cursor",
            payload = ToPoint(sample)
        });
    }

    public void SetDisplay(string altitudeMode, double exaggeration)
    {
        _altitudeMode = altitudeMode == "ground" ? "ground" : "recorded";
        _exaggeration = Math.Clamp(exaggeration, 1.0, 20.0);
        if (_active)
        {
            SendScene();
            SetCursor(_cursor);
        }
    }

    public void SetImagery(MapTileSource source)
    {
        _mapSource = source;
        if (_active)
            SendImagery();
    }

    public void Fit() => Post(new { type = "fit" });

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
                return;

            switch (typeElement.GetString())
            {
                case "ready":
                    _pageReady = true;
                    StatusChanged?.Invoke(string.Empty);
                    if (_active)
                    {
                        SendImagery();
                        SendScene();
                        SetCursor(_cursor);
                    }
                    Ready?.Invoke();
                    break;
                case "status":
                    if (root.TryGetProperty("message", out var message))
                        StatusChanged?.Invoke(message.GetString() ?? string.Empty);
                    break;
            }
        }
        catch
        {
            // Ignore malformed messages from page code; no untrusted page is loaded here.
        }
    }

    private void SendScene()
    {
        if (!_pageReady)
            return;

        var payload = _tracks
            .Select((track, index) => BuildTrack(track, index))
            .Where(track => track.Points.Count >= 2)
            .ToArray();

        Post(new
        {
            type = "tracks",
            payload = new
            {
                tracks = payload,
                altitudeMode = _altitudeMode,
                exaggeration = _exaggeration
            }
        });
    }

    private void SendImagery()
    {
        var url = _mapSource.UrlTemplate
            .Replace("{Z}", "{z}", StringComparison.Ordinal)
            .Replace("{X}", "{x}", StringComparison.Ordinal)
            .Replace("{Y}", "{y}", StringComparison.Ordinal)
            .Replace("{S}", "{s}", StringComparison.Ordinal)
            .Replace("{Q}", "{q}", StringComparison.Ordinal);
        Post(new
        {
            type = "imagery",
            payload = new
            {
                id = _mapSource.Id,
                url,
                maximumLevel = _mapSource.MaxZoom,
                attribution = _mapSource.Attribution
            }
        });
    }

    private static TrackDto BuildTrack(TrackMapPanel.TrackLayer track, int index)
    {
        var valid = track.Samples.Where(TrackProjection.IsValidGps).ToList();
        if (valid.Count == 0)
            return new TrackDto(index, track.Name, track.ColorHex, false, new List<double[]>());

        var finiteAltitudes = valid
            .Select(sample => sample.Altitude)
            .Where(double.IsFinite)
            .ToArray();
        // Importers historically used 0 for a missing altitude. Until the data
        // model carries explicit provenance, an all-zero track is treated as no altitude.
        var hasAltitude = finiteAltitudes.Any(value => Math.Abs(value) > 0.01);
        var baseline = hasAltitude ? finiteAltitudes.Min() : 0.0;
        var indices = SelectIndices(valid.Count, MaxPointsPerTrack);
        var points = new List<double[]>(indices.Count);
        foreach (var i in indices)
        {
            var sample = valid[i];
            points.Add(new[]
            {
                sample.Longitude,
                sample.Latitude,
                double.IsFinite(sample.Altitude) ? sample.Altitude : 0.0,
                (double)i
            });
        }

        return new TrackDto(
            index,
            string.IsNullOrWhiteSpace(track.Name) ? $"Track {index + 1}" : track.Name,
            string.IsNullOrWhiteSpace(track.ColorHex) ? "#42A5F5" : track.ColorHex,
            hasAltitude,
            points,
            baseline);
    }

    private static List<int> SelectIndices(int count, int maximum)
    {
        if (count <= maximum)
            return Enumerable.Range(0, count).ToList();

        var result = new List<int>(maximum) { 0 };
        var step = (count - 1.0) / (maximum - 1.0);
        for (var i = 1; i < maximum - 1; i++)
            result.Add((int)Math.Round(i * step));
        result.Add(count - 1);
        return result;
    }

    private static object? ToPoint(VehicleSample? sample)
    {
        if (sample is null || !TrackProjection.IsValidGps(sample))
            return null;
        return new
        {
            longitude = sample.Longitude,
            latitude = sample.Latitude,
            altitude = double.IsFinite(sample.Altitude) ? sample.Altitude : 0.0
        };
    }

    private void Post(object value)
    {
        if (!_pageReady || _webView.CoreWebView2 is null)
            return;
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, JsonOptions));
    }

    private sealed record TrackDto(
        int Id,
        string Name,
        string Color,
        bool HasAltitude,
        List<double[]> Points,
        double Baseline = 0.0);
}
