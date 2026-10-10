using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chassis_Master_Test_Suite.Controls;
using Chassis_Master_Test_Suite.Themes;

namespace Chassis_Master_Test_Suite.Map;

/// <summary>
/// Draws XYZ basemap tiles onto a Canvas, aligned to TrackMapPanel's local-meter plot.
/// Place the canvas UNDER the ScottPlot host. Call <see cref="Invalidate"/> after pan/zoom/render.
/// </summary>
public sealed class MapTileLayerController : IDisposable
{
    private readonly Canvas _canvas;
    private readonly Func<ScottPlot.WPF.WpfPlot?> _plotGetter;
    private readonly Func<TrackProjection?> _projectionGetter;
    private readonly MapTileCache _cache = new();
    private readonly Dictionary<string, Image> _images = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;
    private int _generation;
    private bool _enabled;
    private string _sourceId = MapTileSources.DefaultId;
    private string _urlTemplate = MapTileSources.Presets[0].UrlTemplate;
    private int _maxZoom = 19;
    private double _opacity = 0.92;
    private string _lastLayoutKey = "";
    private int _debounceGeneration;
    private const int DebounceMs = 120;

    public MapTileLayerController(
        Canvas canvas,
        Func<ScottPlot.WPF.WpfPlot?> plotGetter,
        Func<TrackProjection?> projectionGetter)
    {
        _canvas = canvas;
        _plotGetter = plotGetter;
        _projectionGetter = projectionGetter;
        _canvas.IsHitTestVisible = false;
        _canvas.ClipToBounds = true;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            if (!_enabled)
            {
                Cancel();
                _canvas.Children.Clear();
                _images.Clear();
                _lastLayoutKey = "";
            }
            else
            {
                Invalidate(force: true);
            }
        }
    }

    public void ApplyPreferences()
    {
        var prefs = AppearanceService.Preferences.Map;
        var src = AppearanceService.CurrentMapSource();
        _sourceId = src.Id;
        _urlTemplate = src.UrlTemplate;
        _maxZoom = src.MaxZoom;
        _opacity = prefs.Opacity;
        Enabled = prefs.BasemapEnabled;
        if (_enabled)
            Invalidate(force: true);
    }

    public void SetSource(MapTileSource source)
    {
        _sourceId = source.Id;
        _urlTemplate = source.UrlTemplate;
        _maxZoom = source.MaxZoom;
        if (_enabled)
            Invalidate(force: true);
    }

    public void Invalidate(bool force = false)
    {
        if (!_enabled)
            return;

        if (!force)
        {
            var token = Interlocked.Increment(ref _debounceGeneration);
            _ = DebouncedInvalidateAsync(token);
            return;
        }

        InvalidateCore(force: true);
    }

    private async Task DebouncedInvalidateAsync(int token)
    {
        try
        {
            await Task.Delay(DebounceMs).ConfigureAwait(true);
        }
        catch
        {
            return;
        }

        if (token != _debounceGeneration)
            return;

        if (!_canvas.Dispatcher.CheckAccess())
        {
            await _canvas.Dispatcher.InvokeAsync(() =>
            {
                if (token == _debounceGeneration)
                    InvalidateCore(force: false);
            });
            return;
        }

        InvalidateCore(force: false);
    }

    private void InvalidateCore(bool force)
    {
        if (!_enabled)
            return;

        var plot = _plotGetter();
        var projection = _projectionGetter();
        if (plot is null || projection is null)
            return;

        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;
        if (width < 8 || height < 8)
            return;

        ScottPlot.AxisLimits limits;
        try
        {
            limits = plot.Plot.Axes.GetLimits();
        }
        catch
        {
            return;
        }

        var spanX = limits.Right - limits.Left;
        if (spanX <= 0 || double.IsNaN(spanX))
            return;

        var metersPerPixel = spanX / width;
        var (latC, lonC) = projection.ToLatLon(
            (limits.Left + limits.Right) / 2.0,
            (limits.Bottom + limits.Top) / 2.0);

        var zoom = MapTileMath.SuggestZoom(metersPerPixel, latC);
        zoom = Math.Clamp(zoom, 1, _maxZoom);

        // Viewport corners in lat/lon
        var (latNW, lonNW) = projection.ToLatLon(limits.Left, limits.Top);
        var (latSE, lonSE) = projection.ToLatLon(limits.Right, limits.Bottom);
        var (latNE, lonNE) = projection.ToLatLon(limits.Right, limits.Top);
        var (latSW, lonSW) = projection.ToLatLon(limits.Left, limits.Bottom);

        var minLat = Math.Min(Math.Min(latNW, latNE), Math.Min(latSW, latSE));
        var maxLat = Math.Max(Math.Max(latNW, latNE), Math.Max(latSW, latSE));
        var minLon = Math.Min(Math.Min(lonNW, lonNE), Math.Min(lonSW, lonSE));
        var maxLon = Math.Max(Math.Max(lonNW, lonNE), Math.Max(lonSW, lonSE));

        var (x0, y0) = MapTileMath.LatLonToTile(maxLat, minLon, zoom);
        var (x1, y1) = MapTileMath.LatLonToTile(minLat, maxLon, zoom);
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);

        // Pad one tile
        var n = 1 << zoom;
        x0 = Math.Max(0, x0 - 1);
        y0 = Math.Max(0, y0 - 1);
        x1 = Math.Min(n - 1, x1 + 1);
        y1 = Math.Min(n - 1, y1 + 1);

        // Cap tile count for performance
        var count = (x1 - x0 + 1) * (y1 - y0 + 1);
        if (count > 64)
        {
            // Zoom out one level if too many tiles
            zoom = Math.Max(1, zoom - 1);
            (x0, y0) = MapTileMath.LatLonToTile(maxLat, minLon, zoom);
            (x1, y1) = MapTileMath.LatLonToTile(minLat, maxLon, zoom);
            if (x1 < x0) (x0, x1) = (x1, x0);
            if (y1 < y0) (y0, y1) = (y1, y0);
            n = 1 << zoom;
            x0 = Math.Max(0, x0 - 1);
            y0 = Math.Max(0, y0 - 1);
            x1 = Math.Min(n - 1, x1 + 1);
            y1 = Math.Min(n - 1, y1 + 1);
        }

        var layoutKey =
            $"{_sourceId}|{zoom}|{x0}:{y0}:{x1}:{y1}|{limits.Left:F1}:{limits.Right:F1}:{limits.Bottom:F1}:{limits.Top:F1}|{width:F0}x{height:F0}";
        if (!force && layoutKey == _lastLayoutKey)
            return;
        _lastLayoutKey = layoutKey;

        var gen = Interlocked.Increment(ref _generation);
        Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Reposition existing / schedule downloads
        var needed = new HashSet<string>(StringComparer.Ordinal);
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                var key = $"{zoom}/{x}/{y}";
                needed.Add(key);
                PositionOrCreatePlaceholder(key, plot, projection, zoom, x, y);
                _ = LoadTileAsync(gen, key, zoom, x, y, plot, projection, ct);
            }
        }

        // Remove unused images
        foreach (var key in _images.Keys.ToList())
        {
            if (needed.Contains(key))
                continue;
            if (_images.TryGetValue(key, out var img))
            {
                _canvas.Children.Remove(img);
                _images.Remove(key);
            }
        }
    }

    private void PositionOrCreatePlaceholder(
        string key,
        ScottPlot.WPF.WpfPlot plot,
        TrackProjection projection,
        int z, int x, int y)
    {
        if (!_images.TryGetValue(key, out var img))
        {
            img = new Image
            {
                Stretch = Stretch.Fill,
                Opacity = _opacity,
                IsHitTestVisible = false
            };
            _images[key] = img;
            _canvas.Children.Add(img);
        }
        else
        {
            img.Opacity = _opacity;
        }

        PlaceImage(img, plot, projection, z, x, y);
    }

    private static void PlaceImage(
        Image img,
        ScottPlot.WPF.WpfPlot plot,
        TrackProjection projection,
        int z, int x, int y)
    {
        // Tile corners in local Web Mercator metres (same CRS as the trajectory).
        var (west, north, east, south) = MapTileMath.TileMercatorBounds(x, y, z);
        var wx0 = west - projection.OriginMercatorX;
        var wyN = north - projection.OriginMercatorY;
        var wx1 = east - projection.OriginMercatorX;
        var wyS = south - projection.OriginMercatorY;

        ScottPlot.PixelRect data;
        ScottPlot.AxisLimits limits;
        try
        {
            data = plot.Plot.RenderManager.LastRender.DataRect;
            limits = plot.Plot.Axes.GetLimits();
        }
        catch
        {
            return;
        }

        // DataRect is in *unscaled* figure pixels (same space as WPF DIPs once
        // Plot.ScaleFactor is divided out of mouse hits). Do NOT use GetPixel here:
        // GetPixel multiplies by ScaleFactor, and placing those values on a WPF Canvas
        // makes the geographic error grow when zoomed out on high-DPI displays.
        var spanX = limits.Right - limits.Left;
        var spanY = limits.Top - limits.Bottom;
        if (data.Width < 1 || data.Height < 1 || spanX <= 0 || spanY <= 0)
            return;

        var sx = data.Width / spanX;
        var sy = data.Height / spanY;

        // Screen Y grows downward; plot Y (northing) grows upward.
        var left = data.Left + (wx0 - limits.Left) * sx;
        var right = data.Left + (wx1 - limits.Left) * sx;
        var top = data.Top + (limits.Top - wyN) * sy;
        var bottom = data.Top + (limits.Top - wyS) * sy;

        var w = Math.Abs(right - left);
        var h = Math.Abs(bottom - top);
        if (w < 1 || h < 1)
            return;

        left = Math.Min(left, right);
        top = Math.Min(top, bottom);

        // DataRect is relative to the plot render surface; offset into TileCanvas.
        if (img.Parent is UIElement canvas)
        {
            try
            {
                var o = plot.TranslatePoint(new Point(0, 0), canvas);
                left += o.X;
                top += o.Y;
            }
            catch
            {
                // keep untranslated
            }
        }

        Canvas.SetLeft(img, left);
        Canvas.SetTop(img, top);
        img.Width = w;
        img.Height = h;
    }

    private async Task LoadTileAsync(
        int gen,
        string key,
        int z, int x, int y,
        ScottPlot.WPF.WpfPlot plot,
        TrackProjection projection,
        CancellationToken ct)
    {
        try
        {
            var bytes = await _cache.GetOrDownloadAsync(_sourceId, _urlTemplate, z, x, y, ct)
                .ConfigureAwait(false);
            if (bytes is null || ct.IsCancellationRequested || gen != _generation)
                return;

            await _canvas.Dispatcher.InvokeAsync(() =>
            {
                if (gen != _generation || !_images.TryGetValue(key, out var img))
                    return;
                try
                {
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    img.Source = bmp;
                    PlaceImage(img, plot, projection, z, x, y);
                }
                catch
                {
                    // ignore decode errors
                }
            });
        }
        catch
        {
            // ignore
        }
    }

    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch
        {
            // ignore
        }

        _cts = null;
    }

    public void Dispose()
    {
        Cancel();
        _cache.Dispose();
        _canvas.Children.Clear();
        _images.Clear();
    }
}
