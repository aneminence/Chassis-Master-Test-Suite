using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Localization;
using Chassis_Master_Test_Suite.Map;
using Chassis_Master_Test_Suite.Themes;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// 车辆轨迹地图面板。
///
/// 数据层用真实经纬度（内部换算成以轨迹中心为原点的"米"平面），
/// 显示层是 ScottPlot 画的轨迹 + 一层屏幕层的比例尺网格。
///
/// 网格行为（用户明确要求）：
///     类似底图的固定比例尺。
///     拖动平移时完全不动（方便来回拖动核对尺寸），
///     只有滚轮缩放时间距才跟着变。
///
/// 所以网格画在 GridOverlay 这个 Canvas 上，
/// 它的位置只由"当前每像素多少米"决定，跟轨迹的世界坐标无关。
///
/// 坐标系约定：X 正东、Y 正北，单位米。
/// 横向和纵向锁定等比例（plot.Axes.SquareUnits()），
/// 否则环形轨迹会被拉成椭圆。
/// </summary>
public partial class TrackMapPanel : UserControl
{
    /// <summary>
    /// 轨迹最多画这么多个点，超了按步长抽稀。
    /// 步长抽稀对轨迹形状影响很小（只是圆会略微多边形化），
    /// 但能把实时刷新的开销压住。
    /// </summary>
    private const int MaxDisplayPoints = 50_000;

    /// <summary>
    /// 网格线大致每隔这么多像素一条。
    /// 实际间距会被 NiceDistanceStep 收敛成 1/2/5/10... 的整齐米数。
    /// </summary>
    private const double GridTargetPixels = 20.0;

    /// <summary>
    /// 实时刷新限流。MainWindow 的 UI 定时器是 10 Hz，
    /// 轨迹不需要跟着刷那么快。
    /// </summary>
    private const int MinRefreshIntervalMilliseconds = 250;

    /// <summary>
    /// 鼠标离轨迹这么多个像素以内才算"悬停到轨迹上"。
    /// </summary>
    private const double HoverPickPixels = 14.0;

    public readonly record struct TrackLayer(
        string Name,
        string ColorHex,
        IReadOnlyList<VehicleSample> Samples);

    private readonly ScottPlot.WPF.WpfPlot _wpfPlot = new();
    private readonly List<TrackLayer> _tracks = new();
    /// <summary>所有轨迹样本展平，供点选/悬停；与 _sampleTrackIndex 对齐。</summary>
    private readonly List<VehicleSample> _samples = new();
    private readonly List<int> _sampleTrackIndex = new();
    private readonly Stopwatch _refreshStopwatch = Stopwatch.StartNew();

    /// <summary>
    /// 当前轨迹的投影（原点 = 轨迹外接矩形中心）。
    /// 只在换数据源时重建，实时采集期间保持不动，
    /// 否则轨迹会随着新数据加入而整体漂移。
    /// </summary>
    private TrackProjection? _projection;

    /// <summary>
    /// 用来判断"是不是换了一批数据"。
    /// 用第一条样本的经纬度做锚点：实时采集时它永远不变，
    /// 打开另一个 VBO 时它必然变化。
    /// </summary>
    private (double Latitude, double Longitude)? _projectionAnchor;

    private ScottPlot.Plottables.Scatter? _track;

    /// <summary>曲线光标对应的车辆位置标记。</summary>
    private ScottPlot.Plottables.Marker? _cursorMarker;

    /// <summary>当前光标样本；null 表示跟最新点。</summary>
    private VehicleSample? _cursorSample;

    private IReadOnlyList<AnnotatedTestRun> _runHighlights = Array.Empty<AnnotatedTestRun>();
    // (samples live on each AnnotatedTestRun)
    private int? _selectedRunNumber;
    private readonly List<ScottPlot.Plottables.Scatter> _runHighlightPlots = new();

    private bool _hasData;

    /// <summary>
    /// 用户是否已手动平移/缩放。为 true 后重建轨迹不再 AutoFit，
    /// 并在 Plot.Clear() 后恢复原先视野（否则 ScottPlot 会对 unset 轴自动缩放到数据）。
    /// 换数据源或 Clear 时复位。
    /// </summary>
    private bool _userHasAdjustedView;

    /// <summary>
    /// 本次左键按下是点选轨迹（不是拖动画布）。为 true 时忽略左键拖动锁定。
    /// </summary>
    private bool _leftDownWasTrackPick;

    private enum GatePlaceMode { None, Add }
    private GatePlaceMode _gatePlaceMode;
    private readonly List<ScottPlot.Plottables.Scatter> _gatePlots = new();
    private bool _suppressGateComboChanged;

    // 网格重绘缓存：间距和尺寸都没变就不用重画。
    private double _drawnSpacing;
    private double _drawnWidth;
    private double _drawnHeight;
    private bool _updatingGrid;

    private MapTileLayerController? _tileLayer;
    private bool _suppressBasemapSourceChanged;

    public TrackMapPanel()
    {
        InitializeComponent();

        BuildPlot();

        GateStore.Instance.Changed += (_, _) =>
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() =>
                {
                    RefreshGateCombo();
                    RedrawGates();
                });
                return;
            }
            RefreshGateCombo();
            RedrawGates();
        };

        PlotHost.Children.Add(_wpfPlot);
        Loaded += (_, _) =>
        {
            RefreshGateCombo();
            InitBasemapUi();
            ApplyLocalizedChrome();
        };

        GridOverlay.SizeChanged += (_, _) =>
        {
            UpdateGridOverlay();
            _tileLayer?.Invalidate();
        };
        TileCanvas.SizeChanged += (_, _) => _tileLayer?.Invalidate(force: true);

        _tileLayer = new MapTileLayerController(
            TileCanvas,
            () => _wpfPlot,
            () => _projection);

        AppearanceService.PreferencesChanged += (_, _) =>
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ApplyBasemapFromPreferences);
                return;
            }
            ApplyBasemapFromPreferences();
        };
        Loc.LanguageChanged += (_, _) =>
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ApplyLocalizedChrome);
                return;
            }
            ApplyLocalizedChrome();
        };
    }

    /// <summary>
    /// 当前载入的样本条数。
    /// </summary>
    public int SampleCount => _samples.Count;

    /// <summary>
    /// 用户在轨迹上点选样本时触发（供曲线光标 / Dashboard 同步）。
    /// </summary>
    public event Action<VehicleSample, int>? SampleSelected;

    // ============================================================
    // 初始化
    // ============================================================

    private void BuildPlot()
    {
        _wpfPlot.HorizontalAlignment = HorizontalAlignment.Stretch;
        _wpfPlot.VerticalAlignment = VerticalAlignment.Stretch;
        _wpfPlot.Margin = new Thickness(0);

        var plot = _wpfPlot.Plot;

        // 轴刻度全部不要 —— 距离刻度的活由网格层干。
        plot.HideAxesAndGrid();
        plot.Axes.Frameless(true);

        // 横向纵向锁定等比例，否则轨迹形状会被拉伸。
        plot.Axes.SquareUnits();

        ApplyDarkStyle();

        // 背景透明，让下方 GridOverlay 网格透出来（网格在轨迹之下）。
        _wpfPlot.Background = Brushes.Transparent;

        // Clear() 后轴会变成 unset，下一帧 AutoscaleUnsetAxesToData 会把视野弹回；
        // 关掉持续自动缩放，视野只由我们显式 SetLimits / AutoFit 控制。
        plot.Axes.ContinuouslyAutoscale = false;

        // 保留 ScottPlot 默认交互（左键拖动平移、滚轮缩放、右键拖动缩放）。
        _wpfPlot.UserInputProcessor.Reset();

        // 显式确保左键拖动 = 平移（点在轨迹上时由我们接管，见 PreviewMouseLeftButtonDown）。
        _wpfPlot.UserInputProcessor.LeftClickDragPan(
            enable: true,
            horizontal: true,
            vertical: true);

        // 关掉中键单击自动缩放（默认会把图缩回全图，和用户手动视野冲突）。
        _wpfPlot.UserInputProcessor.RemoveAll<
            ScottPlot.Interactivity.UserActionResponses.SingleClickAutoscale>();

        plot.RenderManager.RenderFinished += (_, _) =>
        {
            UpdateGridOverlay();
            _tileLayer?.Invalidate();
        };

        _wpfPlot.PreviewMouseMove += WpfPlot_PreviewMouseMove;
        _wpfPlot.PreviewMouseLeftButtonDown += WpfPlot_PreviewMouseLeftButtonDown;
        _wpfPlot.PreviewMouseDown += WpfPlot_PreviewMouseDown;
        _wpfPlot.PreviewMouseWheel += WpfPlot_PreviewMouseWheel;
        _wpfPlot.MouseLeave += WpfPlot_MouseLeave;
    }

    /// <summary>
    /// 深色主题。
    /// 注意：Plot.Clear() 会把样式复位，所以每次清空后都要重新套一遍。
    /// </summary>
    private void ApplyDarkStyle()
    {
        // Transparent plot so GridOverlay shows through; legend must stay dark (not default white).
        var transparent = ScottPlot.Colors.Transparent;
        _wpfPlot.Plot.SetStyle(
            new ScottPlot.PlotStyle
            {
                FigureBackgroundColor = transparent,
                DataBackgroundColor = transparent,
                LegendBackgroundColor = ScottPlot.Color.FromHex("#12171E").WithAlpha(0.92),
                LegendFontColor = ScottPlot.Color.FromHex("#C3CBD8"),
                LegendOutlineColor = ScottPlot.Color.FromHex("#1E2530"),
                Palette = new ScottPlot.Palettes.Dark()
            });

        ApplyDarkLegendPlacement();
    }

    /// <summary>
    /// Top-right legend so it does not cover Lat/Lon/Alt readout (bottom-right).
    /// </summary>
    private void ApplyDarkLegendPlacement()
    {
        var legend = _wpfPlot.Plot.Legend;
        legend.Alignment = ScottPlot.Alignment.UpperRight;
        legend.FontColor = ScottPlot.Color.FromHex("#C3CBD8");
        legend.FontSize = 11;
        // Nudge away from edges; keep clear of bottom coordinate stack.
        legend.Margin = new ScottPlot.PixelPadding(10, 10, 10, 10);
    }

    private void UpdateLegendVisibility(bool show)
    {
        if (show)
            _wpfPlot.Plot.ShowLegend(ScottPlot.Alignment.UpperRight);
        else
            _wpfPlot.Plot.Legend.IsVisible = false;

        ApplyDarkLegendPlacement();
    }

    // ============================================================
    // 对外接口
    // ============================================================

    /// <summary>
    /// 用一批样本刷新轨迹。
    ///
    /// 实时采集时 MainWindow 会以 10 Hz 调用，
    /// 这里按 250 ms 限流，避免每帧重画整条轨迹。
    /// 打开文件/数据量突跳时立刻刷新。
    /// </summary>
    public void SetTrack(IReadOnlyList<VehicleSample> samples)
    {
        if (samples.Count == 0)
        {
            Clear();
            return;
        }

        SetTracks(new[]
        {
            new TrackLayer("Track", "#E05252", samples)
        });
    }

    /// <summary>
    /// 多车/多文件轨迹同步显示。
    /// </summary>
    public void SetTracks(IReadOnlyList<TrackLayer> tracks)
    {
        var valid = tracks
            .Where(t => t.Samples is { Count: > 0 })
            .ToList();

        if (valid.Count == 0)
        {
            Clear();
            return;
        }

        var total = valid.Sum(t => t.Samples.Count);
        var countDelta = total - _samples.Count;

        var isBigChange =
            countDelta < 0
            || countDelta > 500
            || _projectionAnchor is null
            || valid.Count != _tracks.Count;

        if (!isBigChange
            && _refreshStopwatch.ElapsedMilliseconds
                < MinRefreshIntervalMilliseconds)
        {
            return;
        }

        _refreshStopwatch.Restart();
        Rebuild(valid);
    }

    /// <summary>
    /// 清空轨迹。
    /// </summary>
    public void Clear()
    {
        ClearRunHighlights();
        _tracks.Clear();
        _samples.Clear();
        _sampleTrackIndex.Clear();
        _projection = null;
        _projectionAnchor = null;
        _track = null;
        _cursorMarker = null;
        _cursorSample = null;
        _hasData = false;
        _userHasAdjustedView = false;

        _wpfPlot.Plot.Clear();

        ApplyDarkStyle();

        GridOverlay.Children.Clear();
        ResetGridCache();

        HoverTip.Visibility = Visibility.Collapsed;
        EmptyHint.Visibility = Visibility.Visible;

        LatText.Text = "--";
        LonText.Text = "--";
        AltText.Text = "--";

        _wpfPlot.Refresh();
    }

    /// <summary>
    /// 把轨迹缩放到刚好铺满视图。
    /// </summary>
    public void AutoFit()
    {
        if (_projection is null || _samples.Count == 0)
        {
            return;
        }

        // 显式复位视野时重新允许后续自动取景
        _userHasAdjustedView = false;

        var gps = TrackProjection.FilterGpsOutliers(_samples);
        if (gps.Count < 2)
            gps = _samples.Where(TrackProjection.IsValidGps).ToList();

        AutoFitToSamples(gps);
    }

    private void AutoFitToSamples(IReadOnlyList<VehicleSample> samples)
    {
        if (_projection is null || samples.Count == 0)
            return;

        var (minX, maxX, minY, maxY) = GetBounds(samples, _projection);

        // 全是同一点时给一个最小视野
        if (maxX - minX < 1e-6)
        {
            minX -= 25;
            maxX += 25;
        }

        if (maxY - minY < 1e-6)
        {
            minY -= 25;
            maxY += 25;
        }

        var padX = Math.Max((maxX - minX) * 0.08, 5.0);
        var padY = Math.Max((maxY - minY) * 0.08, 5.0);

        _wpfPlot.Plot.Axes.SetLimits(
            minX - padX,
            maxX + padX,
            minY - padY,
            maxY + padY);

        _wpfPlot.Refresh();
    }

    /// <summary>
    /// 用曲线光标对应的样本更新车辆位置标记。
    /// sample 为 null 时清除标记，坐标读数回到轨迹最新点。
    /// </summary>
    public void SetCursorSample(VehicleSample? sample)
    {
        // 同一条样本就别重画，避免 UI 定时器 10 Hz 刷闪
        if (ReferenceEquals(_cursorSample, sample))
            return;

        _cursorSample = sample;
        UpdateCursorMarker(refresh: true);
        UpdateCoordinateReadout();
    }

    // ============================================================
    // 重建轨迹
    // ============================================================

    

    /// <summary>
    /// 标注试验结果轨迹段（样本下标相对 <paramref name="samples"/>）。
    /// selectedRunNumber 非空时该 run 加粗高亮，其余变淡。
    /// </summary>
    /// <summary>
    /// Annotate test-run path segments. selectedRunNumber emphasizes that run.
    /// </summary>
    public void SetRunHighlights(
        IReadOnlyList<AnnotatedTestRun> runs,
        int? selectedRunNumber = null)
    {
        _runHighlights = runs ?? Array.Empty<AnnotatedTestRun>();
        _selectedRunNumber = selectedRunNumber;
        RedrawRunHighlights();
        RedrawGates();
    }

    public void ClearRunHighlights()
    {
        _runHighlights = Array.Empty<AnnotatedTestRun>();
        _selectedRunNumber = null;
        ClearRunHighlightPlots();
        if (_hasData)
            _wpfPlot.Refresh();
    }

    private void Rebuild(IReadOnlyList<TrackLayer> tracks)
    {
        // 每条轨迹先滤掉无效 GPS / 远距毛刺，再投影与取景。
        // 否则单个坏点会把视野拉到十几公里，真轨迹缩成看不见的点，
        // 只剩一条对角“假线”。
        var filteredLayers = new List<(TrackLayer Layer, List<VehicleSample> Gps)>();
        foreach (var layer in tracks)
        {
            var gps = TrackProjection.FilterGpsOutliers(layer.Samples);
            if (gps.Count >= 2)
                filteredLayers.Add((layer, gps));
        }

        if (filteredLayers.Count == 0)
        {
            // 有样本但都无有效 GPS：清空地图，避免画对角假线
            Clear();
            EmptyHint.Visibility = Visibility.Visible;
            return;
        }

        // 多文件：丢掉中心点离“场地中位数”过远的整条轨迹（坏文件 / 错场地）
        filteredLayers = KeepLayersNearVenue(filteredLayers);

        if (filteredLayers.Count == 0)
        {
            Clear();
            EmptyHint.Visibility = Visibility.Visible;
            return;
        }

        var allGps = filteredLayers.SelectMany(t => t.Gps).ToList();
        var first = allGps[0];

        var needNewProjection =
            _projection is null
            || _projectionAnchor is null
            || Math.Abs(_projectionAnchor.Value.Latitude - first.Latitude) > 1e-9
            || Math.Abs(_projectionAnchor.Value.Longitude - first.Longitude) > 1e-9
            || tracks.Count != _tracks.Count;

        var hadData = _hasData;
        var savedLimits = hadData
            ? _wpfPlot.Plot.Axes.GetLimits()
            : default;

        _tracks.Clear();
        _tracks.AddRange(tracks);

        // 点选/悬停仍用原始样本顺序（与文件对齐），投影时再校验 GPS
        _samples.Clear();
        _sampleTrackIndex.Clear();
        for (var ti = 0; ti < tracks.Count; ti++)
        {
            foreach (var sample in tracks[ti].Samples)
            {
                _samples.Add(sample);
                _sampleTrackIndex.Add(ti);
            }
        }

        if (needNewProjection)
        {
            _projection = TrackProjection.FitToTrack(allGps);
            _projectionAnchor = (first.Latitude, first.Longitude);
            _userHasAdjustedView = false;
        }

        var projection = _projection!;

        _wpfPlot.Plot.Clear();
        ApplyDarkStyle();
        // Clear 后部分轴设置会丢，必须重套，否则多轨时比例/取景异常
        _wpfPlot.Plot.HideAxesAndGrid();
        _wpfPlot.Plot.Axes.Frameless(true);
        _wpfPlot.Plot.Axes.SquareUnits();
        _wpfPlot.Plot.Axes.ContinuouslyAutoscale = false;

        _track = null;
        foreach (var (layer, gps) in filteredLayers)
        {
            var stride = Math.Max(1, gps.Count / MaxDisplayPoints);
            var xs = new List<double>(gps.Count / stride + 1);
            var ys = new List<double>(gps.Count / stride + 1);

            for (var i = 0; i < gps.Count; i += stride)
            {
                var (x, y) = projection.ToMeters(
                    gps[i].Latitude,
                    gps[i].Longitude);
                xs.Add(x);
                ys.Add(y);
            }

            if (gps.Count > 0 && (gps.Count - 1) % stride != 0)
            {
                var (x, y) = projection.ToMeters(
                    gps[^1].Latitude,
                    gps[^1].Longitude);
                xs.Add(x);
                ys.Add(y);
            }

            if (xs.Count < 2)
                continue;

            var colorHex = string.IsNullOrWhiteSpace(layer.ColorHex)
                ? "#E05252"
                : layer.ColorHex;

            // 用 double[] 拷贝：ScottPlot 对 List 只持引用，避免后续被误改
            // Base track: thin + muted so run overlays read clearly (VBTS-style).
            var scatter = _wpfPlot.Plot.Add.ScatterLine(
                xs.ToArray(),
                ys.ToArray(),
                ScottPlot.Color.FromHex(colorHex).WithAlpha(0.85));

            scatter.LineWidth = 2.4f;
            scatter.MarkerStyle.IsVisible = false;
            scatter.LegendText = layer.Name;
            _track ??= scatter;
        }

        UpdateLegendVisibility(filteredLayers.Count > 1 || _runHighlights.Count > 0);

        _hasData = true;
        EmptyHint.Visibility = Visibility.Collapsed;
        UpdateCoordinateReadout();

        _cursorMarker = null;
        UpdateCursorMarker(refresh: false);

        if (!_userHasAdjustedView)
        {
            AutoFitToSamples(allGps);
        }
        else
        {
            _wpfPlot.Plot.Axes.SetLimits(savedLimits);
            _wpfPlot.Refresh();
        }

        RedrawRunHighlights();
        RedrawGates();
    }


    /// <summary>
    /// 标记用户已手动改过视野，后续实时刷新不再 AutoFit。
    /// </summary>
    private void MarkUserAdjustedView()
    {
        if (!_hasData)
        {
            return;
        }

        _userHasAdjustedView = true;
    }

    private void WpfPlot_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 左键可能是点选轨迹（见 PreviewMouseLeftButtonDown），不在这里锁定；
        // 中键平移、右键缩放才算手动改视野。
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            MarkUserAdjustedView();
        }
    }

    private void WpfPlot_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        MarkUserAdjustedView();
    }

    /// <summary>
    /// 保留中心靠近场地中位数的轨迹；中心偏离 > 8 km 的整条丢掉（不参与取景）。
    /// 仍会尝试画所有滤后轨迹中“在场地内”的那些。
    /// </summary>
    private static List<(TrackLayer Layer, List<VehicleSample> Gps)> KeepLayersNearVenue(
        List<(TrackLayer Layer, List<VehicleSample> Gps)> layers)
    {
        if (layers.Count <= 1)
            return layers;

        var centers = layers.Select(l =>
        {
            var lat = l.Gps.Average(s => s.Latitude);
            var lon = l.Gps.Average(s => s.Longitude);
            return (l, lat, lon);
        }).ToList();

        var medLat = centers.Select(c => c.lat).OrderBy(v => v).ElementAt(centers.Count / 2);
        var medLon = centers.Select(c => c.lon).OrderBy(v => v).ElementAt(centers.Count / 2);
        var mPerLon = TrackProjection.MetersPerDegreeLatitude *
                      Math.Cos(medLat * Math.PI / 180.0);
        const double maxMeters = 8_000.0;
        var maxSq = maxMeters * maxMeters;

        var kept = centers.Where(c =>
        {
            var dx = (c.lon - medLon) * mPerLon;
            var dy = (c.lat - medLat) * TrackProjection.MetersPerDegreeLatitude;
            return dx * dx + dy * dy <= maxSq;
        }).Select(c => c.l).ToList();

        return kept.Count > 0 ? kept : layers;
    }

        private static (double MinX, double MaxX, double MinY, double MaxY) GetBounds(
        IReadOnlyList<VehicleSample> samples,
        TrackProjection projection)
    {
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;
        var any = false;

        foreach (var sample in samples)
        {
            if (!TrackProjection.IsValidGps(sample))
                continue;

            any = true;
            var (x, y) = projection.ToMeters(
                sample.Latitude,
                sample.Longitude);

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        if (!any)
            return (0, 0, 0, 0);

        return (minX, maxX, minY, maxY);
    }

    private void UpdateCoordinateReadout()
    {
        var sample = _cursorSample;

        if (sample is null)
        {
            if (_samples.Count == 0)
            {
                LatText.Text = "--";
                LonText.Text = "--";
                AltText.Text = "--";
                return;
            }

            sample = _samples[^1];
        }

        LatText.Text = FormatLatitude(sample.Latitude);
        LonText.Text = FormatLongitude(sample.Longitude);
        AltText.Text = $"{sample.Altitude:0.0} m";
    }


    /// <summary>
    /// 在轨迹上画金色车辆标记；Rebuild 之后也要再调用（Clear 会抹掉）。
    /// </summary>
    private void UpdateCursorMarker(bool refresh)
    {
        if (_cursorMarker is not null)
        {
            _wpfPlot.Plot.Remove(_cursorMarker);
            _cursorMarker = null;
        }

        if (_cursorSample is null ||
            _projection is null ||
            !_hasData ||
            !TrackProjection.IsValidGps(_cursorSample))
        {
            if (refresh)
                _wpfPlot.Refresh();
            return;
        }

        var (x, y) = _projection.ToMeters(
            _cursorSample.Latitude,
            _cursorSample.Longitude);

        _cursorMarker = _wpfPlot.Plot.Add.Marker(x, y);
        _cursorMarker.Size = 16;
        _cursorMarker.Shape =
            ScottPlot.MarkerShape.FilledCircle;
        _cursorMarker.Color =
            ScottPlot.Color.FromHex("#C8A34A");
        _cursorMarker.MarkerLineColor =
            ScottPlot.Color.FromHex("#F5E6B8");
        _cursorMarker.MarkerLineWidth = 1.5f;

        if (refresh)
            _wpfPlot.Refresh();
    }

    private static string FormatLatitude(double value)
        => $"{Math.Abs(value):0.000000}°{(value >= 0 ? "N" : "S")}";

    private static string FormatLongitude(double value)
        => $"{Math.Abs(value):0.000000}°{(value >= 0 ? "E" : "W")}";

    // ============================================================
    // 比例尺网格（屏幕层）
    // ============================================================

    private void ResetGridCache()
    {
        _drawnSpacing = 0.0;
        _drawnWidth = 0.0;
        _drawnHeight = 0.0;
    }

    /// <summary>
    /// 重画比例尺网格。
    ///
    /// 网格锚定在屏幕上：
    ///     竖线从左边框起算、横线从下边框起算，
    ///     所以平移时它一动不动（这正是用户要的"核对尺寸"参照）。
    ///     只有缩放导致"每像素多少米"变化时，间距才跟着变。
    /// </summary>
    private void UpdateGridOverlay()
    {
        if (_updatingGrid)
        {
            return;
        }

        var width = GridOverlay.ActualWidth;
        var height = GridOverlay.ActualHeight;

        if (width < 1.0 || height < 1.0)
        {
            return;
        }

        if (!_hasData)
        {
            if (GridOverlay.Children.Count > 0)
            {
                GridOverlay.Children.Clear();
                ResetGridCache();
            }

            return;
        }

        var limits = _wpfPlot.Plot.Axes.GetLimits();

        var spanX = limits.Right - limits.Left;

        if (double.IsNaN(spanX) || spanX <= 0.0)
        {
            return;
        }

        // 横向纵向已用 SquareUnits() 锁成等比，取横向的即可。
        var metersPerPixel = spanX / width;

        if (double.IsNaN(metersPerPixel) || metersPerPixel <= 0.0)
        {
            return;
        }

        var step = TrackProjection.NiceDistanceStep(
            metersPerPixel * GridTargetPixels);

        var spacing = step / metersPerPixel;

        if (spacing < 6.0)
        {
            return;
        }

        // 间距和画布尺寸都没变就跳过，避免每次渲染都重建视觉树。
        if (Math.Abs(spacing - _drawnSpacing) < 0.5
            && Math.Abs(width - _drawnWidth) < 0.5
            && Math.Abs(height - _drawnHeight) < 0.5)
        {
            return;
        }

        _updatingGrid = true;

        try
        {
            GridOverlay.Children.Clear();

            var lineBrush = new SolidColorBrush(
                Color.FromRgb(0x1E, 0x28, 0x33));

            var edgeBrush = new SolidColorBrush(
                Color.FromRgb(0x2A, 0x36, 0x44));

            var labelBrush = new SolidColorBrush(
                Color.FromRgb(0x5A, 0x64, 0x74));

            // 竖线：从左边框起算，距离向右递增
            for (var i = 0; i * spacing <= width; i++)
            {
                var x = i * spacing;

                GridOverlay.Children.Add(
                    new Line
                    {
                        X1 = x,
                        Y1 = 0.0,
                        X2 = x,
                        Y2 = height,
                        Stroke = i == 0 ? edgeBrush : lineBrush,
                        StrokeThickness = 1.0
                    });

                if (i > 0)
                {
                    var label = new TextBlock
                    {
                        Text = TrackProjection.FormatDistance(i * step),
                        Foreground = labelBrush,
                        FontSize = 9,
                        FontFamily = new FontFamily("Consolas")
                    };

                    Canvas.SetLeft(label, x + 3.0);
                    Canvas.SetTop(label, height - 13.0);

                    GridOverlay.Children.Add(label);
                }
            }

            // 横线：从下边框起算，距离向上递增
            for (var j = 0; j * spacing <= height; j++)
            {
                var y = height - j * spacing;

                GridOverlay.Children.Add(
                    new Line
                    {
                        X1 = 0.0,
                        Y1 = y,
                        X2 = width,
                        Y2 = y,
                        Stroke = j == 0 ? edgeBrush : lineBrush,
                        StrokeThickness = 1.0
                    });

                if (j > 0)
                {
                    var label = new TextBlock
                    {
                        Text = TrackProjection.FormatDistance(j * step),
                        Foreground = labelBrush,
                        FontSize = 9,
                        FontFamily = new FontFamily("Consolas")
                    };

                    Canvas.SetLeft(label, 4.0);
                    Canvas.SetTop(label, y - 12.0);

                    GridOverlay.Children.Add(label);
                }
            }

            _drawnSpacing = spacing;
            _drawnWidth = width;
            _drawnHeight = height;
        }
        finally
        {
            _updatingGrid = false;
        }
    }

    // ============================================================
    // 悬停提示（暂时只显示速度）
    // ============================================================

    private void WpfPlot_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        // 左键拖动画布平移：按住左键移动且不是点选轨迹时，退出自动取景
        if (e.LeftButton == MouseButtonState.Pressed && !_leftDownWasTrackPick)
        {
            MarkUserAdjustedView();
        }

        if (!_hasData || _projection is null || _samples.Count == 0)
        {
            HoverTip.Visibility = Visibility.Collapsed;
            return;
        }

        var pixel = _wpfPlot.GetPlotPixelPosition(e);

        var coordinates = _wpfPlot.Plot.GetCoordinates(
            pixel.X,
            pixel.Y,
            _wpfPlot.Plot.Axes.Bottom,
            _wpfPlot.Plot.Axes.Left);

        var width = GridOverlay.ActualWidth;

        if (width < 1.0)
        {
            return;
        }

        var limits = _wpfPlot.Plot.Axes.GetLimits();

        var metersPerPixelX = (limits.Right - limits.Left) / width;

        var index = FindNearestSample(
            coordinates.X,
            coordinates.Y,
            metersPerPixelX * HoverPickPixels);

        if (index < 0)
        {
            HoverTip.Visibility = Visibility.Collapsed;
            return;
        }

        var sample = _samples[index];

        HoverTipText.Text = $"{sample.SpeedKph:0.0} km/h";

        HoverTip.Visibility = Visibility.Visible;

        var position = e.GetPosition(MapHost);

        var left = position.X + 14.0;
        var top = position.Y + 14.0;

        // 别让提示框跑出面板
        if (left + HoverTip.ActualWidth > MapHost.ActualWidth - 8.0)
        {
            left = position.X - HoverTip.ActualWidth - 14.0;
        }

        if (top + HoverTip.ActualHeight > MapHost.ActualHeight - 8.0)
        {
            top = position.Y - HoverTip.ActualHeight - 14.0;
        }

        // HoverTip 的父级是 Grid，Canvas.SetLeft 不生效，
        // 用 Margin 配 Left/Top 对齐来定位。
        HoverTip.Margin = new Thickness(
            Math.Max(0.0, left),
            Math.Max(0.0, top),
            0.0,
            0.0);
    }

    private void WpfPlot_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _leftDownWasTrackPick = false;

        if (TryPlaceGateAtMouse(e))
            return;

        if (TrySelectGateAtMouse(e))
            return;

        if (!_hasData || _projection is null || _samples.Count == 0)
            return;

        var pixel = _wpfPlot.GetPlotPixelPosition(e);
        var coordinates = _wpfPlot.Plot.GetCoordinates(
            pixel.X,
            pixel.Y,
            _wpfPlot.Plot.Axes.Bottom,
            _wpfPlot.Plot.Axes.Left);

        var width = GridOverlay.ActualWidth;
        if (width < 1.0)
            return;

        var limits = _wpfPlot.Plot.Axes.GetLimits();
        var metersPerPixelX = (limits.Right - limits.Left) / width;

        var index = FindNearestSample(
            coordinates.X,
            coordinates.Y,
            metersPerPixelX * HoverPickPixels);

        if (index < 0)
            return;

        _leftDownWasTrackPick = true;

        var sample = _samples[index];
        SetCursorSample(sample);
        var trackIndex = index >= 0 && index < _sampleTrackIndex.Count
            ? _sampleTrackIndex[index]
            : 0;
        SampleSelected?.Invoke(sample, trackIndex);

        // 点在轨迹上：选点并同步，不启动平移
        e.Handled = true;
    }

    private void WpfPlot_MouseLeave(object sender, MouseEventArgs e)
    {
        HoverTip.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 找离给定点最近的样本。
    /// 超过阈值就返回 -1（说明鼠标没靠近轨迹）。
    ///
    /// 线性扫描。样本量极大时按步长跳着扫，
    /// 精度略降但悬停本来就是粗定位。
    /// </summary>

    private static readonly string[] RunHighlightPalette =
    {
        "#3FBF6F", "#4A9FD8", "#C06AD8", "#E08A4A",
        "#5AC8C8", "#E05252", "#D8D84A", "#C8A34A"
    };

    private void ClearRunHighlightPlots()
    {
        foreach (var p in _runHighlightPlots)
            _wpfPlot.Plot.Remove(p);
        _runHighlightPlots.Clear();
    }

    private void RedrawRunHighlights()
    {
        ClearRunHighlightPlots();

        if (!_hasData || _projection is null || _runHighlights.Count == 0)
        {
            if (_hasData)
                _wpfPlot.Refresh();
            return;
        }

        var projection = _projection;
        foreach (var annotated in _runHighlights)
        {
            var run = annotated.Result;
            var samples = annotated.Samples;
            if (samples.Count == 0)
                continue;

            var start = Math.Clamp(run.StartSampleIndex, 0, samples.Count - 1);
            var end = Math.Clamp(run.EndSampleIndex, 0, samples.Count - 1);
            if (end < start)
                (start, end) = (end, start);

            var xs = new List<double>();
            var ys = new List<double>();
            for (var i = start; i <= end; i++)
            {
                var s = samples[i];
                if (!TrackProjection.IsValidGps(s))
                    continue;
                var (x, y) = projection.ToMeters(s.Latitude, s.Longitude);
                xs.Add(x);
                ys.Add(y);
            }

            if (xs.Count < 2)
                continue;

            // Colors must match chart HorizontalSpan (AnnotatedTestRun.ColorHex / shared palette).
            var colorHex = string.IsNullOrWhiteSpace(annotated.ColorHex)
                ? RunHighlightPalette[(run.RunNumber - 1) % RunHighlightPalette.Length]
                : annotated.ColorHex;
            var selected = _selectedRunNumber is int sel && sel == run.RunNumber;
            var dimOthers = _selectedRunNumber is not null && !selected;
            var xa = xs.ToArray();
            var ya = ys.ToArray();

            // Dark underlay for contrast against muted base path (VBTS: thick solid run overlay).
            var under = _wpfPlot.Plot.Add.ScatterLine(
                xa, ya, ScottPlot.Color.FromHex("#0A0E14").WithAlpha(dimOthers ? 0.45 : 0.85));
            under.LineWidth = selected ? 14.0f : (dimOthers ? 8.0f : 11.0f);
            under.MarkerStyle.IsVisible = false;
            under.LegendText = string.Empty;
            _runHighlightPlots.Add(under);

            var scatter = _wpfPlot.Plot.Add.ScatterLine(
                xa, ya,
                ScottPlot.Color.FromHex(colorHex).WithAlpha(dimOthers ? 0.70 : 1.0));
            // ~4–5× muted base (1.0): selected brightest/thickest.
            scatter.LineWidth = selected ? 10.0f : (dimOthers ? 6.0f : 8.0f);
            scatter.MarkerStyle.IsVisible = false;
            var src = string.IsNullOrWhiteSpace(annotated.SourceLabel) ? "" : annotated.SourceLabel + " · ";
            scatter.LegendText = $"{src}Run {run.RunNumber}";
            _runHighlightPlots.Add(scatter);
        }

        UpdateLegendVisibility(_tracks.Count > 1 || _runHighlights.Count > 0);
        _wpfPlot.Refresh();
    }



    
    private void AddGateButton_Click(object sender, RoutedEventArgs e)
    {
        _gatePlaceMode = GatePlaceMode.Add;
        if (EmptyHint is not null)
            EmptyHint.Visibility = Visibility.Collapsed;
        StatusGate("Click the track to place a new gate");
    }

    private void DeleteGateButton_Click(object sender, RoutedEventArgs e)
    {
        if (GateStore.Instance.SelectedId is not Guid id)
        {
            StatusGate("Select a gate first");
            return;
        }
        GateStore.Instance.Remove(id);
        StatusGate("Gate deleted");
    }

    private void DeleteAllGatesButton_Click(object sender, RoutedEventArgs e)
    {
        GateStore.Instance.Clear();
        _gatePlaceMode = GatePlaceMode.None;
        StatusGate("All gates deleted");
    }

    private void RenameGateButton_Click(object sender, RoutedEventArgs e)
    {
        var gate = GateStore.Instance.Selected;
        if (gate is null)
        {
            StatusGate("Select a gate first");
            return;
        }

        var name = PromptText("Rename Gate", "Name:", gate.Name);
        if (name is null)
            return;
        if (!GateStore.Instance.Rename(gate.Id, name))
            StatusGate("Rename failed");
        else
            StatusGate($"Renamed to {name}");
    }

    private void GateWidthBox_LostFocus(object sender, RoutedEventArgs e) => ApplyGateWidthFromBox();

    private void GateWidthBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyGateWidthFromBox();
            e.Handled = true;
        }
    }

    private void ApplyGateWidthFromBox()
    {
        if (GateStore.Instance.SelectedId is not Guid id)
            return;
        if (!double.TryParse(GateWidthBox.Text.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var w) &&
            !double.TryParse(GateWidthBox.Text.Trim(), out w))
        {
            StatusGate("Width must be a number (meters)");
            return;
        }
        if (!GateStore.Instance.SetWidth(id, w))
            StatusGate("Width must be >= 0.5 m");
        else
            StatusGate($"Width = {w:0.##} m");
    }

    private void GateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGateComboChanged)
            return;
        if (GateCombo.SelectedItem is GateComboItem item)
            GateStore.Instance.Select(item.Id);
        SyncGateWidthBox();
    }

    private void ImportGatesButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Gates",
            Filter = "VBOX Test Suite (*.vbts)|*.vbts|All files (*.*)|*.*",
            DefaultExt = ".vbts",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            var imported = VbtsGateImporter.ImportFile(dlg.FileName);
            if (imported.Count == 0)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "No gates found in this file.",
                    "Import Gates", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (GateStore.Instance.Gates.Count > 0)
            {
                var ask = MessageBox.Show(Window.GetWindow(this),
                    $"Import {imported.Count} gate(s) and replace the current {GateStore.Instance.Gates.Count} gate(s)?",
                    "Import Gates",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (ask != MessageBoxResult.OK)
                    return;
                GateStore.Instance.Clear();
            }

            foreach (var g in imported)
                GateStore.Instance.Add(g);

            SessionMemoryStore.LastImportedVbtsPath = dlg.FileName;

            StatusGate($"Imported {imported.Count} gate(s) from {System.IO.Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Failed to import gates:\n" + ex.Message,
                "Import Gates", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void ExportGatesButton_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(Window.GetWindow(this),
            "Gate Export (.spl) is not implemented yet.",
            "Export Gates", MessageBoxButton.OK, MessageBoxImage.Information);

    private void StatusGate(string msg)
    {
        if (HoverTipText is not null)
            HoverTipText.Text = msg;
        if (HoverTip is not null)
            HoverTip.Visibility = Visibility.Visible;
    }

    private void InitBasemapUi()
    {
        if (BasemapSourceCombo is null)
            return;

        _suppressBasemapSourceChanged = true;
        try
        {
            BasemapSourceCombo.Items.Clear();
            var lang = Loc.Language;
            foreach (var src in MapTileSources.Presets)
            {
                BasemapSourceCombo.Items.Add(new BasemapSourceItem(src.Id, src.DisplayName(lang)));
            }

            var prefs = AppearanceService.Preferences.Map;
            var match = BasemapSourceCombo.Items.Cast<BasemapSourceItem>()
                .FirstOrDefault(i => string.Equals(i.Id, prefs.SourceId, StringComparison.OrdinalIgnoreCase));
            BasemapSourceCombo.SelectedItem = match ?? BasemapSourceCombo.Items[0];

            if (BasemapCheck is not null)
                BasemapCheck.IsChecked = prefs.BasemapEnabled;
        }
        finally
        {
            _suppressBasemapSourceChanged = false;
        }

        ApplyBasemapFromPreferences();
    }

    private void ApplyBasemapFromPreferences()
    {
        _tileLayer?.ApplyPreferences();
        // Dim scale grid when basemap is on so tiles stay readable.
        if (GridOverlay is not null)
            GridOverlay.Opacity = AppearanceService.Preferences.Map.BasemapEnabled ? 0.35 : 1.0;
    }

    private void ApplyLocalizedChrome()
    {
        // Title texts in XAML that we can reach by name where present.
        if (TitleText is not null)
            TitleText.Text = Loc.T("Map.Title");
        if (AddGateButton is not null)
        {
            AddGateButton.Content = Loc.T("Map.AddGate");
            AddGateButton.ToolTip = Loc.T("Map.AddGateTip");
        }
        if (RenameGateButton is not null)
        {
            RenameGateButton.Content = Loc.T("Map.Rename");
            RenameGateButton.ToolTip = Loc.T("Map.RenameTip");
        }
        if (DeleteGateButton is not null)
        {
            DeleteGateButton.Content = Loc.T("Map.Delete");
            DeleteGateButton.ToolTip = Loc.T("Map.DeleteTip");
        }
        if (DeleteAllGatesButton is not null)
        {
            DeleteAllGatesButton.Content = Loc.T("Map.DeleteAll");
            DeleteAllGatesButton.ToolTip = Loc.T("Map.DeleteAllTip");
        }
        if (ImportGatesButton is not null)
        {
            ImportGatesButton.Content = Loc.T("Map.Import");
            ImportGatesButton.ToolTip = Loc.T("Map.ImportTip");
        }
        if (ExportGatesButton is not null)
        {
            ExportGatesButton.Content = Loc.T("Map.Export");
            ExportGatesButton.ToolTip = Loc.T("Map.ExportTip");
        }
        if (BasemapCheck is not null)
        {
            BasemapCheck.Content = Loc.T("Map.Basemap");
            BasemapCheck.ToolTip = Loc.T("Map.BasemapTip");
        }
        if (BasemapSourceCombo is not null)
        {
            BasemapSourceCombo.ToolTip = Loc.T("Map.SourceTip");
            var selectedId = (BasemapSourceCombo.SelectedItem as BasemapSourceItem)?.Id
                             ?? AppearanceService.Preferences.Map.SourceId;
            _suppressBasemapSourceChanged = true;
            try
            {
                BasemapSourceCombo.Items.Clear();
                foreach (var src in MapTileSources.Presets)
                    BasemapSourceCombo.Items.Add(new BasemapSourceItem(src.Id, src.DisplayName(Loc.Language)));
                BasemapSourceCombo.SelectedItem = BasemapSourceCombo.Items.Cast<BasemapSourceItem>()
                    .FirstOrDefault(i => i.Id == selectedId) ?? BasemapSourceCombo.Items[0];
            }
            finally
            {
                _suppressBasemapSourceChanged = false;
            }
        }
        if (GateCombo is not null)
            GateCombo.ToolTip = Loc.T("Map.SelectGate");
        if (GateWidthBox is not null)
            GateWidthBox.ToolTip = Loc.T("Map.WidthTip");
        if (EmptyHint is not null && EmptyHint.Children.Count >= 2)
        {
            if (EmptyHint.Children[0] is TextBlock t0)
                t0.Text = Loc.T("Map.Title");
            if (EmptyHint.Children[1] is TextBlock t1)
                t1.Text = Loc.T("Map.Waiting");
        }
    }

    private void BasemapCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (BasemapCheck is null)
            return;
        AppearanceService.SetMapBasemap(BasemapCheck.IsChecked == true);
        ApplyBasemapFromPreferences();
    }

    private void BasemapSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBasemapSourceChanged || BasemapSourceCombo?.SelectedItem is not BasemapSourceItem item)
            return;

        string? customUrl = AppearanceService.Preferences.Map.CustomUrlTemplate;
        string? tiandituKey = AppearanceService.Preferences.Map.TiandituKey;

        if (item.Id == MapTileSources.CustomId)
        {
            customUrl = PromptText(
                Loc.T("Map.CustomUrl"),
                Loc.T("Map.CustomUrlPrompt"),
                customUrl ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png");
            if (customUrl is null)
            {
                RevertBasemapSelection();
                return;
            }
        }

        var preset = MapTileSources.Find(item.Id);
        if (preset?.NeedsTiandituKey == true && string.IsNullOrWhiteSpace(tiandituKey))
        {
            tiandituKey = PromptText(
                Loc.T("Map.TiandituKey"),
                Loc.T("Map.TiandituKeyPrompt"),
                "");
            if (string.IsNullOrWhiteSpace(tiandituKey))
            {
                RevertBasemapSelection();
                return;
            }
        }

        AppearanceService.SetMapSource(item.Id, customUrl, tiandituKey);
        _tileLayer?.SetSource(AppearanceService.CurrentMapSource());
        _tileLayer?.Invalidate(force: true);
    }

    private void RevertBasemapSelection()
    {
        _suppressBasemapSourceChanged = true;
        try
        {
            var prev = AppearanceService.Preferences.Map.SourceId;
            if (BasemapSourceCombo is not null)
            {
                BasemapSourceCombo.SelectedItem = BasemapSourceCombo.Items.Cast<BasemapSourceItem>()
                    .FirstOrDefault(i => i.Id == prev) ?? BasemapSourceCombo.Items[0];
            }
        }
        finally
        {
            _suppressBasemapSourceChanged = false;
        }
    }

    private sealed class BasemapSourceItem
    {
        public string Id { get; }
        public string Name { get; }
        public BasemapSourceItem(string id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }


    private void RefreshGateCombo()
    {
        if (GateCombo is null)
            return;
        _suppressGateComboChanged = true;
        try
        {
            var selected = GateStore.Instance.SelectedId;
            GateCombo.Items.Clear();
            foreach (var g in GateStore.Instance.Gates)
            {
                var item = new GateComboItem(g.Id, g.Name);
                GateCombo.Items.Add(item);
                if (selected == g.Id)
                    GateCombo.SelectedItem = item;
            }
            if (GateCombo.SelectedItem is null && GateCombo.Items.Count > 0)
                GateCombo.SelectedIndex = 0;
        }
        finally
        {
            _suppressGateComboChanged = false;
        }
        SyncGateWidthBox();
    }

    private void SyncGateWidthBox()
    {
        if (GateWidthBox is null)
            return;
        var g = GateStore.Instance.Selected;
        GateWidthBox.Text = g is null
            ? "20"
            : g.WidthMeters.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class GateComboItem
    {
        public Guid Id { get; }
        public string Name { get; }
        public GateComboItem(Guid id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }

    private static string? PromptText(string title, string label, string initial)
    {
        var win = new Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x17, 0x1E))
        };
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6)),
            Margin = new Thickness(0, 0, 0, 6)
        });
        var box = new TextBox
        {
            Text = initial,
            Height = 28,
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x13, 0x1A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x25, 0x30)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xA3, 0x4A)),
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 0, 6, 0)
        };
        root.Children.Add(box);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var ok = new Button { Content = "OK", Width = 72, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 72, Height = 28, IsCancel = true };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; win.DialogResult = true; };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        root.Children.Add(row);
        win.Content = root;
        win.Owner = Window.GetWindow(Application.Current.MainWindow);
        return win.ShowDialog() == true ? result : null;
    }

    private bool TryPlaceGateAtMouse(MouseButtonEventArgs e)
    {
        if (_gatePlaceMode != GatePlaceMode.Add)
            return false;
        if (_projection is null || !_hasData || _samples.Count == 0)
        {
            StatusGate("Need a track with valid GPS first");
            return true;
        }

        var pixel = _wpfPlot.GetPlotPixelPosition(e);
        var coordinates = _wpfPlot.Plot.GetCoordinates(
            pixel.X, pixel.Y, _wpfPlot.Plot.Axes.Bottom, _wpfPlot.Plot.Axes.Left);

        var width = GridOverlay.ActualWidth;
        var limits = _wpfPlot.Plot.Axes.GetLimits();
        var metersPerPixelX = width > 1 ? (limits.Right - limits.Left) / width : 1;
        var index = FindNearestSample(coordinates.X, coordinates.Y, metersPerPixelX * HoverPickPixels * 2);
        double lat, lon;
        double? heading = null;
        if (index >= 0)
        {
            var sample = _samples[index];
            lat = sample.Latitude;
            lon = sample.Longitude;
            heading = EstimateHeadingDegAt(index);
        }
        else
        {
            (lat, lon) = _projection.ToLatLon(coordinates.X, coordinates.Y);
        }

        var widthM = 20.0;
        if (double.TryParse(GateWidthBox?.Text?.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedW) || double.TryParse(GateWidthBox?.Text?.Trim(), out parsedW))
            widthM = Math.Max(0.5, parsedW);

        var gate = new GateDefinition
        {
            Name = "",
            Latitude = lat,
            Longitude = lon,
            WidthMeters = widthM,
            HeadingDeg = heading
        };
        GateStore.Instance.Add(gate);
        _gatePlaceMode = GatePlaceMode.None;
        StatusGate($"{gate.Name} @ {lat:F6},{lon:F6}  W={widthM:0.##}m");
        e.Handled = true;
        return true;
    }

    private bool TrySelectGateAtMouse(MouseButtonEventArgs e)
    {
        if (_projection is null || GateStore.Instance.Gates.Count == 0)
            return false;

        var pixel = _wpfPlot.GetPlotPixelPosition(e);
        var coordinates = _wpfPlot.Plot.GetCoordinates(
            pixel.X, pixel.Y, _wpfPlot.Plot.Axes.Bottom, _wpfPlot.Plot.Axes.Left);
        var width = GridOverlay.ActualWidth;
        if (width < 1)
            return false;
        var limits = _wpfPlot.Plot.Axes.GetLimits();
        var metersPerPixelX = (limits.Right - limits.Left) / width;
        var threshold = metersPerPixelX * 10;

        Guid? bestId = null;
        var bestDist = threshold * threshold;
        foreach (var g in GateStore.Instance.Gates)
        {
            if (!g.IsValid) continue;
            var (lat0, lon0, lat1, lon1) = GateCrossing.GetGateEndpointsLatLon(EnsureHeading(g));
            var (x0, y0) = _projection.ToMeters(lat0, lon0);
            var (x1, y1) = _projection.ToMeters(lat1, lon1);
            var d2 = DistancePointToSegmentSq(coordinates.X, coordinates.Y, x0, y0, x1, y1);
            if (d2 < bestDist)
            {
                bestDist = d2;
                bestId = g.Id;
            }
        }

        if (bestId is null)
            return false;

        GateStore.Instance.Select(bestId);
        StatusGate($"Selected {GateStore.Instance.Selected?.Name}");
        e.Handled = true;
        return true;
    }

    private GateDefinition EnsureHeading(GateDefinition g)
    {
        if (g.HeadingDeg is not null)
            return g;
        // Fallback east-west gate (heading north) for drawing when unknown.
        return new GateDefinition
        {
            Id = g.Id,
            Name = g.Name,
            Latitude = g.Latitude,
            Longitude = g.Longitude,
            WidthMeters = g.WidthMeters,
            HeadingDeg = 0
        };
    }

    private double? EstimateHeadingDegAt(int index)
    {
        if (_projection is null || _samples.Count < 2)
            return null;
        var i0 = Math.Max(0, index - 3);
        var i1 = Math.Min(_samples.Count - 1, index + 3);
        if (i1 <= i0) return null;
        var a = _samples[i0];
        var b = _samples[i1];
        if (!TrackProjection.IsValidGps(a) || !TrackProjection.IsValidGps(b))
            return null;
        var (x0, y0) = _projection.ToMeters(a.Latitude, a.Longitude);
        var (x1, y1) = _projection.ToMeters(b.Latitude, b.Longitude);
        var dx = x1 - x0;
        var dy = y1 - y0;
        if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6)
            return null;
        // Atan2(east, north) -> heading deg
        return Math.Atan2(dx, dy) * 180.0 / Math.PI;
    }

    private static double DistancePointToSegmentSq(
        double px, double py, double x0, double y0, double x1, double y1)
    {
        var vx = x1 - x0;
        var vy = y1 - y0;
        var len2 = vx * vx + vy * vy;
        if (len2 < 1e-12)
        {
            var dx = px - x0;
            var dy = py - y0;
            return dx * dx + dy * dy;
        }
        var t = ((px - x0) * vx + (py - y0) * vy) / len2;
        t = Math.Clamp(t, 0, 1);
        var qx = x0 + t * vx;
        var qy = y0 + t * vy;
        var ex = px - qx;
        var ey = py - qy;
        return ex * ex + ey * ey;
    }


    private void RefreshGateColorLegend()
    {
        if (GateLegendPanel is null || GateLegendItems is null)
            return;

        GateLegendItems.Items.Clear();
        var gates = GateStore.Instance.Gates;
        if (gates.Count == 0)
        {
            GateLegendPanel.Visibility = Visibility.Collapsed;
            return;
        }

        GateLegendPanel.Visibility = Visibility.Visible;
        foreach (var g in gates)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1, 0, 1)
            };
            var swatch = new System.Windows.Shapes.Rectangle
            {
                Width = 14,
                Height = 4,
                RadiusX = 1,
                RadiusY = 1,
                Fill = (Brush)new BrushConverter().ConvertFromString(
                    string.IsNullOrWhiteSpace(g.ColorHex) ? "#3FBF6F" : g.ColorHex)!,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            var label = new TextBlock
            {
                Text = g.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC3, 0xCB, 0xD8)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 150
            };
            if (GateStore.Instance.SelectedId == g.Id)
                label.FontWeight = FontWeights.SemiBold;
            row.Children.Add(swatch);
            row.Children.Add(label);
            GateLegendItems.Items.Add(row);
        }
    }

    private void RedrawGates()
    {
        foreach (var p in _gatePlots)
            _wpfPlot.Plot.Remove(p);
        _gatePlots.Clear();

        if (_projection is null)
        {
            _wpfPlot.Refresh();
            return;
        }

        var selectedId = GateStore.Instance.SelectedId;

        foreach (var g in GateStore.Instance.Gates)
        {
            if (!g.IsValid) continue;
            var draw = EnsureHeading(g);
            var (lat0, lon0, lat1, lon1) = GateCrossing.GetGateEndpointsLatLon(draw);
            var (x0, y0) = _projection.ToMeters(lat0, lon0);
            var (x1, y1) = _projection.ToMeters(lat1, lon1);
            var xs = new[] { x0, x1 };
            var ys = new[] { y0, y1 };

            var isSelected = selectedId == g.Id;
            var color = string.IsNullOrWhiteSpace(g.ColorHex) ? "#3FBF6F" : g.ColorHex;

            // Dark underlay for visibility on track
            var under = _wpfPlot.Plot.Add.ScatterLine(xs, ys, ScottPlot.Color.FromHex("#0A0E14").WithAlpha(0.9));
            under.LineWidth = isSelected ? 10f : 7f;
            under.MarkerStyle.IsVisible = false;
            under.LegendText = string.Empty; // keep gates out of UpperRight track legend
            _gatePlots.Add(under);

            var scatter = _wpfPlot.Plot.Add.ScatterLine(xs, ys, ScottPlot.Color.FromHex(color));
            scatter.LineWidth = isSelected ? 5.5f : 4.0f;
            scatter.MarkerStyle.IsVisible = false;
            scatter.LegendText = string.Empty;
            _gatePlots.Add(scatter);
        }

        // UpperRight = tracks/files/runs only; UpperLeft = WPF gate color legend.
        UpdateLegendVisibility(_tracks.Count > 1 || _runHighlights.Count > 0);
        RefreshGateColorLegend();
        _wpfPlot.Refresh();
    }
    private int FindNearestSample(
        double x,
        double y,
        double thresholdMeters)
    {
        var projection = _projection!;

        var stride = Math.Max(1, _samples.Count / 200_000);

        var bestIndex = -1;
        var bestDistanceSquared = thresholdMeters * thresholdMeters;

        for (var i = 0; i < _samples.Count; i += stride)
        {
            if (!TrackProjection.IsValidGps(_samples[i]))
                continue;

            var (sampleX, sampleY) = projection.ToMeters(
                _samples[i].Latitude,
                _samples[i].Longitude);

            var dx = sampleX - x;
            var dy = sampleY - y;

            var distanceSquared = dx * dx + dy * dy;

            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
