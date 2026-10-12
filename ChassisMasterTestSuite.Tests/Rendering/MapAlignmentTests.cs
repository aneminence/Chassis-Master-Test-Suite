using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chassis_Master_Test_Suite.Controls;
using Chassis_Master_Test_Suite.Map;
using Xunit;

namespace ChassisMasterTestSuite.RenderingTests;

public class MapAlignmentTests
{
    public static IEnumerable<object[]> RenderCases()
    {
        foreach (var dpi in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        foreach (var scale in new[] { 1.0, 1.75 })
        foreach (var size in new[] { (800, 600), (1001, 701), (1200, 400) })
            yield return new object[] { dpi, scale, size.Item1, size.Item2 };
    }

    [Theory]
    [MemberData(nameof(RenderCases))]
    public void TileInteriorMatchesActualScottPlotPixels(double dpi, double scale, int width, int height) => OnSta(() =>
    {
        using var scene = new Scene(width, height, new Thickness(17, 23, 31, 19));
        scene.Plot.Plot.ScaleFactor = scale;
        scene.Plot.Plot.Axes.SquareUnits();
        // Exercise both frameless maps and a nonzero plot DataRect offset.
        foreach (var showAxes in new[] { false, true })
        {
            if (showAxes)
                scene.Plot.Plot.ShowAxesAndGrid();
            foreach (var span in new[] { 100.0, 2_000.0, 100_000.0 })
            {
                scene.Plot.Plot.Axes.SetLimits(-span * .4, span * .6, -span * .3, span * .7);
                scene.Render(dpi);
                var viewport = MapViewport.Capture(scene.Plot, scene.Tiles, scene.Projection, scene.Plot.Plot.LastRender)!;
                Assert.NotNull(viewport);
                // These positions sample the center and four quadrants, not just
                // tile corners (which failed to detect the original overlay bug).
                foreach (var (fx, fy) in new[] { (.1, .2), (.2, .8), (.5, .5), (.8, .2), (.9, .8) })
                {
                    var x = viewport.Limits.Left + fx * (viewport.Limits.Right - viewport.Limits.Left);
                    var y = viewport.Limits.Bottom + fy * (viewport.Limits.Top - viewport.Limits.Bottom);
                    var (lat, lon) = scene.Projection.ToLatLon(x, y);
                    var track = scene.TrackDip(lat, lon);
                    foreach (var zoom in new[] { 5, 12, 15, 19 })
                    {
                        // Independent XYZ fractional pixel formula, then interpolate
                        // inside the production tile rectangle on the WPF canvas.
                        var n = 1 << zoom;
                        var globalX = (lon + 180) / 360 * n;
                        var globalY = (1 - Math.Asinh(Math.Tan(lat * Math.PI / 180)) / Math.PI) / 2 * n;
                        var tx = (int)Math.Floor(globalX);
                        var ty = (int)Math.Floor(globalY);
                        var rect = viewport.TileRectangle(zoom, tx, ty);
                        var tilePoint = new Point(rect.Left + (globalX - tx) * rect.Width,
                            rect.Top + (globalY - ty) * rect.Height);
                        AssertNear(track, tilePoint);
                    }
                }
            }
        }
    });

    [Fact]
    public void OldUnscaledDataRectFormulaReproducesHighDpiOffset() => OnSta(() =>
    {
        using var scene = new Scene();
        scene.Render(1.5);
        var data = scene.Plot.Plot.LastRender.DataRect;
        var track = scene.TrackDip(31.2, 121.5);
        var oldPosition = new Point(data.Left + data.Width / 2, data.Top + data.Height / 2);
        AssertNear(new Point(200, 150), new Point(oldPosition.X - track.X, oldPosition.Y - track.Y));
        var viewport = MapViewport.Capture(scene.Plot, scene.Tiles, scene.Projection, scene.Plot.Plot.LastRender)!;
        AssertNear(track, viewport.ToCanvas(0, 0));
    });

    [Fact]
    public void TileMovesWithCompletedFrameBeforeDownloadDebounce() => OnSta(() =>
    {
        using var scene = new Scene();
        scene.Render(1.5);
        scene.Layer.Enabled = true;
        var image = scene.Tiles.Children.OfType<Image>().First();
        var before = ImageRect(image);

        // Pending limits must NOT move the basemap ahead of the visible track.
        scene.Plot.Plot.Axes.SetLimits(-600, 1400, -450, 1050);
        scene.Layer.Invalidate(force: true);
        Assert.Equal(before, ImageRect(image));

        scene.Render(1.5);
        Assert.Contains(image, scene.Tiles.Children.OfType<Image>());
        var after = ImageRect(image);
        // No dispatcher pump / no 120 ms wait occurred between these assertions.
        AssertNear(new Point(-160, 120), new Point(after.Left - before.Left, after.Top - before.Top));
        Assert.Equal(before.Width, after.Width, 6);
        Assert.Equal(before.Height, after.Height, 6);

        scene.Plot.Plot.Axes.SetLimits(-3000, 3000, -2250, 2250);
        scene.Render(1.5);
        Assert.Equal(before.Width / 3, image.Width, 6);
        Assert.Equal(before.Height / 3, image.Height, 6);
    });

    [Fact]
    public void ResizeDpiAndPlotScaleChangesUpdateExistingTilesImmediately() => OnSta(() =>
    {
        using var scene = new Scene();
        scene.Render(1);
        scene.Layer.Enabled = true;
        var image = scene.Tiles.Children.OfType<Image>().First();
        var before = ImageRect(image);
        scene.Arrange(1200, 900);
        scene.Plot.Plot.ScaleFactor = 1.75;
        scene.Render(2);
        var after = ImageRect(image);
        Assert.Equal(before.Width * 1.5, after.Width, 3);
        Assert.Equal(before.Height * 1.5, after.Height, 3);
        Assert.Equal(before.Left * 1.5, after.Left, 3);
        Assert.Equal(before.Top * 1.5, after.Top, 3);
        var clip = Assert.IsType<RectangleGeometry>(scene.Tiles.Clip);
        AssertNear(new Point(1200, 900), clip.Rect.BottomRight);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedTileCompletionUsesLatestCompletedFrame(bool renderPendingPan) => OnSta(() =>
    {
        using var scene = new Scene();
        scene.Render(1.5);
        scene.SeedCenterTile();
        scene.Layer.Enabled = true;
        Assert.DoesNotContain(scene.Tiles.Children.OfType<Image>(), x => x.Source is not null);
        scene.Plot.Plot.Axes.SetLimits(-600, 1400, -450, 1050);
        if (renderPendingPan)
            scene.Render(1.5);
        PumpUntil(() => scene.Tiles.Children.OfType<Image>().Any(x => x.Source is not null));
        var image = Assert.Single(scene.Tiles.Children.OfType<Image>(), x => x.Source is not null);
        var zoom = scene.CenterTileZoom;
        var (x, y) = MapTileMath.LatLonToTile(31.2, 121.5, zoom);
        var viewport = MapViewport.Capture(scene.Plot, scene.Tiles, scene.Projection, scene.Plot.Plot.LastRender)!;
        Assert.Equal(viewport.TileRectangle(zoom, x, y), ImageRect(image));
    });

    [Fact]
    public void ContinuousRenderingDoesNotStarveTileRequests() => OnSta(() =>
    {
        using var scene = new Scene();
        scene.Layer.Enabled = true;
        scene.Render(1.5); // schedules the first set without a forced load
        Assert.Empty(scene.Tiles.Children);
        PumpUntil(() => scene.Tiles.Children.Count > 0, () => scene.Render(1.5));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledOrDisposedLayerIgnoresQueuedTileCompletion(bool dispose) => OnSta(() =>
    {
        using var scene = new Scene();
        scene.Render(1.5);
        scene.SeedCenterTile();
        scene.Layer.Enabled = true;
        if (dispose)
            scene.Layer.Dispose();
        else
            scene.Layer.Enabled = false;
        var drained = false;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => drained = true);
        PumpUntil(() => drained);
        Assert.Empty(scene.Tiles.Children);
    });

    [Fact]
    public void SwitchingSourcesDoesNotReusePreviousImages() => OnSta(() =>
    {
        using var scene = new Scene();
        scene.Render(1.5);
        scene.SeedCenterTile();
        scene.Layer.Enabled = true;
        var oldImages = scene.Tiles.Children.OfType<Image>().ToArray();
        scene.Layer.SetSource(Scene.OfflineSource("second-source"));
        var drained = false;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => drained = true);
        PumpUntil(() => drained);
        Assert.NotEmpty(scene.Tiles.Children);
        Assert.DoesNotContain(scene.Tiles.Children.OfType<Image>(), oldImages.Contains);
        Assert.DoesNotContain(scene.Tiles.Children.OfType<Image>(), image => image.Source is not null);
    });

    private static Rect ImageRect(Image image) =>
        new(Canvas.GetLeft(image), Canvas.GetTop(image), image.Width, image.Height);

    private static void AssertNear(Point expected, Point actual)
    {
        Assert.InRange(actual.X, expected.X - .01, expected.X + .01);
        Assert.InRange(actual.Y, expected.Y - .01, expected.Y + .01);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF test timed out");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void PumpUntil(Func<bool> condition, Action? tick = null)
    {
        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            tick?.Invoke();
            if (condition() || watch.Elapsed > TimeSpan.FromSeconds(3))
                frame.Continue = false;
        };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(condition(), "Expected dispatcher work did not complete");
    }

    private sealed class Scene : IDisposable
    {
        // Keep offline fixtures below the tile-count fallback threshold, so the
        // seeded image is at the provider's actual selected zoom.
        private const int SourceMaxZoom = 15;
        public readonly ScottPlot.WPF.WpfPlot Plot = new();
        public readonly Canvas Tiles = new();
        public readonly TrackProjection Projection = new(31.2, 121.5);
        public readonly MapTileLayerController Layer;
        public readonly Grid Host = new();
        private readonly string _cacheRoot = Path.Combine(AppContext.BaseDirectory, "tile-test-cache", Guid.NewGuid().ToString("N"));
        private readonly MapTileCache _cache;
        private int _pixelWidth;
        private int _pixelHeight;
        public int CenterTileZoom { get; private set; }

        public Scene(int width = 800, int height = 600, Thickness margin = default)
        {
            Host.UseLayoutRounding = true; // same as MainWindow
            Plot.Margin = margin;
            Host.Children.Add(Tiles);
            Host.Children.Add(Plot);
            Arrange(width, height);
            Plot.Plot.HideAxesAndGrid();
            Plot.Plot.Axes.Frameless();
            Plot.Plot.Axes.SetLimits(-1000, 1000, -750, 750);
            _cache = new MapTileCache(_cacheRoot);
            Layer = new MapTileLayerController(Tiles, () => Plot, () => Projection, _cache);
            Layer.SetSource(OfflineSource("test-source"));
            Plot.Plot.RenderManager.RenderFinished += (_, render) => Layer.OnRenderFinished(render);
        }

        public static MapTileSource OfflineSource(string id) => new()
        {
            Id = id, DisplayNameEn = id, DisplayNameZh = id, UrlTemplate = "", MaxZoom = SourceMaxZoom
        };

        public void Arrange(int width, int height)
        {
            Host.Measure(new Size(width, height));
            Host.Arrange(new Rect(0, 0, width, height));
        }

        private FrameworkElement Surface => (FrameworkElement)Plot.Template.FindName("PART_SKElement", Plot);

        public void Render(double dpi)
        {
            _pixelWidth = (int)Math.Round(Surface.ActualWidth * dpi);
            _pixelHeight = (int)Math.Round(Surface.ActualHeight * dpi);
            Plot.Plot.RenderInMemory(_pixelWidth, _pixelHeight);
        }

        public Point TrackDip(double latitude, double longitude)
        {
            var (x, y) = Projection.ToMeters(latitude, longitude);
            var pixel = Plot.Plot.GetPixel(new ScottPlot.Coordinates(x, y));
            return Surface.TranslatePoint(new Point(pixel.X * Surface.ActualWidth / _pixelWidth,
                pixel.Y * Surface.ActualHeight / _pixelHeight), Tiles);
        }

        public void SeedCenterTile()
        {
            var render = Plot.Plot.LastRender;
            CenterTileZoom = Math.Min(SourceMaxZoom, MapTileMath.SuggestZoom(
                (render.AxisLimits.Right - render.AxisLimits.Left) / (render.DataRect.Width * Plot.Plot.ScaleFactor), 31.2));
            var (x, y) = MapTileMath.LatLonToTile(31.2, 121.5, CenterTileZoom);
            var path = _cache.DiskPath("test-source", CenterTileZoom, x, y);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var pixels = Enumerable.Repeat((byte)255, 16 * 16 * 4).ToArray();
            var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        public void Dispose()
        {
            Layer.Dispose();
            Plot.Plot.Dispose();
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }
}
