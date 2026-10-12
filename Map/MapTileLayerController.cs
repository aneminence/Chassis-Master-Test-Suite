using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chassis_Master_Test_Suite.Controls;
using Chassis_Master_Test_Suite.Themes;

namespace Chassis_Master_Test_Suite.Map;

/// <summary>
/// Draws XYZ tiles below the track. Placement follows every completed plot frame;
/// only fetching a new set of tiles is debounced.
/// </summary>
public sealed class MapTileLayerController : IDisposable
{
    private readonly Canvas _canvas;
    private readonly Func<ScottPlot.WPF.WpfPlot?> _plotGetter;
    private readonly Func<TrackProjection?> _projectionGetter;
    private readonly MapTileCache _cache;
    private readonly Dictionary<TileKey, Image> _images = new();
    private CancellationTokenSource? _cts;
    private int _generation;
    private bool _enabled;
    private bool _disposed;
    private string _sourceId = MapTileSources.DefaultId;
    private string _urlTemplate = MapTileSources.Presets[0].UrlTemplate;
    private int _maxZoom = 19;
    private double _opacity = 0.92;
    private MapViewport? _viewport;
    private TileCoverage? _requestedCoverage;
    private TileCoverage? _pendingCoverage;
    private int _debounceGeneration;
    private const int DebounceMs = 120;

    private readonly record struct TileKey(int Zoom, int X, int Y);
    private readonly record struct TileCoverage(int Zoom, int X0, int Y0, int X1, int Y1);

    public MapTileLayerController(
        Canvas canvas,
        Func<ScottPlot.WPF.WpfPlot?> plotGetter,
        Func<TrackProjection?> projectionGetter)
        : this(canvas, plotGetter, projectionGetter, new MapTileCache())
    {
    }

    internal MapTileLayerController(
        Canvas canvas,
        Func<ScottPlot.WPF.WpfPlot?> plotGetter,
        Func<TrackProjection?> projectionGetter,
        MapTileCache cache)
    {
        _canvas = canvas;
        _plotGetter = plotGetter;
        _projectionGetter = projectionGetter;
        _cache = cache;
        _canvas.IsHitTestVisible = false;
        _canvas.ClipToBounds = true;
        _canvas.UseLayoutRounding = false;
        _canvas.SnapsToDevicePixels = false;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_disposed || _enabled == value)
                return;
            _enabled = value;
            if (!_enabled)
                ClearTiles();
            else
                Invalidate(force: true);
        }
    }

    public void ApplyPreferences()
    {
        var prefs = AppearanceService.Preferences.Map;
        _opacity = prefs.Opacity;
        SetSourceCore(AppearanceService.CurrentMapSource());
        Enabled = prefs.BasemapEnabled;
        Invalidate(force: true);
    }

    public void SetSource(MapTileSource source)
    {
        SetSourceCore(source);
        Invalidate(force: true);
    }

    private void SetSourceCore(MapTileSource source)
    {
        if (_sourceId != source.Id || _urlTemplate != source.UrlTemplate || _maxZoom != source.MaxZoom)
            ClearTiles();
        _sourceId = source.Id;
        _urlTemplate = source.UrlTemplate;
        _maxZoom = source.MaxZoom;
    }

    /// <summary>Called with the completed frame, never with pending axis limits.</summary>
    public void OnRenderFinished(ScottPlot.RenderDetails render)
    {
        if (_disposed)
            return;

        var plot = _plotGetter();
        var projection = _projectionGetter();
        _viewport = plot is null || projection is null
            ? null
            : MapViewport.Capture(plot, _canvas, projection, render);
        if (_viewport is null)
        {
            ClearTiles();
            return;
        }

        if (_enabled)
        {
            RepositionTiles();
            ScheduleTiles(force: false);
        }
    }

    public void Invalidate(bool force = false)
    {
        if (!_enabled || _disposed)
            return;

        // Loading a different track may change the projection before its first
        // frame. Do not combine that new origin with the previous frame's axes.
        if (_viewport is null || !ReferenceEquals(_viewport.Projection, _projectionGetter()))
        {
            ClearTiles();
            _plotGetter()?.Refresh();
            return;
        }

        RepositionTiles();
        ScheduleTiles(force);
    }

    private void RepositionTiles()
    {
        if (_viewport is null)
            return;
        if (_canvas.Clip is not RectangleGeometry clip || clip.Rect != _viewport.DataArea)
            _canvas.Clip = new RectangleGeometry(_viewport.DataArea);
        foreach (var (key, img) in _images)
            PlaceImage(img, key, _viewport);
    }

    private void ScheduleTiles(bool force)
    {
        if (_viewport is null)
            return;
        var coverage = GetCoverage(_viewport);
        if (coverage == _requestedCoverage)
        {
            // Returning to the currently loaded area cancels a pending request.
            _pendingCoverage = null;
            _debounceGeneration++;
            return;
        }

        if (!force && coverage == _pendingCoverage)
            return; // repeated renders must not postpone downloads indefinitely

        _pendingCoverage = coverage;
        var token = ++_debounceGeneration;
        if (force)
            UpdateTiles(coverage);
        else
            _ = DebouncedLoadAsync(token, coverage);
    }

    private async Task DebouncedLoadAsync(int token, TileCoverage coverage)
    {
        await Task.Delay(DebounceMs).ConfigureAwait(false);
        if (_canvas.Dispatcher.HasShutdownStarted)
            return;
        try
        {
            await _canvas.Dispatcher.InvokeAsync(() =>
            {
                if (!_disposed && _enabled && token == _debounceGeneration)
                    UpdateTiles(coverage);
            });
        }
        catch (TaskCanceledException)
        {
            // The dispatcher may shut down between the check and the queued call.
        }
    }

    private TileCoverage GetCoverage(MapViewport viewport)
    {
        var limits = viewport.Limits;
        var projection = viewport.Projection;
        var (north, west) = projection.ToLatLon(limits.Left, limits.Top);
        var (south, east) = projection.ToLatLon(limits.Right, limits.Bottom);
        var metersPerPixel = (limits.Right - limits.Left) / viewport.RenderDataWidth;
        var zoom = Math.Clamp(MapTileMath.SuggestZoom(metersPerPixel, projection.OriginLatitude), 1, _maxZoom);

        while (true)
        {
            var (x0, y0) = MapTileMath.LatLonToTile(north, west, zoom);
            var (x1, y1) = MapTileMath.LatLonToTile(south, east, zoom);
            if (x1 < x0) (x0, x1) = (x1, x0);
            if (y1 < y0) (y0, y1) = (y1, y0);
            var n = 1 << zoom;
            x0 = Math.Max(0, x0 - 1);
            y0 = Math.Max(0, y0 - 1);
            x1 = Math.Min(n - 1, x1 + 1);
            y1 = Math.Min(n - 1, y1 + 1);
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) <= 64 || zoom == 1)
                return new TileCoverage(zoom, x0, y0, x1, y1);
            zoom--;
        }
    }

    private void UpdateTiles(TileCoverage coverage)
    {
        if (_viewport is null)
            return;
        _pendingCoverage = null;
        _requestedCoverage = coverage;
        var gen = ++_generation;
        Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var needed = new HashSet<TileKey>();
        for (var x = coverage.X0; x <= coverage.X1; x++)
        {
            for (var y = coverage.Y0; y <= coverage.Y1; y++)
            {
                var key = new TileKey(coverage.Zoom, x, y);
                needed.Add(key);
                if (!_images.TryGetValue(key, out var img))
                {
                    img = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
                    _images[key] = img;
                    _canvas.Children.Add(img);
                }
                PlaceImage(img, key, _viewport);
                if (img.Source is null)
                    _ = LoadTileAsync(gen, key, _sourceId, _urlTemplate, ct);
            }
        }

        foreach (var key in _images.Keys.Where(key => !needed.Contains(key)).ToArray())
        {
            _canvas.Children.Remove(_images[key]);
            _images.Remove(key);
        }
    }

    private void PlaceImage(Image img, TileKey key, MapViewport viewport)
    {
        var rect = viewport.TileRectangle(key.Zoom, key.X, key.Y);
        Canvas.SetLeft(img, rect.Left);
        Canvas.SetTop(img, rect.Top);
        img.Width = rect.Width;
        img.Height = rect.Height;
        img.Opacity = _opacity;
    }

    private async Task LoadTileAsync(int gen, TileKey key, string sourceId, string urlTemplate, CancellationToken ct)
    {
        try
        {
            var bytes = await _cache.GetOrDownloadAsync(sourceId, urlTemplate, key.Zoom, key.X, key.Y, ct)
                .ConfigureAwait(false);
            if (bytes is null || ct.IsCancellationRequested)
                return;

            await _canvas.Dispatcher.InvokeAsync(() =>
            {
                if (_disposed || !_enabled || ct.IsCancellationRequested || gen != _generation ||
                    !_images.TryGetValue(key, out var img) || _viewport is null)
                    return;

                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                img.Source = bmp;
                // Downloads only supply imagery. Position always uses the latest
                // completed frame, including pans made while the request was in flight.
                PlaceImage(img, key, _viewport);
            });
        }
        catch
        {
            // Missing/offline tiles and decode errors leave an empty placeholder.
        }
    }

    private void ClearTiles()
    {
        _generation++;
        _debounceGeneration++;
        Cancel();
        _pendingCoverage = null;
        _requestedCoverage = null;
        _canvas.Children.Clear();
        _images.Clear();
    }

    private void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _enabled = false;
        ClearTiles();
        _cache.Dispose();
    }
}
