using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Chassis_Master_Test_Suite.Core;

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

    private readonly ScottPlot.WPF.WpfPlot _wpfPlot = new();
    private readonly List<VehicleSample> _samples = new();
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

    // 网格重绘缓存：间距和尺寸都没变就不用重画。
    private double _drawnSpacing;
    private double _drawnWidth;
    private double _drawnHeight;
    private bool _updatingGrid;

    public TrackMapPanel()
    {
        InitializeComponent();

        BuildPlot();

        PlotHost.Children.Add(_wpfPlot);

        GridOverlay.SizeChanged += (_, _) => UpdateGridOverlay();
    }

    /// <summary>
    /// 当前载入的样本条数。
    /// </summary>
    public int SampleCount => _samples.Count;

    /// <summary>
    /// 用户在轨迹上点选样本时触发（供曲线光标 / Dashboard 同步）。
    /// </summary>
    public event Action<VehicleSample>? SampleSelected;

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

        plot.RenderManager.RenderFinished += (_, _) => UpdateGridOverlay();

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
        // Alpha=0：网格 Canvas 在 Plot 下方可见，轨迹画在网格之上
        var transparent = ScottPlot.Colors.Transparent;
        _wpfPlot.Plot.SetStyle(
            new ScottPlot.PlotStyle
            {
                FigureBackgroundColor = transparent,
                DataBackgroundColor = transparent,
                Palette = new ScottPlot.Palettes.Dark()
            });
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

        var countDelta = samples.Count - _samples.Count;

        var isBigChange =
            countDelta < 0
            || countDelta > 500
            || _projectionAnchor is null;

        if (!isBigChange
            && _refreshStopwatch.ElapsedMilliseconds
                < MinRefreshIntervalMilliseconds)
        {
            return;
        }

        _refreshStopwatch.Restart();

        Rebuild(samples);
    }

    /// <summary>
    /// 清空轨迹。
    /// </summary>
    public void Clear()
    {
        _samples.Clear();
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

        var (minX, maxX, minY, maxY) = GetBounds(_samples, _projection);

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

    private void Rebuild(IReadOnlyList<VehicleSample> samples)
    {
        var first = samples[0];

        var needNewProjection =
            _projection is null
            || _projectionAnchor is null
            || Math.Abs(_projectionAnchor.Value.Latitude - first.Latitude) > 1e-9
            || Math.Abs(_projectionAnchor.Value.Longitude - first.Longitude) > 1e-9;

        // Clear() 会让轴变 unset，下一帧可能自动缩放到数据；
        // 用户已手动取景时必须先记下视野，重建后再写回去。
        var hadData = _hasData;
        var savedLimits = hadData
            ? _wpfPlot.Plot.Axes.GetLimits()
            : default;

        _samples.Clear();
        _samples.AddRange(samples);

        // 纵向/横向的抽稀步长
        var stride = Math.Max(1, _samples.Count / MaxDisplayPoints);

        if (needNewProjection)
        {
            _projection = TrackProjection.FitToTrack(_samples);
            _projectionAnchor = (first.Latitude, first.Longitude);
            // 新数据源：重新允许自动取景
            _userHasAdjustedView = false;
        }

        var projection = _projection!;

        var xs = new List<double>(_samples.Count / stride + 1);
        var ys = new List<double>(_samples.Count / stride + 1);

        for (var i = 0; i < _samples.Count; i += stride)
        {
            var (x, y) = projection.ToMeters(
                _samples[i].Latitude,
                _samples[i].Longitude);

            xs.Add(x);
            ys.Add(y);
        }

        // 抽稀容易把最后一个点漏掉，补上，否则轨迹末端会短一截。
        if ((_samples.Count - 1) % stride != 0)
        {
            var (x, y) = projection.ToMeters(
                _samples[^1].Latitude,
                _samples[^1].Longitude);

            xs.Add(x);
            ys.Add(y);
        }

        _wpfPlot.Plot.Clear();

        // Clear() 会复位样式，必须重新套。
        ApplyDarkStyle();
        _wpfPlot.Plot.Axes.ContinuouslyAutoscale = false;

        _track = _wpfPlot.Plot.Add.ScatterLine(
            xs,
            ys,
            ScottPlot.Color.FromHex("#E05252"));

        _track.LineWidth = 2.0f;
        _track.MarkerStyle.IsVisible = false;

        _hasData = true;

        EmptyHint.Visibility = Visibility.Collapsed;

        UpdateCoordinateReadout();

        // Clear() 清掉了光标标记，重建后再画上
        _cursorMarker = null;
        UpdateCursorMarker(refresh: false);

        // 首包 / 换数据源 / 用户尚未手动操作 → 自动取景；
        // 用户已平移缩放 → 恢复 Clear 前的视野，绝不弹回全图。
        if (!_userHasAdjustedView)
        {
            AutoFit();
        }
        else
        {
            _wpfPlot.Plot.Axes.SetLimits(savedLimits);
            _wpfPlot.Refresh();
        }
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

    private static (double MinX, double MaxX, double MinY, double MaxY) GetBounds(
        IReadOnlyList<VehicleSample> samples,
        TrackProjection projection)
    {
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;

        foreach (var sample in samples)
        {
            var (x, y) = projection.ToMeters(
                sample.Latitude,
                sample.Longitude);

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

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
            !_hasData)
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
        SampleSelected?.Invoke(sample);

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
