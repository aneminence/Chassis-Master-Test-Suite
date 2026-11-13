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

    private readonly WebView2CompositionControl _webView;
    private bool _initialized;
    private Task? _initializationTask;
    private TaskCompletionSource? _pageLoad;
    private bool _pageReady;
    private bool _active;
    private IReadOnlyList<TrackMapPanel.TrackLayer> _tracks = Array.Empty<TrackMapPanel.TrackLayer>();
    private IReadOnlyList<TrackMapPanel.TrackLayer> _sentSceneTracks = Array.Empty<TrackMapPanel.TrackLayer>();
    private long _sceneRevision;
    private int? _selectedTrackSourceIndex;
    private readonly Dictionary<VehicleSample, (int TrackId, int SampleIndex)> _sampleLocations =
        new(ReferenceEqualityComparer.Instance);
    private VehicleSample? _cursor;
    private IReadOnlyList<TrackMapPanel.CompareCursor> _compareCursors = Array.Empty<TrackMapPanel.CompareCursor>();
    private string _altitudeMode = "ground";
    private IReadOnlyList<GateDto> _gates = Array.Empty<GateDto>();
    private readonly Stopwatch _sendStopwatch = Stopwatch.StartNew();
    private readonly TrackUpdateGate _sceneGate = new();
    private MapTileSource _mapSource = MapTileSources.Resolve(MapTileSources.DefaultId, null);

    public TrackMap3DController(WebView2CompositionControl webView)
    {
        _webView = webView;
    }

    public event Action<string>? StatusChanged;
    public event Action? Ready;
    public event Action? ManualDrag;
    public event Action<IReadOnlyList<VehicleSample>, int, int>? SampleSelected;
    public event Action<Guid>? GateSelected;

    public Task EnsureInitializedAsync()
    {
        if (_pageReady)
        {
            StatusChanged?.Invoke(string.Empty);
            return Task.CompletedTask;
        }
        if (_initializationTask is { IsCompleted: false })
            return _initializationTask;
        return _initializationTask = InitializePageAsync();
    }

    private async Task InitializePageAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async().WaitAsync(TimeSpan.FromSeconds(30));
            if (!_initialized)
            {
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _webView.CoreWebView2.ProcessFailed += (_, e) =>
                {
                    _pageReady = false;
                    _pageLoad?.TrySetException(new InvalidOperationException($"3D renderer stopped ({e.ProcessFailedKind})."));
                    StatusChanged?.Invoke($"3D renderer stopped ({e.ProcessFailedKind}). Switch to 2D and try again.");
                };
                _webView.CoreWebView2.NavigationCompleted += (_, e) =>
                {
                    if (!e.IsSuccess)
                        _pageLoad?.TrySetException(new InvalidOperationException($"3D page navigation failed: {e.WebErrorStatus}"));
                };
                _initialized = true;
            }

            var contentFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "TrackMap3D");
            if (!Directory.Exists(contentFolder))
                throw new DirectoryNotFoundException($"3D map assets were not found: {contentFolder}");

            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                contentFolder,
                CoreWebView2HostResourceAccessKind.DenyCors);
            _pageLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // Navigate explicitly on retries; assigning the same Source need not reload.
            _webView.CoreWebView2.Navigate($"https://{VirtualHost}/index.html");
            await _pageLoad.Task.WaitAsync(TimeSpan.FromSeconds(45));
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"3D map could not finish loading. Switch to 2D and back to retry.\n{ex.Message}");
        }
    }

    public void SetTracks(IReadOnlyList<TrackMapPanel.TrackLayer> tracks)
    {
        if (tracks.Count != _tracks.Count || tracks.Where((track, index) => index < _tracks.Count
            && (track.IsVisible != _tracks[index].IsVisible || track.SourceIndex != _tracks[index].SourceIndex)).Any())
            _sceneGate.Reset();
        _tracks = tracks;
        if (!_active)
            return;

        var stamps = tracks.Select(track => new TrackUpdateStamp(
            track.Identity ?? track.Samples,
            track.Samples.Count > 0 ? track.Samples[0] : null,
            track.Samples.Count > 0 ? track.Samples[^1] : null,
            track.Samples.Count, track.Name, track.ColorHex, track.IsLive)).ToArray();
        // Endpoint references distinguish a rolling full buffer from unchanged
        // data even when Count remains constant. Snapshot containers are ignored.
        if (!_sceneGate.TryAccept(stamps, _sendStopwatch.ElapsedMilliseconds))
            return;

        RebuildSampleLocations();
        SendScene();
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (active)
        {
            _sceneGate.Reset();
            RebuildSampleLocations();
            SendImagery();
            SendScene();
            SendGates();
            if (_compareCursors.Count > 0)
                SetCompareCursors(_compareCursors);
            else
                SetCursor(_cursor);
        }
    }

    public void Clear()
    {
        _tracks = Array.Empty<TrackMapPanel.TrackLayer>();
        _sentSceneTracks = Array.Empty<TrackMapPanel.TrackLayer>();
        _sceneRevision++;
        _sampleLocations.Clear();
        _cursor = null;
        _compareCursors = Array.Empty<TrackMapPanel.CompareCursor>();
        _sceneGate.Reset();
        _gates = Array.Empty<GateDto>();
        Post(new { type = "clear" });
    }

    public void SetCursor(VehicleSample? sample)
    {
        _cursor = sample;
        _compareCursors = Array.Empty<TrackMapPanel.CompareCursor>();
        if (!_pageReady || !_active)
            return;

        Post(new
        {
            type = "cursor",
            payload = ToCursorPoint(sample)
        });
    }

    public void SetCompareCursors(IReadOnlyList<TrackMapPanel.CompareCursor> cursors)
    {
        _compareCursors = cursors ?? Array.Empty<TrackMapPanel.CompareCursor>();
        _cursor = null;
        if (!_pageReady || !_active)
            return;
        Post(new
        {
            type = "cursors",
            payload = _compareCursors.Select(cursor =>
                ToCursorPoint(cursor.Sample, cursor.ColorHex, cursor.RunNumber))
                .Where(point => point is not null)
                .ToArray()
        });
    }

    public void SetDisplay(string altitudeMode)
    {
        _altitudeMode = altitudeMode == "ground" ? "ground" : "recorded";
        if (_active)
        {
            // Force a full scene resend so JS recreates polylines when clamp/height mode changes.
            _sceneGate.Reset();
            RebuildSampleLocations();
            SendScene();
            SendGates();
            if (_compareCursors.Count > 0)
                SetCompareCursors(_compareCursors);
            else
                SetCursor(_cursor);
        }
    }

    public void SetGates(IReadOnlyList<GateDto> gates)
    {
        _gates = gates ?? Array.Empty<GateDto>();
        if (_active)
            SendGates();
    }

    public void SetImagery(MapTileSource source)
    {
        _mapSource = source;
        if (_active)
            SendImagery();
    }

    private bool _followVehicle;
    public void SetFollowVehicle(bool enabled)
    {
        _followVehicle = enabled;
        Post(new { type = "followVehicle", enabled });
    }
    public void Fit() => Post(new { type = "fit" });

    public void SetSelectedTrack(int sourceIndex)
    {
        _selectedTrackSourceIndex = sourceIndex;
        Post(new { type = "selectedTrack", trackId = SelectedTrackId });
    }

    private int SelectedTrackId
    {
        get
        {
            for (var i = 0; i < _tracks.Count; i++)
                if (_tracks[i].SourceIndex == _selectedTrackSourceIndex)
                    return i;
            return _selectedTrackSourceIndex is null && _tracks.Count > 0 ? 0 : -1;
        }
    }

    private void RebuildSampleLocations()
    {
        // Do not pre-index every sample: a multi-million-point NCOM track would
        // allocate a Dictionary with millions of entries and hitch the UI.
        // Cursor look-ups use Sequence (O(tracks)) with a small LRU cache.
        _sampleLocations.Clear();
    }

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
                    _pageLoad?.TrySetResult();
                    SetFollowVehicle(_followVehicle);
                    StatusChanged?.Invoke(string.Empty);
                    if (_active)
                    {
                        SendImagery();
                        SendScene();
                        SendGates();
                        if (_compareCursors.Count > 0)
                            SetCompareCursors(_compareCursors);
                        else
                            SetCursor(_cursor);
                    }
                    Ready?.Invoke();
                    break;
                case "status":
                    if (root.TryGetProperty("message", out var message))
                    {
                        if (!_pageReady && !string.IsNullOrWhiteSpace(message.GetString()))
                            _pageLoad?.TrySetException(new InvalidOperationException(message.GetString()));
                        StatusChanged?.Invoke(message.GetString() ?? string.Empty);
                    }
                    break;
                case "manualDrag":
                    _followVehicle = false;
                    ManualDrag?.Invoke();
                    break;
                case "sampleSelected":
                    if (root.TryGetProperty("trackId", out var trackIdElement)
                        && root.TryGetProperty("sampleIndex", out var sampleIndexElement)
                        && trackIdElement.TryGetInt32(out var trackId)
                        && sampleIndexElement.TryGetInt32(out var sampleIndex)
                        && root.TryGetProperty("sceneRevision", out var revisionElement)
                        && revisionElement.TryGetInt64(out var revision)
                        && revision == _sceneRevision
                        && trackId >= 0 && trackId < _sentSceneTracks.Count
                        && _sentSceneTracks[trackId].IsVisible)
                    {
                        // The live ring buffer can advance while geometry is
                        // throttled. Resolve against the scene actually sent.
                        var track = _sentSceneTracks[trackId];
                        SampleSelected?.Invoke(track.Samples,
                            track.SourceIndex >= 0 ? track.SourceIndex : trackId, sampleIndex);
                    }
                    break;
                case "gateSelected":
                    if (root.TryGetProperty("gateId", out var gateIdElement)
                        && Guid.TryParse(gateIdElement.GetString(), out var gateId)
                        && _gates.Any(gate => Guid.TryParse(gate.Id, out var existing) && existing == gateId))
                        GateSelected?.Invoke(gateId);
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

        _sentSceneTracks = _tracks.ToArray();
        var revision = ++_sceneRevision;
        var payload = _sentSceneTracks
            .Select((track, index) => BuildTrack(track, index))
            .ToArray();

        Post(new
        {
            type = "tracks",
            payload = new
            {
                tracks = payload,
                selectedTrackId = SelectedTrackId,
                sceneRevision = revision,
                altitudeMode = _altitudeMode
            }
        });
    }

    private void SendGates()
    {
        if (!_pageReady)
            return;
        Post(new
        {
            type = "gates",
            payload = _gates
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
        var valid = track.Samples
            .Select((sample, originalIndex) => (Sample: sample, OriginalIndex: originalIndex))
            .Where(item => TrackProjection.IsValidGps(item.Sample))
            .ToList();
        if (valid.Count == 0)
            return new TrackDto(index, track.Name, track.ColorHex, false, new List<double[]>(), IsVisible: track.IsVisible);

        var finiteAltitudes = valid
            .Select(item => item.Sample.Altitude)
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
            var sample = valid[i].Sample;
            points.Add(new[]
            {
                sample.Longitude,
                sample.Latitude,
                double.IsFinite(sample.Altitude) ? sample.Altitude : 0.0,
                (double)valid[i].OriginalIndex
            });
        }

        return new TrackDto(
            index,
            string.IsNullOrWhiteSpace(track.Name) ? $"Track {index + 1}" : track.Name,
            string.IsNullOrWhiteSpace(track.ColorHex) ? "#42A5F5" : track.ColorHex,
            hasAltitude,
            points,
            baseline,
            track.IsVisible);
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

    private object? ToCursorPoint(
        VehicleSample? sample,
        string? color = null,
        int? runNumber = null)
    {
        if (sample is null || !TrackProjection.IsValidGps(sample))
            return null;

        var location = _sampleLocations.TryGetValue(sample, out var found)
            ? found
            : FindSampleLocation(sample);
        if (location.Item1 >= 0 && location.Item1 < _tracks.Count && !_tracks[location.Item1].IsVisible)
            return null;
        return new
        {
            trackId = location.Item1,
            sampleIndex = location.Item2,
            longitude = sample.Longitude,
            latitude = sample.Latitude,
            altitude = double.IsFinite(sample.Altitude) ? sample.Altitude : 0.0,
            heading = ResolveCursorHeadingDeg(sample, location.Item1, location.Item2),
            color,
            runNumber
        };
    }

    /// <summary>
    /// Travel heading deg (0=N, clockwise). Prefer sample.Heading; else bearing from nearby track samples.
    /// </summary>
    private double ResolveCursorHeadingDeg(VehicleSample sample, int trackId, int sampleIndex)
    {
        if (double.IsFinite(sample.Heading))
            return sample.Heading;

        if (trackId < 0 || trackId >= _tracks.Count || sampleIndex < 0)
            return 0.0;

        var samples = _tracks[trackId].Samples;
        if (samples.Count < 2)
            return 0.0;

        var i0 = Math.Max(0, sampleIndex - 1);
        var i1 = Math.Min(samples.Count - 1, sampleIndex + 1);
        if (i1 <= i0)
        {
            i0 = Math.Max(0, sampleIndex - 3);
            i1 = Math.Min(samples.Count - 1, sampleIndex + 3);
        }
        if (i1 <= i0)
            return 0.0;

        var a = samples[i0];
        var b = samples[i1];
        if (!TrackProjection.IsValidGps(a) || !TrackProjection.IsValidGps(b))
            return 0.0;

        var mPerLon = 111320.0 * Math.Cos(a.Latitude * Math.PI / 180.0);
        var dx = (b.Longitude - a.Longitude) * mPerLon;
        var dy = (b.Latitude - a.Latitude) * 111320.0;
        if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6)
            return 0.0;

        var deg = Math.Atan2(dx, dy) * 180.0 / Math.PI;
        if (deg < 0)
            deg += 360.0;
        return deg;
    }

    private (int TrackId, int SampleIndex) FindSampleLocation(VehicleSample sample)
    {
        for (var trackId = 0; trackId < _tracks.Count; trackId++)
        {
            var samples = _tracks[trackId].Samples;
            // Importers assign Sequence = index in the track list; O(1) when true.
            if (sample.Sequence >= 0
                && sample.Sequence < samples.Count
                && ReferenceEquals(samples[(int)sample.Sequence], sample))
            {
                var location = (trackId, (int)sample.Sequence);
                CacheSampleLocation(sample, location);
                return location;
            }
        }

        // Rare fallback (trimmed history / mismatched Sequence): linear scan.
        for (var trackId = 0; trackId < _tracks.Count; trackId++)
        {
            var samples = _tracks[trackId].Samples;
            for (var sampleIndex = samples.Count - 1; sampleIndex >= 0; sampleIndex--)
            {
                if (!ReferenceEquals(samples[sampleIndex], sample))
                    continue;
                var location = (trackId, sampleIndex);
                CacheSampleLocation(sample, location);
                return location;
            }
        }

        return (-1, -1);
    }

    private void CacheSampleLocation(
        VehicleSample sample,
        (int TrackId, int SampleIndex) location)
    {
        // Keep the cache tiny so large offline tracks do not grow it unbounded.
        if (_sampleLocations.Count > 256)
            _sampleLocations.Clear();
        _sampleLocations[sample] = location;
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
        double Baseline = 0.0,
        bool IsVisible = true);

    public sealed record GateDto(
        string Id,
        string Name,
        string ColorHex,
        double Lat0,
        double Lon0,
        double Alt0,
        double Lat1,
        double Lon1,
        double Alt1,
        bool Selected);
}
