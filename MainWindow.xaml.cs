using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Communication;
using Chassis_Master_Test_Suite.Communication.GSpot;
using Chassis_Master_Test_Suite.Controls;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Localization;
using Chassis_Master_Test_Suite.Themes;
using Chassis_Master_Test_Suite.Recorder;
using Chassis_Master_Test_Suite.Simulator;
using Chassis_Master_Test_Suite.Session;

namespace Chassis_Master_Test_Suite;

public partial class MainWindow : Window
{
    private readonly DataBus _dataBus = new();

    /// <summary>
    /// 当前活动数据源（UDP / GSpot）所有权；Settings 完善前用顶部按钮切换。
    /// </summary>
    private readonly DataSourceSession _dataSourceSession;

    private UdpSender? _udpSender;

    // ============================================================
    // 录制（VBO）— 文件 / 状态机在 RecordingSession；UI 外观仍在本类。
    // ============================================================

    private readonly RecordingSession _recordingSession = new();

    private DispatcherTimer? _elapsedTimer;

    private CancellationTokenSource? _cancellationTokenSource;

    private const int MaxHistorySamples = 1_000_000;

    private readonly SampleHistoryBuffer _sampleHistory =
        new(MaxHistorySamples);

    /// <summary>
    /// 打开 VBO 离线回放后为 true：消费循环仍读总线，但不写入历史，
    /// 避免实时包污染 Replay 曲线 / Track Map。切回 live 源时清零。
    /// </summary>
    private volatile bool _historyOfflineMode;

    private long _sampleCount;

    /// <summary>最近一次成功加载 VBO 的可绘通道数（不含合成 Time）。</summary>
    private int _lastLoadedChannelCount;

    private readonly DispatcherTimer _uiTimer;

    private VehicleSample? _latestSample;

    /// <summary>true = 线下模式（VBO）；false = 线上模式。</summary>
    private bool _isOfflineMode;

    // Layout panel visibility (saved GridLengths restored when shown again)
    private bool _layoutShowDashboard = true;
    private bool _layoutShowTestResults = true;
    private bool _layoutShowMap = true;
    private bool _layoutShowChart = true;
    private GridLength? _layoutDashboardWidth;
    private GridLength? _layoutTestResultsWidth;
    private GridLength? _layoutMapWidth;
    private GridLength? _layoutCurvesHeight;
    private double _layoutDashboardMin = 280;
    private double _layoutTestResultsMin = 260;
    private double _layoutMapMin = 300;
    private double _layoutCurvesMin = 240;
    private double _layoutTopPanelsMin = 200;

    private static readonly GridLength LayoutDefaultDashboardWidth =
        new(1.2, GridUnitType.Star);
    private static readonly GridLength LayoutDefaultTestResultsWidth =
        new(1.0, GridUnitType.Star);
    private static readonly GridLength LayoutDefaultMapWidth =
        new(1.6, GridUnitType.Star);
    private static readonly GridLength LayoutDefaultCurvesHeight =
        new(1.0, GridUnitType.Star);
    private static readonly GridLength LayoutDefaultTopPanelsHeight =
        new(1.0, GridUnitType.Star);

    private static readonly SolidColorBrush LayoutRegionActiveBg =
        MakeFrozenBrush(0x1E, 0x28, 0x34);
    private static readonly SolidColorBrush LayoutRegionActiveBorder =
        MakeFrozenBrush(0xC8, 0xA3, 0x4A);
    private static readonly SolidColorBrush LayoutRegionActiveFg =
        MakeFrozenBrush(0xC8, 0xA3, 0x4A);
    private static readonly SolidColorBrush LayoutRegionInactiveBg =
        MakeFrozenBrush(0x12, 0x17, 0x1E);
    private static readonly SolidColorBrush LayoutRegionInactiveBorder =
        MakeFrozenBrush(0x3A, 0x45, 0x54);
    private static readonly SolidColorBrush LayoutRegionInactiveFg =
        MakeFrozenBrush(0x7A, 0x84, 0x94);

    private static SolidColorBrush MakeFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>线下已打开的 VBO 集合（可多文件对比）。</summary>
    private readonly OfflineFileSet _offlineFiles = new();

    /// <summary>线上会话：第一条样本时间戳（毫秒）。</summary>
    private long? _liveSessionOriginMs;

    /// <summary>线上累计行驶距离（米）。</summary>
    private double _liveDistanceM;

    private VehicleSample? _livePreviousSample;

    // ============================================================
    // Simulator
    // ============================================================

    private SimulatorWindow? _simulatorWindow;

    // ============================================================
    // Plot 系统
    // ============================================================

    private readonly List<PlotDefinition> _plots = new();

    private int _nextPlotNumber = 1;

    private const double PlotMinHeightPx = 120;

    /// <summary>Last known curves viewport height (ScrollViewer.ViewportHeight).</summary>
    private double _curvesViewportHeight = 280;

    /// <summary>When true and only one plot, keep that plot height = curves viewport.</summary>
    private bool _singlePlotFillsViewport = true;


    private bool _isRefreshingPlots;

    /// <summary>
    /// 当前驱动 Dashboard 冻结读数的曲线（有光标时）。
    /// Escape 清除光标后恢复实时。
    /// </summary>
    private PlotDefinition? _cursorSourcePlot;

    /// <summary>Test Results 标注：最近一次 Compute 的 run 与对应样本。</summary>
    private IReadOnlyList<AnnotatedTestRun> _annotatedRuns = Array.Empty<AnnotatedTestRun>();
    private IReadOnlyList<AnnotatedTestRun> _compareRuns = Array.Empty<AnnotatedTestRun>();
    private int? _selectedAnnotatedRun;

    /// <summary>
    /// 中键平移 / 右键缩放进行中：跳过 RefreshPlot，避免 10 Hz Clear 把手势锁死。
    /// </summary>
    private int _activeManualAxisGestures;

    /// <summary>
    /// SuspendAutoScale 时程序化改勾选，不要顺带触发 RefreshAllPlots。
    /// </summary>
    private bool _suppressAutoScaleCheckboxRefresh;

    /// <summary>
    /// 曲线脏检查：与上次成功重建时历史尾指纹相同则跳过 Clear+重建。
    /// UI 改通道 / 轴 / 手势结束时置 <see cref="_forcePlotRebuild"/>。
    /// </summary>
    private int _lastPlotHistoryCount = -1;

    private long _lastPlotHistoryTimestamp;

    private long _lastPlotHistorySequence;

    /// <summary>通道/轴/视野变化时强制下一拍重建（即使样本指纹未变）。</summary>
    private bool _forcePlotRebuild = true;

    /// <summary>北京时间（Asia/Shanghai / China Standard Time）。</summary>
    private static readonly TimeZoneInfo BeijingTimeZone = ResolveBeijingTimeZone();

    private static TimeZoneInfo ResolveBeijingTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows()
                    ? "China Standard Time"
                    : "Asia/Shanghai");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                "Beijing",
                TimeSpan.FromHours(8),
                "Beijing",
                "Beijing");
        }
    }


    public MainWindow()
    {
        _dataSourceSession = new DataSourceSession(_dataBus);

        InitializeComponent();

        // P0 Test Results：注入历史快照提供者（Replay / 实时缓冲）
        TestResultsPanelControl.GetSamples = GetHistorySnapshotForAnalysis;
        TestResultsPanelControl.GetSampleSources = GetTestSampleSources;
        TestResultsPanelControl.ResultsAnnotated += OnTestResultsAnnotated;
        TestResultsPanelControl.SelectedRunChanged += OnTestResultRunSelected;
        TestResultsPanelControl.CheckedRunsChanged += OnTestResultCheckedRunsChanged;
        TestResultsPanelControl.SessionEditRequested += OnSessionEditRequested;
        TestResultsPanelControl.MathsChanged += OnMathsChanged;

        ApplyModeUi();

        InitializeAxisSelector();

        InitializePlotSystem();

        ChannelRegistry.Instance.AvailableChanged += (_, _) =>
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RefreshChannelSelectorsFromRegistry);
                return;
            }

            RefreshChannelSelectorsFromRegistry();
        };

        TrackMapPanelControl.SampleSelected += ApplyCursorFromTrackSample;
        DashboardPanelControl.VehicleSummaryClicked += OnVehicleSummaryClicked;

        GateStore.Instance.Changed += (_, _) => ScheduleSessionMemorySave();
        MathsChannelStore.Instance.Changed += (_, _) =>
        {
            // Persist maths immediately (not only debounced) so restart keeps formulas
            // even if the process is killed before the 800ms timer fires.
            if (!_restoringSession)
                SaveSessionMemory();
            else
                ScheduleSessionMemorySave();
        };

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        _uiTimer.Tick += UiTimer_Tick;

        StartClock();

        Loaded += MainWindow_Loaded;

        Closed += MainWindow_Closed;

        Loc.LanguageChanged += (_, _) =>
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ApplyLocalizedToolbar);
                return;
            }
            ApplyLocalizedToolbar();
        };
        ApplyLocalizedToolbar();
    }

    // ============================================================
    // 顶部栏时钟（1 秒刷新）
    // ============================================================

    private DispatcherTimer? _clockTimer;

    private void StartClock()
    {
        UpdateClock();

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _clockTimer.Tick += (_, _) => UpdateClock();

        _clockTimer.Start();
    }

    private void UpdateClock()
    {
        if (ClockText is null)
            return;

        ClockText.Text = DateTime.Now.ToString(
            "yyyy-MM-dd HH:mm:ss");
    }


    // ============================================================
    // 录制状态机
    //
    // Stopped -> Recording <-> Paused -> Stopped
    //
    // 暂停时只是不写文件，
    // 实时曲线、数值显示和数据消费都照常运行。
    // ============================================================

    private void RecordButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        switch (_recordingSession.State)
        {
            case RecordingState.Stopped:
                ApplyRecordingState(
                    RecordingState.Recording);
                break;

            case RecordingState.Recording:
                ApplyRecordingState(
                    RecordingState.Paused);
                break;

            case RecordingState.Paused:
                ApplyRecordingState(
                    RecordingState.Recording);
                break;
        }
    }

    private void StopRecordButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ApplyRecordingState(
            RecordingState.Stopped);
    }

    /// <summary>
    /// 切换录制状态（委托 RecordingSession），并同步按钮外观和 Elapsed 计时器。
    /// </summary>
    private void ApplyRecordingState(
        RecordingState state)
    {
        _recordingSession.ApplyState(state);

        switch (state)
        {
            case RecordingState.Stopped:
                _elapsedTimer?.Stop();

                RecordGlyphText.Text = "●";
                RecordLabelText.Text = "Start";
                RecordGlyphText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0xE0, 0x8A, 0x8A));

                StopRecordButton.IsEnabled = false;

                ElapsedTimeText.Text = "--";
                break;

            case RecordingState.Recording:
                RecordGlyphText.Text = "❚❚";
                RecordLabelText.Text = "Pause";
                RecordGlyphText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0xC8, 0xA3, 0x4A));

                StopRecordButton.IsEnabled = true;

                StartElapsedTimer();
                break;

            case RecordingState.Paused:
                RecordGlyphText.Text = "▶";
                RecordLabelText.Text = "Resume";

                StopRecordButton.IsEnabled = true;
                break;
        }
    }

    private void StartElapsedTimer()
    {
        if (_elapsedTimer is null)
        {
            _elapsedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            _elapsedTimer.Tick += (_, _) => UpdateElapsedText();
        }

        _elapsedTimer.Start();
    }

    private void UpdateElapsedText()
    {
        if (ElapsedTimeText is null)
            return;

        if (_recordingSession.RecordingStartTimestamp is null ||
            _latestSample is null)
        {
            ElapsedTimeText.Text = "00:00:00.0";
            return;
        }

        var elapsed =
            TimeSpan.FromMilliseconds(
                _latestSample.Timestamp -
                _recordingSession.RecordingStartTimestamp.Value);

        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        ElapsedTimeText.Text =
            $"{(int)elapsed.TotalHours:00}:" +
            $"{elapsed.Minutes:00}:" +
            $"{elapsed.Seconds:00}." +
            $"{elapsed.Milliseconds / 100}";
    }


    // ============================================================
    // 离线回放：打开 VBO 文件
    // ============================================================

    /// <summary>
    /// 读取一个 VBO 文件并载入到历史缓冲区，
    /// 载入后曲线和数值显示都会立即刷新。
    ///
    /// 返回读到的样本数，失败时返回 0。
    /// </summary>
    public int LoadVboFile(string filePath)
    {
        try
        {
            var reader = new VboReader(filePath);
            var raw = reader.ReadAll();
            if (raw.Count == 0)
                return 0;

            if (reader.SessionData.Count > 0)
            {
                SessionMetadata.Current = SessionMetadata.FromVboLines(reader.SessionData);
                TestResultsPanelControl.RefreshSessionSummary();
            }

            var samples = SampleEnricher.EnrichAll(raw);

            var columnNames = reader.Columns.Count > 0
                ? reader.Columns
                : samples[0].Channels.Keys.ToList();

            var entry = _offlineFiles.Add(filePath, samples, columnNames);
            ApplyOfflineDataset();

            _lastLoadedChannelCount =
                ChannelRegistry.Instance.AvailablePlotChannels.Count;

            SaveSessionMemory();
            return samples.Count;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"无法读取 VBO 文件：\n{filePath}\n\n{ex.Message}",
                "VBO",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return 0;
        }
    }


    /// <summary>
    /// 按当前已打开线下文件重建通道目录、主历史、文件芯片 UI。
    /// </summary>
    private void ApplyOfflineDataset()
    {
        _historyOfflineMode = true;

        if (_offlineFiles.Count == 0)
        {
            _sampleHistory.Clear();
            _latestSample = null;
            ChannelRegistry.Instance.SetLiveCore();
            RemergeMathsChannels();
            RefreshChannelSelectorsFromRegistry();
            RebuildOfflineFileChips();
            RefreshAllPlots(force: true);
            UpdateNumericDisplay();
            return;
        }

        var columns = _offlineFiles.UnionColumns();
        ChannelRegistry.Instance.SetFromVboColumns(columns);
        RemergeMathsChannels();
        RefreshChannelSelectorsFromRegistry();

        // 主历史 = 当前选中文件（芯片点击切换；默认最新打开）
        var primary = _offlineFiles.Primary!;
        _sampleHistory.Clear();
        _sampleHistory.AddRange(primary.Samples);
        _latestSample = primary.Samples[^1];

        RebuildOfflineFileChips();
        RefreshAllPlots(force: true);
        UpdateNumericDisplay();
    }


    private void RebuildOfflineFileChips()
    {
        if (OfflineFilesPanel is null)
            return;

        OfflineFilesPanel.Children.Clear();

        var selectedId = _offlineFiles.Selected?.Id;

        foreach (var file in _offlineFiles.Files)
        {
            var isSelected = file.Id == selectedId;

            var chip = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString(
                    isSelected ? "#243040" : "#1A222C")!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString(file.ColorHex)!,
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 4, 2),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = file.Id,
                ToolTip = isSelected
                    ? $"Selected: {file.FilePath}"
                    : $"Click to focus Dashboard / cursor on: {file.DisplayName} (all files stay overlaid)"
            };

            chip.MouseLeftButtonUp += OfflineFileChip_Click;

            var row = new StackPanel { Orientation = Orientation.Horizontal };

            row.Children.Add(new TextBlock
            {
                Text = file.DisplayName,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 11,
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                MaxWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            });

            var close = new Button
            {
                Content = "×",
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Tag = file.Id,
                Style = TryFindResource("ToolButtonStyle") as Style,
                ToolTip = "Close this file"
            };
            close.Click += CloseOfflineFileChip_Click;
            row.Children.Add(close);

            chip.Child = row;
            OfflineFilesPanel.Children.Add(chip);
        }
    }

    private void OfflineFileChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Guid id })
            return;

        // 点关闭按钮时不切换
        if (e.OriginalSource is DependencyObject source &&
            FindParentButton(source) is not null)
            return;

        if (_offlineFiles.Selected?.Id == id)
            return;

        SelectOfflineFileKeepingCursor(id);
        e.Handled = true;
    }

    private void ClearPlotCursors()
    {
        _cursorSourcePlot = null;
        foreach (var plot in _plots)
        {
            plot.CursorX = null;
            plot.SelectionX1 = null;
            plot.SelectionX2 = null;
            plot.IsSelectingRange = false;
            if (plot.WpfPlot is not null)
            {
                ApplyPlotOverlays(plot);
                plot.WpfPlot.Refresh();
            }
        }

        TrackMapPanelControl.SetCursorSample(null);
    }


    /// <summary>
    /// Focus an offline VBO (Dashboard / cursor / map). Curves stay multi-file overlay.
    /// Dashboard / Track Map / readouts update to the new file at the same cursor.
    /// </summary>
    private void SelectOfflineFileKeepingCursor(Guid id)
    {
        if (_offlineFiles.Selected?.Id == id)
            return;

        // Preserve absolute cursor X and selection across the file switch.
        double? cursorX = null;
        double? sel1 = null;
        double? sel2 = null;
        foreach (var plot in _plots)
        {
            if (cursorX is null && plot.CursorX is double cx)
                cursorX = cx;
            if (sel1 is null && plot.SelectionX1 is double s1)
                sel1 = s1;
            if (sel2 is null && plot.SelectionX2 is double s2)
                sel2 = s2;
        }

        _offlineFiles.Select(id);
        ApplyOfflineDataset();

        if (cursorX is double keepX)
        {
            foreach (var plot in _plots)
            {
                plot.SelectionX1 = sel1;
                plot.SelectionX2 = sel2;
            }

            var source = _plots.Count > 0 ? _plots[0] : null;
            SetSharedCursor(keepX, source);
            UpdateNumericDisplay();
        }
    }

    private static Button? FindParentButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is Button button)
                return button;
            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }


    private void OnVehicleSummaryClicked(string displayName)
    {
        var file = _offlineFiles.Files.FirstOrDefault(f =>
            string.Equals(f.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
        if (file is null || _offlineFiles.Selected?.Id == file.Id)
            return;

        SelectOfflineFileKeepingCursor(file.Id);
    }


    private void CloseOfflineFileChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id })
            return;

        _offlineFiles.Remove(id);
        ApplyOfflineDataset();
        SaveSessionMemory();
    }


    // ============================================================
    // 导航栏页面切换
    // ============================================================

    private void DashboardNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowPage(dashboard: true);
    }


    private async void ReplayNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EnterOfflineModeAsync(stopOnline: true);
        ShowPage(dashboard: false);
    }


    private void ShowPage(bool dashboard)
    {
        DashboardPage.Visibility =
            dashboard
                ? Visibility.Visible
                : Visibility.Collapsed;

        ReplayPage.Visibility =
            dashboard
                ? Visibility.Collapsed
                : Visibility.Visible;

    }


    // ============================================================
    // Replay：打开 VBO 文件
    // ============================================================

    private async void OpenVboButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        // 线下打开前自动停掉线上连接
        await EnterOfflineModeAsync(stopOnline: true);

        var preferredDirs = new[]
        {
            @"D:\CMTS",
            Path.Combine(AppContext.BaseDirectory, "Recordings"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        var initialDir = preferredDirs.FirstOrDefault(Directory.Exists)
            ?? AppContext.BaseDirectory;

        var dialog =
            new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open VBO file(s)",
                Filter =
                    "VBO files (*.vbo)|*.vbo|All files (*.*)|*.*",
                InitialDirectory = initialDir,
                Multiselect = true
            };

        if (dialog.ShowDialog(this) != true)
            return;

        var total = 0;
        foreach (var file in dialog.FileNames)
            total += LoadVboFile(file);

        if (total <= 0)
            return;

        ShowPage(dashboard: true);

        if (ReplayFileText is not null)
        {
            ReplayFileText.Text =
                _offlineFiles.Count == 1
                    ? _offlineFiles.Primary!.DisplayName
                    : $"{_offlineFiles.Count} files";
        }

        if (ReplayInfoText is not null)
        {
            ReplayInfoText.Text =
                $"{total} samples · {_lastLoadedChannelCount} channels · {_offlineFiles.Count} file(s)";
        }
    }


    private void CloseAllVboButton_Click(object sender, RoutedEventArgs e)
    {
        _offlineFiles.Clear();
        ApplyOfflineDataset();
        UpdateOfflineFileStatusLabels();
        SaveSessionMemory();
    }


    private async void OnlineModeButton_Click(object sender, RoutedEventArgs e)
    {
        await EnterOnlineModeAsync();
    }


    private async void OfflineModeButton_Click(object sender, RoutedEventArgs e)
    {
        await EnterOfflineModeAsync(stopOnline: true);
    }


    private async void StopOnlineButton_Click(object sender, RoutedEventArgs e)
    {
        await StopOnlineConnectionAsync();
        UpdateConnectionStatusUi();
    }


    private async Task EnterOnlineModeAsync()
    {
        var wasOffline = _isOfflineMode;
        _isOfflineMode = false;
        ApplyModeUi();

        // Already online with live history: only refresh toolbar.
        if (!wasOffline && !_historyOfflineMode)
        {
            UpdateConnectionStatusUi();
            return;
        }

        // Switch display to live. Keep opened offline VBO files in memory
        // (VBTS-style): Offline tools hide, but switching back restores them.
        _historyOfflineMode = false;
        _sampleHistory.Clear();
        _latestSample = null;
        _liveSessionOriginMs = null;
        _liveDistanceM = 0;
        _livePreviousSample = null;
        ChannelRegistry.Instance.SetLiveCore();
        RefreshChannelSelectorsFromRegistry();
        RefreshAllPlots(force: true);
        UpdateNumericDisplay();

        // 若当前无连接，尝试拉起默认 UDP
        if (_dataSourceSession.Current is null ||
            _dataSourceSession.Current.State is DataSourceState.Disconnected
                or DataSourceState.Faulted)
        {
            try
            {
                var next = _dataSourceSession.CreateUdpReceiver();
                await SwitchDataSourceAsync(next);
                if (GSpotButton is not null)
                    GSpotButton.Content = "GSpot…";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "启动 UDP 失败:\n" + ex.Message,
                    "Online",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        UpdateConnectionStatusUi();
    }


    private async Task EnterOfflineModeAsync(bool stopOnline)
    {
        _isOfflineMode = true;
        ApplyModeUi();

        if (stopOnline)
            await StopOnlineConnectionAsync();

        // Restore preserved offline files into plots/Dashboard/TrackMap.
        // Opening Online must not clear _offlineFiles (VBTS-style).
        ApplyOfflineDataset();
        UpdateOfflineFileStatusLabels();
        UpdateConnectionStatusUi();
    }


    /// <summary>
    /// Refresh Replay page labels from the current offline file set.
    /// </summary>
    private void UpdateOfflineFileStatusLabels()
    {
        if (ReplayFileText is not null)
        {
            ReplayFileText.Text = _offlineFiles.Count == 0
                ? "No file loaded"
                : _offlineFiles.Count == 1
                    ? _offlineFiles.Primary!.DisplayName
                    : $"{_offlineFiles.Count} files";
        }

        if (ReplayInfoText is not null)
        {
            if (_offlineFiles.Count == 0)
            {
                ReplayInfoText.Text = "";
            }
            else
            {
                var total = _offlineFiles.Files.Sum(f => f.Samples.Count);
                ReplayInfoText.Text =
                    $"{total} samples · {_lastLoadedChannelCount} channels · {_offlineFiles.Count} file(s)";
            }
        }
    }


    private async Task StopOnlineConnectionAsync()
    {
        var old = _dataSourceSession.Current;
        _dataSourceSession.SetCurrent(null);

        if (old is not null)
        {
            try
            {
                await old.StopAsync();
            }
            catch
            {
                try { old.Dispose(); } catch { /* ignore */ }
            }
        }

        if (_simulatorWindow is not null)
        {
            try { _simulatorWindow.Close(); } catch { /* ignore */ }
            _simulatorWindow = null;
        }
    }


    private void ApplyModeUi()
    {
        if (OnlineModeButton is null || OfflineModeButton is null)
            return;

        // Mutual exclusive highlight; file chips stay visible in both modes.
        if (_isOfflineMode)
        {
            OnlineModeButton.Style = (Style)FindResource("NavButtonStyle");
            OfflineModeButton.Style = (Style)FindResource("NavButtonActiveStyle");
            OnlineModeButton.Opacity = 0.45;
            OfflineModeButton.Opacity = 1.0;

            if (OnlineToolsPanel is not null)
                OnlineToolsPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            OnlineModeButton.Style = (Style)FindResource("NavButtonActiveStyle");
            OfflineModeButton.Style = (Style)FindResource("NavButtonStyle");
            OnlineModeButton.Opacity = 1.0;
            OfflineModeButton.Opacity = 0.45;

            if (OnlineToolsPanel is not null)
                OnlineToolsPanel.Visibility = Visibility.Visible;
        }
    }

    // ============================================================
    // Appearance: language + theme
    // ============================================================

    private void LangZh_Click(object sender, RoutedEventArgs e) =>
        AppearanceService.SetLanguage(Loc.ZhCn);

    private void LangEn_Click(object sender, RoutedEventArgs e) =>
        AppearanceService.SetLanguage(Loc.En);

    private void ThemeDark_Click(object sender, RoutedEventArgs e) =>
        AppearanceService.SetTheme(AppThemeMode.Dark);

    private void ThemeLight_Click(object sender, RoutedEventArgs e) =>
        AppearanceService.SetTheme(AppThemeMode.Light);

    // Custom theme is a placeholder in the Theme menu (IsEnabled=false).
    private void ThemeCustom_Click(object sender, RoutedEventArgs e)
    {
        // Reserved for a future custom-color picker.
    }

    private void ApplyLocalizedToolbar()
    {
        Title = Loc.T("App.Title");

        if (LayoutMenuRoot is not null)
        {
            LayoutMenuRoot.Header = Loc.T("Menu.Layout");
            LayoutMenuRoot.ToolTip = Loc.T("Menu.LayoutTip");
        }
        if (LanguageMenu is not null)
        {
            LanguageMenu.Header = Loc.T("Menu.Language");
            LanguageMenu.ToolTip = Loc.T("Menu.LanguageTip");
        }
        if (ThemeMenu is not null)
        {
            ThemeMenu.Header = Loc.T("Menu.Theme");
            ThemeMenu.ToolTip = Loc.T("Menu.ThemeTip");
        }
        if (LangZhItem is not null)
            LangZhItem.Header = Loc.T("Menu.Lang.Zh");
        if (LangEnItem is not null)
            LangEnItem.Header = Loc.T("Menu.Lang.En");
        if (ThemeLightItem is not null)
            ThemeLightItem.Header = Loc.T("Menu.Theme.Light");
        if (ThemeDarkItem is not null)
            ThemeDarkItem.Header = Loc.T("Menu.Theme.Dark");
        if (ThemeCustomItem is not null)
        {
            ThemeCustomItem.Header = Loc.T("Menu.Theme.Custom");
            ThemeCustomItem.ToolTip = Loc.T("Menu.Theme.CustomTip");
            ThemeCustomItem.IsEnabled = false;
        }

        SetText(LayoutCellDashboard, Loc.T("Layout.Dashboard"), Loc.T("Layout.ToggleDashboard"));
        SetText(LayoutCellTestResults, Loc.T("Layout.TestResults"), Loc.T("Layout.ToggleTestResults"));
        SetText(LayoutCellMap, Loc.T("Layout.Map"), Loc.T("Layout.ToggleMap"));
        SetText(LayoutCellChart, Loc.T("Layout.Chart"), Loc.T("Layout.ToggleChart"));
        SetText(LayoutCellReset, Loc.T("Layout.Reset"), Loc.T("Layout.ResetTip"));

        if (LoadVboButton is not null)
        {
            LoadVboButton.Content = Loc.T("Toolbar.Load");
            LoadVboButton.ToolTip = Loc.T("Toolbar.LoadTip");
        }
        if (OnlineModeButton is not null)
        {
            OnlineModeButton.Content = Loc.T("Toolbar.Online");
            OnlineModeButton.ToolTip = Loc.T("Toolbar.OnlineTip");
        }
        if (OfflineModeButton is not null)
        {
            OfflineModeButton.Content = Loc.T("Toolbar.Offline");
            OfflineModeButton.ToolTip = Loc.T("Toolbar.OfflineTip");
        }
        if (ClearOfflineButton is not null)
        {
            ClearOfflineButton.Content = Loc.T("Toolbar.Clear");
            ClearOfflineButton.ToolTip = Loc.T("Toolbar.ClearTip");
        }
        if (UdpSourceButton is not null)
        {
            UdpSourceButton.Content = Loc.T("Toolbar.Udp");
            UdpSourceButton.ToolTip = Loc.T("Toolbar.UdpTip");
        }
        if (GSpotButton is not null)
        {
            GSpotButton.Content = Loc.T("Toolbar.GSpot");
            GSpotButton.ToolTip = Loc.T("Toolbar.GSpotTip");
        }
        if (SimulatorButton is not null)
            SimulatorButton.Content = Loc.T("Toolbar.Simulator");
        if (StopOnlineButton is not null)
        {
            StopOnlineButton.Content = Loc.T("Toolbar.Stop");
            StopOnlineButton.ToolTip = Loc.T("Toolbar.StopTip");
        }
        if (RecordLabelText is not null && RecordButton?.IsEnabled == true)
        {
            // Keep Start/Pause state text owned by record state machine; only refresh Stop.
        }
        if (StopRecordButton is not null)
            StopRecordButton.Content = Loc.T("Toolbar.RecordStop");
        if (ConnectionStatusText is not null)
            ConnectionStatusText.ToolTip = Loc.T("Status.ConnectionTip");
    }

    private static void SetText(Border? cell, string label, string tip)
    {
        if (cell is null)
            return;
        cell.ToolTip = tip;
        if (cell.Child is TextBlock tb)
            tb.Text = label;
    }

    // ============================================================
    // Layout: VBTS-style panel map (Dashboard / Chart / Test Results / Map)
    // ============================================================

    private void LayoutRegion_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tag)
            return;

        e.Handled = true;

        switch (tag)
        {
            case "Dashboard":
                _layoutShowDashboard = !_layoutShowDashboard;
                break;
            case "TestResults":
                _layoutShowTestResults = !_layoutShowTestResults;
                break;
            case "Map":
                _layoutShowMap = !_layoutShowMap;
                break;
            case "Chart":
                _layoutShowChart = !_layoutShowChart;
                break;
            default:
                return;
        }

        ApplyLayoutVisibility();
        SyncLayoutMapVisuals();
    }


    private void LayoutReset_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        _layoutShowDashboard = true;
        _layoutShowTestResults = true;
        _layoutShowMap = true;
        _layoutShowChart = true;

        _layoutDashboardWidth = LayoutDefaultDashboardWidth;
        _layoutTestResultsWidth = LayoutDefaultTestResultsWidth;
        _layoutMapWidth = LayoutDefaultMapWidth;
        _layoutCurvesHeight = LayoutDefaultCurvesHeight;
        _layoutDashboardMin = 280;
        _layoutTestResultsMin = 260;
        _layoutMapMin = 300;
        _layoutCurvesMin = 240;
        _layoutTopPanelsMin = 200;

        if (DashboardColumn is not null)
        {
            DashboardColumn.Width = LayoutDefaultDashboardWidth;
            DashboardColumn.MinWidth = 280;
        }

        if (TestResultsColumn is not null)
        {
            TestResultsColumn.Width = LayoutDefaultTestResultsWidth;
            TestResultsColumn.MinWidth = 260;
        }

        if (MapColumn is not null)
        {
            MapColumn.Width = LayoutDefaultMapWidth;
            MapColumn.MinWidth = 300;
        }

        if (CurvesRow is not null)
        {
            CurvesRow.Height = LayoutDefaultCurvesHeight;
            CurvesRow.MinHeight = 240;
        }

        if (TopPanelsRow is not null)
            TopPanelsRow.Height = LayoutDefaultTopPanelsHeight;

        ApplyLayoutVisibility();
        SyncLayoutMapVisuals();
    }


    private void ApplyLayoutVisibility()
    {
        var dash = _layoutShowDashboard;
        var test = _layoutShowTestResults;
        var map = _layoutShowMap;
        var chart = _layoutShowChart;

        SetColumnPanelVisible(
            DashboardColumn,
            DashboardPanelControl,
            dash,
            ref _layoutDashboardWidth,
            ref _layoutDashboardMin,
            fallbackWidth: LayoutDefaultDashboardWidth,
            fallbackMin: 280);

        SetColumnPanelVisible(
            TestResultsColumn,
            TestResultsPanelControl,
            test,
            ref _layoutTestResultsWidth,
            ref _layoutTestResultsMin,
            fallbackWidth: LayoutDefaultTestResultsWidth,
            fallbackMin: 260);

        SetColumnPanelVisible(
            MapColumn,
            TrackMapPanelControl,
            map,
            ref _layoutMapWidth,
            ref _layoutMapMin,
            fallbackWidth: LayoutDefaultMapWidth,
            fallbackMin: 300);

        if (DashboardSplitter is not null)
        {
            DashboardSplitter.Visibility =
                dash && (test || map)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        if (MapSplitter is not null)
        {
            MapSplitter.Visibility =
                map && (test || dash)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        // Chart (curves row)
        if (CurvesPanelBorder is not null)
            CurvesPanelBorder.Visibility =
                chart ? Visibility.Visible : Visibility.Collapsed;

        var anyTop = dash || test || map;

        if (CurvesRowSplitter is not null)
            CurvesRowSplitter.Visibility =
                chart && anyTop ? Visibility.Visible : Visibility.Collapsed;

        if (CurvesRow is not null)
        {
            if (chart)
            {
                if (_layoutCurvesHeight is GridLength h)
                    CurvesRow.Height = h;
                else
                    CurvesRow.Height = LayoutDefaultCurvesHeight;
                CurvesRow.MinHeight = _layoutCurvesMin > 0 ? _layoutCurvesMin : 240;
            }
            else
            {
                if (CurvesRow.Height.Value > 0 || CurvesRow.Height.IsStar)
                    _layoutCurvesHeight = CurvesRow.Height;
                if (CurvesRow.MinHeight > 0)
                    _layoutCurvesMin = CurvesRow.MinHeight;
                CurvesRow.MinHeight = 0;
                CurvesRow.Height = new GridLength(0);
            }
        }

        // Allow all top panels closed so Chart can fill the workspace alone.
        if (TopPanelsRow is not null)
        {
            if (!anyTop)
            {
                if (TopPanelsRow.MinHeight > 0)
                    _layoutTopPanelsMin = TopPanelsRow.MinHeight;
                TopPanelsRow.MinHeight = 0;
                TopPanelsRow.Height = new GridLength(0);
            }
            else
            {
                TopPanelsRow.MinHeight = _layoutTopPanelsMin > 0 ? _layoutTopPanelsMin : 200;
                TopPanelsRow.Height = LayoutDefaultTopPanelsHeight;
            }
        }
    }


    private void SyncLayoutMapVisuals()
    {
        SetLayoutRegionVisual(LayoutCellDashboard, _layoutShowDashboard);
        SetLayoutRegionVisual(LayoutCellTestResults, _layoutShowTestResults);
        SetLayoutRegionVisual(LayoutCellMap, _layoutShowMap);
        SetLayoutRegionVisual(LayoutCellChart, _layoutShowChart);
    }


    private static void SetLayoutRegionVisual(Border? cell, bool active)
    {
        if (cell is null)
            return;

        if (active)
        {
            cell.Background = LayoutRegionActiveBg;
            cell.BorderBrush = LayoutRegionActiveBorder;
            cell.BorderThickness = new Thickness(1.5);
            cell.Opacity = 1.0;
        }
        else
        {
            cell.Background = LayoutRegionInactiveBg;
            cell.BorderBrush = LayoutRegionInactiveBorder;
            cell.BorderThickness = new Thickness(1);
            cell.Opacity = 0.72;
        }

        if (cell.Child is TextBlock label)
        {
            label.Foreground = active
                ? LayoutRegionActiveFg
                : LayoutRegionInactiveFg;
            label.FontWeight = active
                ? FontWeights.SemiBold
                : FontWeights.Normal;
        }
    }


    private static void SetColumnPanelVisible(
        ColumnDefinition? column,
        UIElement? panel,
        bool visible,
        ref GridLength? savedWidth,
        ref double savedMin,
        GridLength fallbackWidth,
        double fallbackMin)
    {
        if (panel is not null)
            panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (column is null)
            return;

        if (visible)
        {
            column.Width = savedWidth ?? fallbackWidth;
            column.MinWidth = savedMin > 0 ? savedMin : fallbackMin;
        }
        else
        {
            if (column.Width.Value > 0 || column.Width.IsStar)
                savedWidth = column.Width;
            if (column.MinWidth > 0)
                savedMin = column.MinWidth;
            column.MinWidth = 0;
            column.Width = new GridLength(0);
        }
    }





    // ============================================================
    // X Axis
    // ============================================================

    /// <summary>
    /// 当前可用通道变化后，把已选但已不存在的通道改成默认。
    /// </summary>
    private void SanitizePlotChannelSelections()
    {
        // Online switches ChannelRegistry to live-core (narrower set), but opened VBOs stay in _offlineFiles.
        // Do not clobber VBO-only plot channel picks to Speed; Offline restore must keep them.
        if (_offlineFiles.Count > 0 && !_isOfflineMode)
            return;

        var fallback = ChannelRegistry.Instance.DefaultPlotChannelId;

        foreach (var plot in _plots)
        {
            foreach (var channel in plot.Channels)
            {
                if (!ChannelRegistry.Instance.IsAvailable(channel.ChannelId) ||
                    string.Equals(channel.ChannelId, ChannelIds.AxisTime, StringComparison.OrdinalIgnoreCase))
                {
                    channel.ChannelId = fallback;
                }
            }
        }
    }


    /// <summary>
    /// 按 ChannelRegistry.Available 重建 X 轴与各 Plot 的 Channel 下拉。
    /// </summary>
    private void RefreshChannelSelectorsFromRegistry()
    {
        SanitizePlotChannelSelections();
        InitializeAxisSelector();
        RefreshPlotContainer();
    }


    private void InitializeAxisSelector()
    {
        var available = ChannelRegistry.Instance.Available.ToList();

        XAxisSelector.DisplayMemberPath = nameof(ChannelInfo.DisplayName);
        XAxisSelector.SelectedValuePath = nameof(ChannelInfo.Id);
        XAxisSelector.ItemsSource = available;

        var selectedId = ChannelIds.AxisTime;
        if (XAxisSelector.SelectedValue is string current &&
            available.Any(c => string.Equals(c.Id, current, StringComparison.OrdinalIgnoreCase)))
        {
            selectedId = current;
        }

        XAxisSelector.SelectedValue = selectedId;
    }


    private void AxisSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
            return;

        RefreshAllPlots(force: true);
        ScheduleSessionMemorySave();
    }


    private void XAxisAutoScaleCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsInitialized || _suppressAutoScaleCheckboxRefresh)
            return;

        // 手动取消 Auto X：立刻锁定当前范围
        if (XAxisAutoScaleCheckBox.IsChecked != true)
            CaptureLockedLimitsFromPlots();

        RefreshAllPlots(force: true);
        ScheduleSessionMemorySave();
    }


    // ============================================================
    // Plot 初始化
    // ============================================================

    private void InitializePlotSystem()
    {
        if (PlotScrollViewer is not null)
            PlotScrollViewer.SizeChanged += PlotScrollViewer_SizeChanged;

        AddPlot();
    }

    private void PlotScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PlotScrollViewer is null)
            return;

        if (PlotScrollViewer.ViewportHeight > 50)
            _curvesViewportHeight = PlotScrollViewer.ViewportHeight;

        if (_restoringSession)
            return;

        if (_plots.Count == 1 && _singlePlotFillsViewport)
            ApplySinglePlotFillHeight(rebuild: false);
    }

    private double GetViewportFillHeight()
    {
        var h = PlotScrollViewer?.ViewportHeight ?? 0;
        if (h < 50)
            h = _curvesViewportHeight;
        if (h < 50)
            h = 280;
        // Margin (6*2) + bottom resize thumb (~6)
        return Math.Max(PlotMinHeightPx, h - 18);
    }

    private void ApplySinglePlotFillHeight(bool rebuild)
    {
        if (_plots.Count != 1)
            return;

        var target = GetViewportFillHeight();
        if (Math.Abs(_plots[0].HeightPx - target) < 1.5)
            return;

        _plots[0].HeightPx = target;
        if (rebuild)
            RefreshPlotContainer();
        else
            ApplyPlotHeightsToRows();
    }

    private void ApplyPlotHeightsToRows()
    {
        if (PlotContainer.RowDefinitions.Count == 0 || _plots.Count == 0)
            return;

        for (var i = 0; i < _plots.Count; i++)
        {
            var row = i * 2;
            if (row >= PlotContainer.RowDefinitions.Count)
                break;
            PlotContainer.RowDefinitions[row].Height =
                new GridLength(Math.Max(PlotMinHeightPx, _plots[i].HeightPx));
        }
    }


    // ============================================================
    // 添加 Plot
    // ============================================================

    private void AddPlotButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        AddPlot();
    }


    private void AddPlot()
    {
        var isFirst = _plots.Count == 0;
        var height = isFirst
            ? GetViewportFillHeight()
            : Math.Max(PlotMinHeightPx, Math.Min(360, GetViewportFillHeight() * 0.55));

        if (!isFirst)
            _singlePlotFillsViewport = false;

        var plot = new PlotDefinition
        {
            Name = $"Plot {_nextPlotNumber++}",
            AutoScaleY = true,
            HeightPx = height
        };

        plot.Channels.Add(
            new ChannelDefinition
            {
                ChannelId = ChannelRegistry.Instance.DefaultPlotChannelId
            });

        _plots.Add(plot);

        RefreshPlotContainer();

        RefreshAllPlots(force: true);
        ScheduleSessionMemorySave();
    }


    // ============================================================
    // 删除 Plot
    // ============================================================

    private void RemovePlot(
        PlotDefinition plot)
    {
        if (!_plots.Contains(plot))
            return;

        _plots.Remove(plot);

        if (ReferenceEquals(_cursorSourcePlot, plot))
        {
            _cursorSourcePlot = null;
            UpdateNumericDisplay();
        }

        if (_plots.Count == 1)
            _singlePlotFillsViewport = false;

        RefreshPlotContainer();

        RefreshAllPlots(force: true);
        ScheduleSessionMemorySave();
    }


    // ============================================================
    // 重建 Plot UI
    // ============================================================

    // ============================================================
    // ScottPlot 深色主题
    //
    // 注意：ScottPlot 5.1.59 里 Plot.Style / PlotStyler 已标记过时，
    // 官方推荐用 Plot.SetStyle(PlotStyle)。这里用非过时的写法。
    // ============================================================

    private void ApplyDarkPlotStyle(
        ScottPlot.WPF.WpfPlot wpfPlot)
    {
        var scottPlot =
            wpfPlot.Plot;

        scottPlot.SetStyle(
            new ScottPlot.PlotStyle
            {
                // 最外层背景（与曲线区卡片一致）
                FigureBackgroundColor =
                    ScottPlot.Color.FromHex("#12171E"),

                // 绘图区背景
                DataBackgroundColor =
                    ScottPlot.Color.FromHex("#0E131A"),

                // 坐标轴 / 刻度文字（DateTimeTicksBottom 会冲掉，见 ApplyAxisLabelColors）
                AxisColor =
                    ScottPlot.Color.FromHex("#C3CBD8"),

                // 网格线
                GridMajorLineColor =
                    ScottPlot.Color.FromHex("#232C38"),

                // 图例
                LegendBackgroundColor =
                    ScottPlot.Color.FromHex("#12171E"),

                LegendFontColor =
                    ScottPlot.Color.FromHex("#C3CBD8"),

                LegendOutlineColor =
                    ScottPlot.Color.FromHex("#1E2530"),

                // 曲线配色
                Palette =
                    new ScottPlot.Palettes.Dark()
            });

        ApplyAxisLabelColors(scottPlot);
    }


    /// <summary>
    /// DateTimeTicksBottom() 会把 TickLabelStyle.ForeColor 重置为黑色，
    /// 所以每次套深色主题 / 切时间轴后都要显式刷一遍浅色刻度。
    /// </summary>
    private static void ApplyAxisLabelColors(ScottPlot.Plot scottPlot)
    {
        var tickColor =
            ScottPlot.Color.FromHex("#C3CBD8");

        var axisLabelColor =
            ScottPlot.Color.FromHex("#8A94A6");

        foreach (var axis in scottPlot.Axes.GetAxes())
        {
            axis.TickLabelStyle.ForeColor = tickColor;
            axis.Label.ForeColor = axisLabelColor;
        }
    }


    private void RefreshPlotContainer()
    {
        // Keep HeightPx as source of truth; sync from live rows if user dragged.
        CapturePlotRowHeightsFromContainer();

        PlotContainer.Children.Clear();
        PlotContainer.RowDefinitions.Clear();
        PlotContainer.ColumnDefinitions.Clear();

        if (_plots.Count == 0)
            return;

        if (_plots.Count == 1 && _singlePlotFillsViewport)
            _plots[0].HeightPx = GetViewportFillHeight();

        for (var i = 0; i < _plots.Count; i++)
        {
            var heightPx = Math.Max(PlotMinHeightPx, _plots[i].HeightPx);
            _plots[i].HeightPx = heightPx;

            PlotContainer.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = new GridLength(heightPx),
                    MinHeight = PlotMinHeightPx
                });

            var plotControl = CreatePlotControl(_plots[i]);
            Grid.SetRow(plotControl, PlotContainer.RowDefinitions.Count - 1);
            PlotContainer.Children.Add(plotControl);

            // Resize thumb under every plot (including the last) — grows/shrinks that plot only.
            PlotContainer.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            var thumb = CreatePlotHeightThumb(_plots[i]);
            Grid.SetRow(thumb, PlotContainer.RowDefinitions.Count - 1);
            PlotContainer.Children.Add(thumb);
        }
    }

    private FrameworkElement CreatePlotHeightThumb(PlotDefinition plot)
    {
        var thumb = new Border
        {
            Height = 6,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x40)),
            Cursor = Cursors.SizeNS,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 2),
            ToolTip = "Drag to resize plot height"
        };

        double startY = 0;
        double startH = 0;
        var dragging = false;

        thumb.MouseLeftButtonDown += (_, e) =>
        {
            startY = e.GetPosition(PlotContainer).Y;
            startH = plot.HeightPx;
            dragging = true;
            thumb.CaptureMouse();
            e.Handled = true;
        };

        thumb.MouseMove += (_, e) =>
        {
            if (!dragging || !thumb.IsMouseCaptured)
                return;

            var dy = e.GetPosition(PlotContainer).Y - startY;
            var next = Math.Max(PlotMinHeightPx, startH + dy);
            if (Math.Abs(next - plot.HeightPx) < 0.5)
                return;

            plot.HeightPx = next;
            _singlePlotFillsViewport = false;
            ApplyPlotHeightsToRows();
        };

        thumb.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging)
                return;
            dragging = false;
            if (thumb.IsMouseCaptured)
                thumb.ReleaseMouseCapture();
            ScheduleSessionMemorySave();
            e.Handled = true;
        };

        thumb.LostMouseCapture += (_, _) => { dragging = false; };

        return thumb;
    }

    /// <summary>
    /// Persist each plot row height from the live Grid before rebuild.
    /// Layout: plot0, thumb, plot1, thumb, ... => plot i at row i*2.
    /// </summary>
    private void CapturePlotRowHeightsFromContainer()
    {
        if (PlotContainer.RowDefinitions.Count == 0 || _plots.Count == 0)
            return;

        for (var i = 0; i < _plots.Count; i++)
        {
            var row = i * 2;
            if (row >= PlotContainer.RowDefinitions.Count)
                break;

            var def = PlotContainer.RowDefinitions[row];
            if (def.ActualHeight > 1)
                _plots[i].HeightPx = def.ActualHeight;
            else if (def.Height.IsAbsolute && def.Height.Value > 0)
                _plots[i].HeightPx = def.Height.Value;
        }
    }


    // ============================================================
    // 创建单个 Plot UI
    // ============================================================

    private FrameworkElement CreatePlotControl(
        PlotDefinition plot)
    {
        var outerBorder = new Border
        {
            Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        0x0E,
                        0x13,
                        0x1A)),

            BorderBrush =
                new SolidColorBrush(
                    Color.FromRgb(
                        0x1E,
                        0x25,
                        0x30)),

            BorderThickness =
                new Thickness(1),

            CornerRadius =
                new CornerRadius(4),

            Margin =
                new Thickness(0, 0, 0, 0),

            Padding =
                new Thickness(8),

            // Stretch within fixed-pixel plot row (height set by outer Grid + drag thumb).
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };


        var root = new Grid();


        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });


        root.RowDefinitions.Add(
            new RowDefinition
            {
                // Stretch with parent plot row height.
                Height = new GridLength(1, GridUnitType.Star),
                MinHeight = 80
            });


        // ========================================================
        // Plot Header: name / Auto Y / + Channel / channels (wrap) / Remove
        // ========================================================

        var header = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var removePlotButton = new Button
        {
            Content = "Remove Plot",
            Height = 26,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        removePlotButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "DangerButtonStyle");
        removePlotButton.Click += (_, _) => RemovePlot(plot);
        DockPanel.SetDock(removePlotButton, Dock.Right);
        header.Children.Add(removePlotButton);

        // Wrap: Plot Name | name box | Auto Y | + Channel | channel chips...
        var chrome = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var nameLabel = new TextBlock
        {
            Text = "Plot Name",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 4)
        };
        chrome.Children.Add(nameLabel);

        var nameTextBox = new TextBox
        {
            Text = plot.Name,
            Width = 160,
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 4)
        };
        nameTextBox.SetResourceReference(
            FrameworkElement.StyleProperty,
            "DarkTextBoxStyle");
        nameTextBox.TextChanged += (_, _) =>
        {
            plot.Name = nameTextBox.Text;
            RefreshPlotTitle(plot);
            ScheduleSessionMemorySave();
        };
        chrome.Children.Add(nameTextBox);

        var autoScaleCheckBox = new CheckBox
        {
            Content = "Auto Y",
            IsChecked = plot.AutoScaleY,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 4)
        };
        plot.AutoScaleYCheckBox = autoScaleCheckBox;
        autoScaleCheckBox.Checked += (_, _) =>
        {
            plot.AutoScaleY = true;
            if (!_suppressAutoScaleCheckboxRefresh)
                RefreshPlot(plot);
            ScheduleSessionMemorySave();
        };
        autoScaleCheckBox.Unchecked += (_, _) =>
        {
            plot.AutoScaleY = false;
            if (plot.WpfPlot is not null)
                plot.LockedLimits = plot.WpfPlot.Plot.Axes.GetLimits();
            ScheduleSessionMemorySave();
        };
        chrome.Children.Add(autoScaleCheckBox);

        var addChannelButton = new Button
        {
            Content = "+ Channel",
            Height = 26,
            Margin = new Thickness(0, 0, 8, 4)
        };
        addChannelButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ToolButtonStyle");
        addChannelButton.Click += (_, _) => AddChannel(plot);
        chrome.Children.Add(addChannelButton);

        foreach (var channel in plot.Channels)
            chrome.Children.Add(CreateChannelControl(plot, channel));

        header.Children.Add(chrome);

        Grid.SetRow(header, 0);
        root.Children.Add(header);


        // ========================================================
        // ScottPlot
        // ========================================================

        var wpfPlot =
            new ScottPlot.WPF.WpfPlot();

        // Fill the plot row; height comes from outer Grid + drag thumb.
        wpfPlot.MinHeight = 80;
        wpfPlot.VerticalAlignment = VerticalAlignment.Stretch;
        wpfPlot.HorizontalAlignment = HorizontalAlignment.Stretch;

        ApplyDarkPlotStyle(wpfPlot);

        plot.WpfPlot = wpfPlot;
        wpfPlot.Focusable = true;

        AttachPlotInteraction(plot, wpfPlot);


        Grid.SetRow(
            wpfPlot,
            1);

        root.Children.Add(
            wpfPlot);


        outerBorder.Child = root;

        return outerBorder;
    }


    // ============================================================
    // 创建 Channel UI
    // ============================================================

    private FrameworkElement CreateChannelControl(
        PlotDefinition plot,
        ChannelDefinition channel)
    {
        var chip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 4)
        };

        var comboBox = new ComboBox
        {
            DisplayMemberPath = nameof(ChannelInfo.DisplayName),
            SelectedValuePath = nameof(ChannelInfo.Id),
            ItemsSource = ChannelRegistry.Instance.AvailablePlotChannels,
            SelectedValue = channel.ChannelId,
            Width = 170,
            Height = 28,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center
        };

        if (comboBox.SelectedValue is null)
        {
            // Channel not in current ItemsSource (e.g. Online live-core while VBO picks preserved).
            // Keep the model ChannelId and temporarily inject the orphan so the combo can display it.
            if (!string.IsNullOrWhiteSpace(channel.ChannelId) &&
                ChannelRegistry.Instance.TryGet(channel.ChannelId, out var orphanInfo))
            {
                var list = ChannelRegistry.Instance.AvailablePlotChannels.ToList();
                if (!list.Any(c => string.Equals(c.Id, orphanInfo.Id, StringComparison.OrdinalIgnoreCase)))
                    list.Insert(0, orphanInfo);
                comboBox.ItemsSource = list;
                comboBox.SelectedValue = channel.ChannelId;
            }
            else
            {
                channel.ChannelId = ChannelRegistry.Instance.DefaultPlotChannelId;
                comboBox.SelectedValue = channel.ChannelId;
            }
        }

        var unitText = new TextBlock
        {
            Text = GetSignalUnit(channel.ChannelId),
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 4, 0),
            MinWidth = 36
        };

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedValue is string channelId)
            {
                channel.ChannelId = channelId;
                unitText.Text = GetSignalUnit(channelId);
                RefreshPlot(plot);
                ScheduleSessionMemorySave();
            }
        };

        chip.Children.Add(comboBox);
        chip.Children.Add(unitText);

        var removeButton = new Button
        {
            Content = "x",
            Width = 26,
            Height = 26,
            FontSize = 12,
            Padding = new Thickness(0),
            ToolTip = "Remove channel"
        };
        removeButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "DangerButtonStyle");
        removeButton.Click += (_, _) => RemoveChannel(plot, channel);
        chip.Children.Add(removeButton);

        return chip;
    }


    // ============================================================

    private void AddChannel(
        PlotDefinition plot)
    {
        plot.Channels.Add(
            new ChannelDefinition
            {
                ChannelId =
                    ChannelRegistry.Instance.DefaultPlotChannelId
            });

        RefreshPlotContainer();

        RefreshAllPlots(force: true);
        ScheduleSessionMemorySave();
    }


    // ============================================================
    // 删除 Channel
    // ============================================================

    private void RemoveChannel(
        PlotDefinition plot,
        ChannelDefinition channel)
    {
        if (!plot.Channels.Contains(channel))
            return;

        plot.Channels.Remove(channel);

        RefreshPlotContainer();

        RefreshAllPlots(force: true);
        ScheduleSessionMemorySave();
    }


    // ============================================================
    // 刷新所有 Plot
    // ============================================================

    private void RefreshAllPlots(bool force = false)
    {
        if (_isRefreshingPlots)
            return;

        _isRefreshingPlots = true;

        try
        {
            // 整拍只 Snapshot 一次，所有 Plot + Track Map 共用。
            var history = _sampleHistory.Snapshot();

            // 手动平移/缩放时不要 Clear+重建，否则像「缩放被锁定」
            if (_activeManualAxisGestures == 0)
            {
                var rebuild =
                    force
                    || _forcePlotRebuild
                    || IsPlotHistoryDirty(history);

                if (rebuild)
                {
                    foreach (var plot in _plots)
                    {
                        RefreshPlot(plot, history);
                    }

                    RememberPlotHistoryFingerprint(history);
                    _forcePlotRebuild = false;
                }
            }
            else if (force)
            {
                // 手势进行中来的强制请求延后到松手后下一拍
                _forcePlotRebuild = true;
            }

            // 轨迹图：线下多文件叠轨；线上单轨（Snapshot）。
            RefreshTrackMap(history);
        }
        finally
        {
            _isRefreshingPlots = false;
        }
    }


    private bool IsPlotHistoryDirty(
        IReadOnlyList<VehicleSample> history)
    {
        if (history.Count != _lastPlotHistoryCount)
            return true;

        if (history.Count == 0)
            return _lastPlotHistoryCount != 0;

        var last = history[^1];
        return last.Timestamp != _lastPlotHistoryTimestamp
            || last.Sequence != _lastPlotHistorySequence;
    }


    private void RememberPlotHistoryFingerprint(
        IReadOnlyList<VehicleSample> history)
    {
        _lastPlotHistoryCount = history.Count;
        if (history.Count == 0)
        {
            _lastPlotHistoryTimestamp = 0;
            _lastPlotHistorySequence = 0;
            return;
        }

        var last = history[^1];
        _lastPlotHistoryTimestamp = last.Timestamp;
        _lastPlotHistorySequence = last.Sequence;
    }


    private void RefreshTrackMap(
        IReadOnlyList<VehicleSample>? liveSnapshot = null)
    {
        if (_isOfflineMode && _offlineFiles.Count > 0)
        {
            var layers = _offlineFiles.Files
                .Select(f => new Controls.TrackMapPanel.TrackLayer(
                    f.DisplayName,
                    f.ColorHex,
                    f.Samples))
                .ToList();
            TrackMapPanelControl.SetTracks(layers);
            TrackMapPanelControl.SetRunHighlights(
                _annotatedRuns,
                _selectedAnnotatedRun);
            return;
        }

        TrackMapPanelControl.SetTrack(
            liveSnapshot ?? _sampleHistory.Snapshot());
        TrackMapPanelControl.SetRunHighlights(
            _annotatedRuns,
            _selectedAnnotatedRun);
    }


    // ============================================================
    // 刷新单个 Plot
    // ============================================================

    private void RefreshPlot(
        PlotDefinition plot)
    {
        RefreshPlot(plot, GetHistorySnapshot());
    }


    private void RefreshPlot(
        PlotDefinition plot,
        IReadOnlyList<VehicleSample> history)
    {
        if (plot.WpfPlot is null)
            return;


        var selectedXSignal =
            XAxisSelector.SelectedValue
                is string xSignal
                ? xSignal
                : ChannelIds.AxisTime;


        var selectedChannels =
            plot.Channels.ToList();


        var scottPlot =
            plot.WpfPlot.Plot;


        // 记住用户拖动/缩放后的轴范围（非 Auto 时）。
        // 优先用 LockedLimits：避免上一帧 DateTimeTicksBottom 把 GetLimits 冲成数据范围。
        var previousLimits =
            plot.LockedLimits ?? scottPlot.Axes.GetLimits();


        scottPlot.Clear();


        plot.LastXs = null;
        plot.LastSamples = null;
        plot.LastChannelSeries.Clear();
        plot.IsTimeAxis = string.Equals(selectedXSignal, ChannelIds.AxisTime, StringComparison.OrdinalIgnoreCase);


        // ========================================================
        // 数据不足
        // ========================================================


        // Multi-run compare: X = seconds from each run start (checked rows).
        if (IsCompareModeActive)
        {
            DrawCompareOverlay(plot, scottPlot, selectedChannels, previousLimits);
            plot.WpfPlot.Refresh();
            return;
        }

        // Leaving / not in compare: drop any edge legend panels from a prior compare view.
        ClearCompareEdgeLegend(scottPlot);

        if (history.Count < 2)
        {
            ApplyDarkPlotStyle(plot.WpfPlot);
            ApplyPlotOverlays(plot);
            RefreshPlotTitle(plot);
            plot.WpfPlot.Refresh();
            return;
        }


        // ========================================================
        // 没有 Channel
        // ========================================================

        if (selectedChannels.Count == 0)
        {
            scottPlot.Title(
                $"{plot.Name} - No Channel");

            scottPlot.XLabel(
                GetAxisLabel(selectedXSignal));

            scottPlot.YLabel(
                "Value");

            ApplyDarkPlotStyle(plot.WpfPlot);
            ApplyDateTimeAxisIfNeeded(plot, selectedXSignal);
            ApplyPlotOverlays(plot);
            plot.WpfPlot.Refresh();
            return;
        }




        // ========================================================
        // 可见窗口 + 降采样下标（各通道共用，保证 LastXs / LastSamples 对齐）
        // ========================================================

        var autoX =
            XAxisAutoScaleCheckBox.IsChecked == true;

        double GetXAt(int index) =>
            GetAxisValue(history[index], selectedXSignal);

        var (visStart, visEnd) =
            PlotDownsampler.FindVisibleIndexRange(
                history.Count,
                GetXAt,
                previousLimits.Left,
                previousLimits.Right,
                useFullRange: autoX);

        if (visEnd - visStart < 2)
        {
            visStart = 0;
            visEnd = history.Count;
        }

        // min-max 按首通道 Y 选点，保留尖峰；其余通道复用同一批下标
        var primaryChannelId = selectedChannels[0].ChannelId;
        double GetPrimaryY(int index) =>
            GetSignalValue(history[index], primaryChannelId);

        var indices =
            PlotDownsampler.BuildDownsampleIndices(
                visStart,
                visEnd,
                GetPrimaryY,
                PlotDownsampler.MaxPlotPoints);

        if (indices.Length < 2)
        {
            ApplyDarkPlotStyle(plot.WpfPlot);
            ApplyPlotOverlays(plot);
            RefreshPlotTitle(plot);
            plot.WpfPlot.Refresh();
            return;
        }

        var xs =
            PlotDownsampler.ExtractXs(
                history,
                indices,
                sample => GetAxisValue(sample, selectedXSignal));



        // Channel 曲线
        // ========================================================

        double minY =
            double.MaxValue;

        double maxY =
            double.MinValue;


        // 线下多文件：同一通道叠多条曲线（按文件着色）
        // Offline multi-file: ALWAYS overlay every open file (chip click = focus, not hide).
        // X policy: if all files' time windows overlap within 2h, use absolute Beijing time;
        // otherwise use elapsed seconds from each file's first sample so curves superimpose.
        if (_isOfflineMode && _offlineFiles.Count > 1)
        {
            var focusId = _offlineFiles.Selected?.Id;
            var useElapsedTime = false;
            var isTimeAxis = string.Equals(selectedXSignal, ChannelIds.AxisTime, StringComparison.OrdinalIgnoreCase);
            if (isTimeAxis)
            {
                long globalMin = long.MaxValue, globalMax = long.MinValue;
                foreach (var file in _offlineFiles.Files)
                {
                    if (file.Samples.Count < 2) continue;
                    var t0 = file.Samples[0].Timestamp;
                    var t1 = file.Samples[^1].Timestamp;
                    if (t0 > t1) (t0, t1) = (t1, t0);
                    if (t0 < globalMin) globalMin = t0;
                    if (t1 > globalMax) globalMax = t1;
                }
                // Different days / far-apart sessions → elapsed overlay
                useElapsedTime = globalMax > globalMin && (globalMax - globalMin) > 2L * 60 * 60 * 1000;
            }

            double minX = double.MaxValue, maxX = double.MinValue;
            var allXsForLimits = new List<double>();

            foreach (var channel in selectedChannels)
            {
                foreach (var file in _offlineFiles.Files)
                {
                    if (file.Samples.Count < 2)
                        continue;

                    var n = file.Samples.Count;
                    var fileXs = new double[n];
                    var fileYs = new double[n];
                    var tOrigin = file.Samples[0].Timestamp;

                    for (var i = 0; i < n; i++)
                    {
                        if (useElapsedTime)
                            fileXs[i] = (file.Samples[i].Timestamp - tOrigin) / 1000.0;
                        else
                            fileXs[i] = GetAxisValue(file.Samples[i], selectedXSignal);

                        var value = GetSignalValue(file.Samples[i], channel.ChannelId);
                        fileYs[i] = value;
                        if (!double.IsNaN(value) && !double.IsInfinity(value))
                        {
                            if (value < minY) minY = value;
                            if (value > maxY) maxY = value;
                        }
                        if (fileXs[i] < minX) minX = fileXs[i];
                        if (fileXs[i] > maxX) maxX = fileXs[i];
                        allXsForLimits.Add(fileXs[i]);
                    }

                    var focused = focusId is Guid fid && file.Id == fid;
                    var scatter = scottPlot.Add.Scatter(fileXs, fileYs);
                    scatter.Color = ScottPlot.Color.FromHex(file.ColorHex);
                    scatter.LegendText =
                        $"{GetSignalDisplayName(channel.ChannelId)} · {file.DisplayName}";
                    scatter.LineWidth = focused ? 2.8f : 1.2f;
                    scatter.MarkerSize = 0;
                    if (!focused)
                        scatter.Color = ScottPlot.Color.FromHex(file.ColorHex).WithAlpha(0.55);
                }
            }

            // Cursor / Dashboard track focused file; X matches what was plotted.
            if (useElapsedTime && history.Count >= 2)
            {
                var t0 = history[0].Timestamp;
                plot.LastXs = new double[indices.Length];
                for (var ii = 0; ii < indices.Length; ii++)
                    plot.LastXs[ii] = (history[indices[ii]].Timestamp - t0) / 1000.0;
            }
            else
            {
                plot.LastXs = xs;
            }
            plot.LastSamples = PlotDownsampler.ExtractSamples(history, indices);
            plot.LastChannelSeries.Clear();
            foreach (var channel in selectedChannels)
            {
                var ys = PlotDownsampler.ExtractYs(
                    history, indices, sample => GetSignalValue(sample, channel.ChannelId));
                plot.LastChannelSeries.Add((channel.ChannelId, ys));
            }

            // Axis label + datetime ticks
            if (useElapsedTime)
            {
                plot.IsTimeAxis = false;
                scottPlot.Title(plot.Name);
                scottPlot.XLabel("Elapsed (s) — multi-file overlay");
                scottPlot.YLabel("Value");
                scottPlot.Legend.IsVisible = true;
                ApplyDarkPlotStyle(plot.WpfPlot);
                // No DateTime axis when using elapsed overlay
                var limitXs = allXsForLimits.Count > 0 ? allXsForLimits.ToArray() : xs;
                ApplyAxisLimitsAfterRebuild(plot, scottPlot, limitXs, minY, maxY, previousLimits);
                ApplyPlotOverlays(plot);
                plot.WpfPlot.Refresh();
                return;
            }
            else
            {
                // Absolute time: limits must span ALL files, not just primary xs
                var limitXs = allXsForLimits.Count > 0 ? allXsForLimits.ToArray() : xs;
                // Fall through to shared title/limits below using limitXs — stash on plot via local replace of xs
                xs = limitXs;
            }
        }
        else
        {
            foreach (var channel in selectedChannels)
            {
                var ys =
                    PlotDownsampler.ExtractYs(
                        history,
                        indices,
                        sample => GetSignalValue(sample, channel.ChannelId));

                for (var i = 0; i < ys.Length; i++)
                {
                    var value = ys[i];

                    if (value < minY)
                        minY = value;

                    if (value > maxY)
                        maxY = value;
                }

                var scatter =
                    scottPlot.Add.Scatter(
                        xs,
                        ys);

                scatter.LegendText =
                    $"{GetSignalDisplayName(channel.ChannelId)} " +
                    $"({GetSignalUnit(channel.ChannelId)})";

                scatter.LineWidth = 1;
                scatter.MarkerSize = 0;

                plot.LastChannelSeries.Add(
                    (channel.ChannelId, ys));
            }

            plot.LastXs = xs;
            // 与降采样后的 LastXs 对齐，供光标 / Dashboard 冻结；非全量历史。
            plot.LastSamples =
                PlotDownsampler.ExtractSamples(history, indices);
        }


        // ========================================================
        // 标题 / 坐标轴
        // ========================================================

        scottPlot.Title(
            plot.Name);


        scottPlot.XLabel(
            GetAxisLabel(selectedXSignal));


        scottPlot.YLabel(
            "Value");


        scottPlot.Legend.IsVisible = true;


        // ScottPlot 5 的 Clear() 会把样式复位，
        // 所以每次重建曲线后都重新套一遍深色主题。
        //
        // 重要：DateTimeTicksBottom() 会替换 Bottom 轴并按数据 AutoScale，
        // 必须先装时间轴，再 SetLimits；否则 10Hz 刷新会把手动平移/缩放冲掉。
        ApplyDarkPlotStyle(
            plot.WpfPlot);

        ApplyDateTimeAxisIfNeeded(
            plot,
            selectedXSignal);

        ApplyAxisLimitsAfterRebuild(
            plot,
            scottPlot,
            xs,
            minY,
            maxY,
            previousLimits);

        ApplyPlotOverlays(plot);


        plot.WpfPlot.Refresh();
    }



    // ============================================================
    // 更新 Plot 标题
    // ============================================================

    private void RefreshPlotTitle(
        PlotDefinition plot)
    {
        if (plot.WpfPlot is null)
            return;

        plot.WpfPlot.Plot.Title(
            plot.Name);

        plot.WpfPlot.Refresh();
    }


    // ============================================================
    // 曲线交互：北京时间轴 / 光标 / 横向选区 / 左上角读数
    // ============================================================

    /// <summary>
    /// Keep the yellow scrubber on the same X across every plot.
    /// Clicking / dragging on one plot updates CursorX + overlays on all.
    /// </summary>
    private void SetSharedCursor(
        double? cursorX,
        PlotDefinition? source,
        bool refreshOverlays = true)
    {
        _cursorSourcePlot = cursorX is null ? null : source;

        foreach (var plot in _plots)
        {
            plot.CursorX = cursorX;

            if (!refreshOverlays || plot.WpfPlot is null)
                continue;

            ApplyPlotOverlays(plot);
            plot.WpfPlot.Refresh();
        }
    }


    private void AttachPlotInteraction(
        PlotDefinition plot,
        ScottPlot.WPF.WpfPlot wpfPlot)
    {
        ConfigurePlotMouseBindings(wpfPlot);

        wpfPlot.PreviewMouseWheel += (_, e) =>
        {
            // Default: scroll the plot list. Zoom only after the plot is clicked/focused.
            if (!wpfPlot.IsKeyboardFocused)
            {
                if (PlotScrollViewer is not null)
                {
                    var next = PlotScrollViewer.VerticalOffset - e.Delta;
                    if (next < 0)
                        next = 0;
                    else if (next > PlotScrollViewer.ScrollableHeight)
                        next = PlotScrollViewer.ScrollableHeight;

                    PlotScrollViewer.ScrollToVerticalOffset(next);
                }

                e.Handled = true;
                return;
            }

            // Focused plot: apply ScottPlot-style wheel zoom around the cursor.
            SuspendAutoScaleForUserInteraction(plot);

            var pixel = wpfPlot.GetPlotPixelPosition(e);
            const double zoomFraction = 0.15;
            var zoomIn = 1 + zoomFraction;
            var zoomOut = 1 / zoomIn;
            var frac = e.Delta > 0 ? zoomIn : zoomOut;
            ScottPlot.Interactivity.MouseAxisManipulation.MouseWheelZoom(
                wpfPlot.Plot,
                frac,
                frac,
                pixel,
                ChangeOpposingAxesTogether: false);

            var limits = wpfPlot.Plot.Axes.GetLimits();
            if (!double.IsInfinity(limits.Left) &&
                !double.IsInfinity(limits.Right) &&
                !double.IsNaN(limits.Left) &&
                !double.IsNaN(limits.Right) &&
                limits.Right > limits.Left)
            {
                plot.LockedLimits = limits;
            }

            wpfPlot.Refresh();
            e.Handled = true;
        };

        wpfPlot.MouseDown += (_, e) =>
        {
            Keyboard.Focus(wpfPlot);

            if (e.ChangedButton == MouseButton.Middle ||
                e.ChangedButton == MouseButton.Right)
            {
                // 立刻取消 Auto，并在手势期间跳过 RefreshPlot
                SuspendAutoScaleForUserInteraction(plot);
                BeginManualAxisGesture();
                return;
            }

            if (e.ChangedButton != MouseButton.Left)
                return;


            var x = GetPlotMouseX(wpfPlot, e);
            if (x is null)
                return;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                plot.IsSelectingRange = true;
                plot.IsDraggingCursor = false;
                plot.SelectionX1 = x;
                plot.SelectionX2 = x;
                wpfPlot.UserInputProcessor.Disable();
                ApplyPlotOverlays(plot);
                wpfPlot.Refresh();
                e.Handled = true;
            }
            else
            {
                plot.IsDraggingCursor = true;
                // Shared yellow scrubber: same X on every plot.
                SetSharedCursor(x, plot);
                // 左键专用于光标，避免与其它左键交互抢事件
                e.Handled = true;
            }

            UpdateNumericDisplay();
        };

        wpfPlot.MouseMove += (_, e) =>
        {
            if (plot.IsDraggingCursor &&
                e.LeftButton == MouseButtonState.Pressed)
            {
                var cx = GetPlotMouseX(wpfPlot, e);
                if (cx is null)
                    return;

                SetSharedCursor(cx, plot);
                UpdateNumericDisplay();
                e.Handled = true;
                return;
            }

            if (!plot.IsSelectingRange ||
                e.LeftButton != MouseButtonState.Pressed)
                return;

            var x = GetPlotMouseX(wpfPlot, e);
            if (x is null)
                return;

            plot.SelectionX2 = x;
            ApplyPlotOverlays(plot);
            wpfPlot.Refresh();
            e.Handled = true;
        };

        wpfPlot.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle ||
                e.ChangedButton == MouseButton.Right)
            {
                EndManualAxisGesture();
                return;
            }

            if (e.ChangedButton != MouseButton.Left)
                return;

            if (plot.IsDraggingCursor)
            {
                plot.IsDraggingCursor = false;
                var cx = GetPlotMouseX(wpfPlot, e);
                if (cx is not null)
                    SetSharedCursor(cx, plot);

                UpdateNumericDisplay();
                e.Handled = true;
                return;
            }

            if (!plot.IsSelectingRange)
                return;

            var x = GetPlotMouseX(wpfPlot, e);
            if (x is not null)
                plot.SelectionX2 = x;

            plot.IsSelectingRange = false;
            wpfPlot.UserInputProcessor.Enable();

            // 选区过窄则视为点击，清除高亮
            if (plot.SelectionX1 is double a &&
                plot.SelectionX2 is double b &&
                Math.Abs(b - a) < 1e-12)
            {
                plot.SelectionX1 = null;
                plot.SelectionX2 = null;
            }


            UpdateSelectionMeasure(plot);
            // 松手后把光标放到选区终点（或点击位置），并同步到所有 Plot
            if (plot.SelectionX2 is double endX)
                SetSharedCursor(endX, plot);
            else
            {
                ApplyPlotOverlays(plot);
                wpfPlot.Refresh();
            }

            UpdateNumericDisplay();
        };

        wpfPlot.MouseLeave += (_, _) =>
        {
            // 拖出控件时也结束手势，避免计数卡死导致曲线永不刷新
            if (_activeManualAxisGestures > 0)
                EndManualAxisGesture();

            if (plot.IsDraggingCursor)
            {
                plot.IsDraggingCursor = false;
            }

            if (!plot.IsSelectingRange)
                return;

            plot.IsSelectingRange = false;
            wpfPlot.UserInputProcessor.Enable();
        };

        wpfPlot.KeyDown += (_, e) =>
        {
            if (e.Key == Key.C &&
                Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                CopyMeasureToClipboard();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Escape)
                return;

            plot.SelectionX1 = null;
            plot.SelectionX2 = null;
            _lastMeasureText = null;
            plot.IsSelectingRange = false;
            plot.IsDraggingCursor = false;
            wpfPlot.UserInputProcessor.Enable();
            SetSharedCursor(null, null);
            UpdateNumericDisplay();
        };
    }


    /// <summary>
    /// 左键 = 竖线光标；中键 = 平移；右键 = 缩放（保持 ScottPlot 默认右键拖拽缩放）。
    /// 关掉默认「中键单击自动缩放 / 中键拖拽缩放矩形」，否则拖动像被锁死。
    /// </summary>
    private static void ConfigurePlotMouseBindings(
        ScottPlot.WPF.WpfPlot wpfPlot)
    {
        var processor = wpfPlot.UserInputProcessor;

        // 先清掉左键平移（会 RemoveAll MouseDragPan）
        processor.LeftClickDragPan(enable: false);

        // 中键默认：单击 Autoscale、拖拽 ZoomRectangle —— 正是「缩放被锁定」的来源
        processor.RemoveAll<ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle>();
        processor.RemoveAll<ScottPlot.Interactivity.UserActionResponses.SingleClickAutoscale>();
        // Wheel zoom is handled manually only when the plot has keyboard focus
        processor.RemoveAll<ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom>();
        processor.DoubleLeftClickBenchmark(false);

        // 中键拖拽 = 平移
        processor.UserActionResponses.Add(
            new ScottPlot.Interactivity.UserActionResponses.MouseDragPan(
                ScottPlot.Interactivity.StandardMouseButtons.Middle));

        // 右键拖拽 = 缩放（与原先默认一致）
        processor.RightClickDragZoom(enable: true);
    }


    private void BeginManualAxisGesture()
    {
        _activeManualAxisGestures++;
    }


    private void EndManualAxisGesture()
    {
        if (_activeManualAxisGestures > 0)
            _activeManualAxisGestures--;

        // 手势结束立刻锁定当前轴范围，避免下一帧 Refresh 读到被冲掉的 GetLimits
        if (_activeManualAxisGestures == 0)
        {
            CaptureLockedLimitsFromPlots();
            // 视野变了但样本指纹可能未变：强制下一拍按新可见窗降采样
            _forcePlotRebuild = true;
        }
    }


    private void CaptureLockedLimitsFromPlots()
    {
        foreach (var plot in _plots)
        {
            if (plot.WpfPlot is null)
                continue;

            var limits = plot.WpfPlot.Plot.Axes.GetLimits();
            if (double.IsInfinity(limits.Left) ||
                double.IsInfinity(limits.Right) ||
                double.IsNaN(limits.Left) ||
                double.IsNaN(limits.Right) ||
                limits.Right <= limits.Left)
            {
                continue;
            }

            plot.LockedLimits = limits;
        }
    }


    /// <summary>
    /// 用户手动平移/缩放时关掉 Auto X / Auto Y；
    /// 只有再次勾选才会恢复自动缩放。
    /// </summary>
    private void SuspendAutoScaleForUserInteraction(
        PlotDefinition plot)
    {
        _suppressAutoScaleCheckboxRefresh = true;
        try
        {
            if (XAxisAutoScaleCheckBox.IsChecked == true)
                XAxisAutoScaleCheckBox.IsChecked = false;

            if (plot.AutoScaleY)
            {
                plot.AutoScaleY = false;
                if (plot.AutoScaleYCheckBox is not null)
                    plot.AutoScaleYCheckBox.IsChecked = false;
            }
        }
        finally
        {
            _suppressAutoScaleCheckboxRefresh = false;
        }

        // 取消 Auto 的瞬间锁定当前视图，防止下一帧按数据重算
        if (plot.WpfPlot is not null)
        {
            var limits = plot.WpfPlot.Plot.Axes.GetLimits();
            if (!(double.IsInfinity(limits.Left) ||
                  double.IsInfinity(limits.Right) ||
                  double.IsNaN(limits.Left) ||
                  double.IsNaN(limits.Right) ||
                  limits.Right <= limits.Left))
            {
                plot.LockedLimits = limits;
            }
        }

        _forcePlotRebuild = true;
    }


    /// <summary>
    /// Track Map 点选轨迹点 → 同步曲线光标、Dashboard、车辆标记。
    /// </summary>
    private void ApplyCursorFromTrackSample(VehicleSample sample, int trackIndex)
    {
        // 点到某条轨迹时，切换 Dashboard 到对应线下文件
        if (_isOfflineMode &&
            trackIndex >= 0 &&
            trackIndex < _offlineFiles.Count)
        {
            var file = _offlineFiles.Files[trackIndex];
            if (_offlineFiles.Selected?.Id != file.Id)
            {
                _offlineFiles.Select(file.Id);
                _sampleHistory.Clear();
                _sampleHistory.AddRange(file.Samples);
                _latestSample = file.Samples.Count > 0 ? file.Samples[^1] : null;
                RebuildOfflineFileChips();
                RefreshAllPlots(force: true);
            }
        }

        var selectedXSignal =
            XAxisSelector.SelectedValue is string xSignal
                ? xSignal
                : ChannelIds.AxisTime;

        var cursorX = GetAxisValue(sample, selectedXSignal);

        var source = _plots.Count > 0 ? _plots[0] : null;
        SetSharedCursor(cursorX, source);

        // 立刻刷新 Dashboard + 地图标记（不等下一帧 UI 定时器）
        DashboardPanelControl.SetValues(
            speedKph: sample.SpeedKph,
            longitudinalAcceleration: sample.LongitudinalAcceleration,
            lateralAcceleration: sample.LateralAcceleration,
            yawRate: sample.YawRate,
            steeringAngleDeg: 0.0,
            cursorFrozen: true);

        TrackMapPanelControl.SetCursorSample(sample);
    }


    private static double? GetPlotMouseX(
        ScottPlot.WPF.WpfPlot wpfPlot,
        MouseEventArgs e)
    {
        try
        {
            var pos = e.GetPosition(wpfPlot);
            var pixel = new ScottPlot.Pixel(
                (float)(pos.X * wpfPlot.DisplayScale),
                (float)(pos.Y * wpfPlot.DisplayScale));
            return wpfPlot.Plot.GetCoordinates(pixel).X;
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// 在 Clear + DateTimeTicksBottom 之后应用轴范围。
    /// Auto 勾选：按数据；未勾选：绝不从数据重算，沿用 Locked / previous。
    /// </summary>
    private void ApplyAxisLimitsAfterRebuild(
        PlotDefinition plot,
        ScottPlot.Plot scottPlot,
        double[] xs,
        double minY,
        double maxY,
        ScottPlot.AxisLimits previousLimits)
    {
        var autoX =
            XAxisAutoScaleCheckBox.IsChecked == true;
        var autoY =
            plot.AutoScaleY;

        double left;
        double right;
        double bottom;
        double top;

        if (autoX)
        {
            var minX = xs.Min();
            var maxX = xs.Max();
            if (maxX <= minX)
                maxX = minX + 1;

            var xPadding = (maxX - minX) * 0.02;
            if (xPadding <= 0)
                xPadding = 1;

            left = minX - xPadding;
            right = maxX + xPadding;
        }
        else
        {
            // Auto X 关闭：不从数据重算
            left = previousLimits.Left;
            right = previousLimits.Right;

            // 防御：无效范围时退回数据（仅首次/损坏状态）
            if (double.IsInfinity(left) ||
                double.IsInfinity(right) ||
                double.IsNaN(left) ||
                double.IsNaN(right) ||
                right <= left)
            {
                var minX = xs.Min();
                var maxX = xs.Max();
                if (maxX <= minX)
                    maxX = minX + 1;
                var xPadding = (maxX - minX) * 0.02;
                if (xPadding <= 0)
                    xPadding = 1;
                left = minX - xPadding;
                right = maxX + xPadding;
            }
        }

        if (autoY)
        {
            if (maxY <= minY)
                maxY = minY + 1;

            var yPadding = (maxY - minY) * 0.05;
            if (yPadding <= 0)
                yPadding = 1;

            bottom = minY - yPadding;
            top = maxY + yPadding;
        }
        else
        {
            // Auto Y 关闭：不从数据重算
            bottom = previousLimits.Bottom;
            top = previousLimits.Top;

            if (double.IsInfinity(bottom) ||
                double.IsInfinity(top) ||
                double.IsNaN(bottom) ||
                double.IsNaN(top) ||
                top <= bottom)
            {
                if (maxY <= minY)
                    maxY = minY + 1;
                var yPadding = (maxY - minY) * 0.05;
                if (yPadding <= 0)
                    yPadding = 1;
                bottom = minY - yPadding;
                top = maxY + yPadding;
            }
        }

        scottPlot.Axes.SetLimits(left, right, bottom, top);

        // Auto 全关时锁定当前范围，供下一帧 10Hz Refresh 使用
        if (!autoX && !autoY)
        {
            plot.LockedLimits =
                scottPlot.Axes.GetLimits();
        }
        else if (autoX && autoY)
        {
            plot.LockedLimits = null;
        }
        else
        {
            // 单轴 Auto：仍保存当前完整范围，关闭那一轴时用
            plot.LockedLimits =
                scottPlot.Axes.GetLimits();
        }
    }


    private void ApplyDateTimeAxisIfNeeded(
        PlotDefinition plot,
        string selectedXSignal)
    {
        if (plot.WpfPlot is null)
            return;

        if (!string.Equals(selectedXSignal, ChannelIds.AxisTime, StringComparison.OrdinalIgnoreCase))
            return;

        var axis =
            plot.WpfPlot.Plot.Axes.DateTimeTicksBottom();

        if (axis.TickGenerator is
            ScottPlot.TickGenerators.DateTimeAutomatic tickGen)
        {
            tickGen.LabelFormatter = FormatBeijingTickLabel;
        }

        // DateTimeTicksBottom 会把刻度字重置成黑色，必须再刷浅色
        ApplyAxisLabelColors(plot.WpfPlot.Plot);
    }


    private static string FormatBeijingTickLabel(DateTime dt)
    {
        // OADate 按 UTC 存；刻度显示为北京时间
        var utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        // 旧秒表时间戳会落在 1970；显示为经过时间，避免误导
        if (utc.Year < 2000)
        {
            var elapsedMs = (long)Math.Round(
                (utc - DateTime.UnixEpoch).TotalMilliseconds);
            if (elapsedMs < 0)
                elapsedMs = 0;
            var ts = TimeSpan.FromMilliseconds(elapsedMs);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}h{ts.Minutes:D2}m"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        var beijing =
            TimeZoneInfo.ConvertTimeFromUtc(utc, BeijingTimeZone);

        if (beijing.Hour == 0 &&
            beijing.Minute == 0 &&
            beijing.Second == 0)
        {
            return beijing.ToString("MM-dd");
        }

        if (beijing.Second == 0)
            return beijing.ToString("HH:mm");

        return beijing.ToString("HH:mm:ss");
    }


    private static readonly string[] RunAnnotationPalette =
    {
        "#3FBF6F", "#4A9FD8", "#C06AD8", "#E08A4A",
        "#5AC8C8", "#E05252", "#D8D84A", "#C8A34A"
    };

    private void OnTestResultsAnnotated(IReadOnlyList<AnnotatedTestRun> runs)
    {
        _annotatedRuns = runs ?? Array.Empty<AnnotatedTestRun>();
        _selectedAnnotatedRun = _annotatedRuns.Count > 0 ? _annotatedRuns[0].RunNumber : null;
        DashboardPanelControl.ApplyTestResults(_annotatedRuns, _selectedAnnotatedRun);
        // Fresh compute clears compare checks until user re-checks.
        if (_compareRuns.Count > 0 && _compareRuns.All(c => _annotatedRuns.Any(a => a.RunNumber == c.RunNumber)))
        {
            // keep compare if still present
        }
        else
        {
            _compareRuns = Array.Empty<AnnotatedTestRun>();
        }
        ApplyRunAnnotationsToUi();
        RefreshAllPlotsForCompareMode();
    }

    private void OnTestResultRunSelected(int? runNumber)
    {
        _selectedAnnotatedRun = runNumber;
        DashboardPanelControl.ApplyTestResults(_annotatedRuns, _selectedAnnotatedRun);
        ApplyRunAnnotationsToUi();
    }

    private void OnTestResultCheckedRunsChanged(IReadOnlyList<AnnotatedTestRun> checkedRuns)
    {
        var wasCompare = _compareRuns.Count >= 2;
        _compareRuns = checkedRuns ?? Array.Empty<AnnotatedTestRun>();
        var nowCompare = _compareRuns.Count >= 2;

        // Compare uses run-relative seconds; normal mode uses absolute/elapsed axes.
        // Drop locked limits whenever compare mode toggles so the first view can autofit.
        if (wasCompare != nowCompare)
        {
            foreach (var plot in _plots)
            {
                plot.LockedLimits = null;
                if (!nowCompare && plot.WpfPlot is not null)
                    ClearCompareEdgeLegend(plot.WpfPlot.Plot);
            }
        }

        RefreshAllPlotsForCompareMode();
        ApplyRunAnnotationsToUi();
    }

    private void RefreshAllPlotsForCompareMode()
    {
        _forcePlotRebuild = true;
        foreach (var plot in _plots)
            RefreshPlot(plot, GetHistorySnapshot());
    }

    private bool IsCompareModeActive => _compareRuns.Count >= 2;

    private void ApplyRunAnnotationsToUi()
    {
        foreach (var plot in _plots)
        {
            ApplyPlotOverlays(plot);
            plot.WpfPlot?.Refresh();
        }

        // Map: when comparing, highlight checked runs; else all annotated with focus.
        var mapRuns = IsCompareModeActive ? _compareRuns : _annotatedRuns;
        TrackMapPanelControl.SetRunHighlights(mapRuns, _selectedAnnotatedRun);
    }

    private void DrawCompareOverlay(
        PlotDefinition plot,
        ScottPlot.Plot scottPlot,
        List<ChannelDefinition> selectedChannels,
        ScottPlot.AxisLimits previousLimits)
    {
        if (plot.WpfPlot is null) return;
        ApplyDarkPlotStyle(plot.WpfPlot);
        scottPlot.Title(plot.Name + " — Compare");
        scottPlot.XLabel("Run time (s)");
        scottPlot.YLabel("Value");

        double minY = double.MaxValue;
        double maxY = double.MinValue;
        double maxX = 0;

        var channels = selectedChannels.Count > 0
            ? selectedChannels
            : new List<ChannelDefinition>
            {
                new ChannelDefinition { ChannelId = ChannelIds.Velocity }
            };

        foreach (var annotated in _compareRuns)
        {
            var run = annotated.Result;
            var samples = annotated.Samples;
            if (samples.Count < 2)
                continue;

            var i0 = Math.Clamp(run.StartSampleIndex, 0, samples.Count - 1);
            var i1 = Math.Clamp(run.EndSampleIndex, 0, samples.Count - 1);
            if (i1 < i0)
                (i0, i1) = (i1, i0);

            var t0 = samples[i0].Timestamp;
            var colorHex = string.IsNullOrWhiteSpace(annotated.ColorHex)
                ? RunAnnotationPalette[(run.RunNumber - 1) % RunAnnotationPalette.Length]
                : annotated.ColorHex;

            foreach (var channel in channels)
            {
                var xs = new List<double>();
                var ys = new List<double>();
                for (var i = i0; i <= i1; i++)
                {
                    var x = (samples[i].Timestamp - t0) / 1000.0;
                    var y = GetSignalValue(samples[i], channel.ChannelId);
                    if (double.IsNaN(y) || double.IsInfinity(y))
                        continue;
                    xs.Add(x);
                    ys.Add(y);
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    if (x > maxX) maxX = x;
                }

                if (xs.Count < 2)
                    continue;

                var scatter = scottPlot.Add.Scatter(xs.ToArray(), ys.ToArray());
                scatter.Color = ScottPlot.Color.FromHex(colorHex);
                scatter.LineWidth = 2f;
                scatter.MarkerSize = 0;
                var chName = GetSignalDisplayName(channel.ChannelId);
                var src = string.IsNullOrWhiteSpace(annotated.SourceLabel) ? "file" : annotated.SourceLabel;
                scatter.LegendText = $"{src} · Run {run.RunNumber} · {chName}";
            }
        }

        if (minY > maxY)
        {
            minY = 0;
            maxY = 1;
        }

        // Legend outside the data area (right edge) so it never covers traces.
        // Remove prior LegendPanels — Clear() does not, and rebuilds would stack them.
        ApplyCompareEdgeLegend(scottPlot);

        // Respect Auto X/Y + LockedLimits (same as normal mode). Never force-fit every frame.
        var limitXs = new[] { 0.0, Math.Max(maxX, 0.1) };
        ApplyAxisLimitsAfterRebuild(plot, scottPlot, limitXs, minY, maxY, previousLimits);

        plot.LastXs = Array.Empty<double>();
        plot.LastSamples = Array.Empty<VehicleSample>();
        plot.LastChannelSeries.Clear();
    }

    /// <summary>
    /// Place compare legend on the figure's right edge (outside data area).
    /// </summary>
    private static void ApplyCompareEdgeLegend(ScottPlot.Plot scottPlot)
    {
        foreach (var panel in scottPlot.Axes.GetPanels())
        {
            if (panel is ScottPlot.Panels.LegendPanel legendPanel)
                scottPlot.Axes.Remove(legendPanel);
        }

        scottPlot.HideLegend();
        var legend = scottPlot.ShowLegend(ScottPlot.Edge.Right);
        legend.Legend.FontSize = 10;
        legend.Legend.FontColor = ScottPlot.Color.FromHex("#C3CBD8");
        legend.Legend.BackgroundColor = ScottPlot.Color.FromHex("#12171E").WithAlpha(0.88);
        legend.Legend.OutlineColor = ScottPlot.Color.FromHex("#1E2530");
        legend.Legend.ShadowColor = ScottPlot.Colors.Transparent;
    }

    /// <summary>
    /// Drop edge legend panels when leaving compare so normal in-plot legend returns.
    /// </summary>
    private static void ClearCompareEdgeLegend(ScottPlot.Plot scottPlot)
    {
        foreach (var panel in scottPlot.Axes.GetPanels())
        {
            if (panel is ScottPlot.Panels.LegendPanel legendPanel)
                scottPlot.Axes.Remove(legendPanel);
        }
    }

    private void DrawRunAnnotationsOnPlot(PlotDefinition plot, ScottPlot.Plot scottPlot)
    {
        if (IsCompareModeActive)
            return; // compare mode draws normalized overlays in RefreshPlot

        if (_annotatedRuns.Count == 0)
            return;

        var xSignal = XAxisSelector.SelectedValue as string ?? ChannelIds.AxisTime;

        foreach (var annotated in _annotatedRuns)
        {
            var run = annotated.Result;
            if (!TryGetRunAxisRange(annotated, xSignal, out var left, out var right))
                continue;

            var colorHex = string.IsNullOrWhiteSpace(annotated.ColorHex)
                ? RunAnnotationPalette[(run.RunNumber - 1) % RunAnnotationPalette.Length]
                : annotated.ColorHex;
            var selected = _selectedAnnotatedRun is int sel && sel == run.RunNumber;
            var dim = _selectedAnnotatedRun is not null && !selected;
            var fillAlpha = selected ? 0.28 : (dim ? 0.08 : 0.16);
            var lineAlpha = selected ? 0.95 : (dim ? 0.35 : 0.65);

            var span = scottPlot.Add.HorizontalSpan(left, right);
            span.FillColor = ScottPlot.Color.FromHex(colorHex).WithAlpha(fillAlpha);
            span.LineColor = ScottPlot.Color.FromHex(colorHex).WithAlpha(lineAlpha);
            span.LineWidth = selected ? 2 : 1;

            if (!selected)
                continue;

            var v1 = scottPlot.Add.VerticalLine(left);
            v1.LineColor = ScottPlot.Color.FromHex(colorHex);
            v1.LineWidth = 1.5f;
            v1.LinePattern = ScottPlot.LinePattern.DenselyDashed;

            var v2 = scottPlot.Add.VerticalLine(right);
            v2.LineColor = ScottPlot.Color.FromHex(colorHex);
            v2.LineWidth = 1.5f;
            v2.LinePattern = ScottPlot.LinePattern.DenselyDashed;

            var labelText = string.IsNullOrWhiteSpace(annotated.SourceLabel)
                ? $"Run {run.RunNumber}"
                : $"{annotated.SourceLabel} · Run {run.RunNumber}";
            var label = scottPlot.Add.Annotation(labelText, ScottPlot.Alignment.UpperRight);
            label.LabelFontSize = 14;
            label.LabelFontColor = ScottPlot.Color.FromHex(colorHex);
            label.LabelBackgroundColor = ScottPlot.Color.FromHex("#0E131A").WithAlpha(0.7);
            label.LabelBorderColor = ScottPlot.Color.FromHex(colorHex);
            label.OffsetX = 10;
            label.OffsetY = 8;
        }
    }

    private bool TryGetRunAxisRange(
        AnnotatedTestRun annotated,
        string xSignal,
        out double left,
        out double right)
    {
        left = 0;
        right = 0;
        var samples = annotated.Samples;
        var n = samples.Count;
        if (n == 0)
            return false;

        var run = annotated.Result;
        var i0 = Math.Clamp(run.StartSampleIndex, 0, n - 1);
        var i1 = Math.Clamp(run.EndSampleIndex, 0, n - 1);
        left = GetAxisValue(samples[i0], xSignal);
        right = GetAxisValue(samples[i1], xSignal);
        if (right < left)
            (left, right) = (right, left);
        return Math.Abs(right - left) > 1e-12;
    }

    private void ApplyPlotOverlays(PlotDefinition plot)
    {
        if (plot.WpfPlot is null)
            return;

        var scottPlot = plot.WpfPlot.Plot;

        // 清掉旧的交互层（曲线本身在 RefreshPlot 里重建；
        // 这里在 Clear 之后调用时图上还没有 overlay）
        // 若被 Mouse 事件单独调用，需先移除旧 overlay。
        RemovePlotOverlays(scottPlot);

        DrawRunAnnotationsOnPlot(plot, scottPlot);

        // 横向选区高亮（X 方向）
        if (plot.SelectionX1 is double sx1 &&
            plot.SelectionX2 is double sx2 &&
            Math.Abs(sx2 - sx1) > 1e-12)
        {
            var left = Math.Min(sx1, sx2);
            var right = Math.Max(sx1, sx2);
            var span = scottPlot.Add.HorizontalSpan(left, right);
            span.FillColor =
                ScottPlot.Color.FromHex("#C8A34A").WithAlpha(0.16);
            span.LineColor =
                ScottPlot.Color.FromHex("#C8A34A").WithAlpha(0.55);
            span.LineWidth = 1;

            if (!string.IsNullOrEmpty(_lastMeasureText))
            {
                var manno = scottPlot.Add.Annotation(
                    _lastMeasureText,
                    ScottPlot.Alignment.LowerLeft);
                manno.LabelFontSize = 13;
                manno.LabelFontColor = ScottPlot.Color.FromHex("#C8A34A");
                manno.LabelBackgroundColor =
                    ScottPlot.Color.FromHex("#0E131A").WithAlpha(0.78);
                manno.LabelBorderColor = ScottPlot.Color.FromHex("#1E2530");
                manno.OffsetX = 10;
                manno.OffsetY = 10;
            }
        }

        // 竖向光标
        if (plot.CursorX is double cursorX)
        {
            var vLine = scottPlot.Add.VerticalLine(cursorX);
            vLine.LineColor =
                ScottPlot.Color.FromHex("#C8A34A");
            vLine.LineWidth = 1.5f;
            vLine.LinePattern = ScottPlot.LinePattern.Solid;

            var readout = BuildCursorReadout(plot, cursorX);
            if (!string.IsNullOrEmpty(readout))
            {
                var anno = scottPlot.Add.Annotation(
                    readout,
                    ScottPlot.Alignment.UpperLeft);
                anno.LabelFontSize = 18;
                anno.LabelFontColor =
                    ScottPlot.Color.FromHex("#C8A34A");
                anno.LabelBackgroundColor =
                    ScottPlot.Color.FromHex("#0E131A").WithAlpha(0.72);
                anno.LabelBorderColor =
                    ScottPlot.Color.FromHex("#1E2530");
                anno.LabelBorderWidth = 1;
                anno.LabelShadowColor =
                    ScottPlot.Colors.Transparent;
                anno.OffsetX = 10;
                anno.OffsetY = 10;
            }
        }
    }


    private static void RemovePlotOverlays(ScottPlot.Plot scottPlot)
    {
        // RefreshPlot 每次 Clear() 后没有旧 overlay；
        // 鼠标拖动时需要去掉上一帧自己加的 span/line/annotation。
        var toRemove = scottPlot.GetPlottables()
            .Where(p =>
                p is ScottPlot.Plottables.VerticalLine
                    or ScottPlot.Plottables.HorizontalSpan
                    or ScottPlot.Plottables.Annotation)
            .ToList();

        foreach (var p in toRemove)
            scottPlot.Remove(p);
    }


    private string BuildCursorReadout(
        PlotDefinition plot,
        double cursorX)
    {
        if (plot.LastXs is null ||
            plot.LastXs.Length == 0 ||
            plot.LastChannelSeries.Count == 0)
        {
            return FormatAxisValue(cursorX, plot.IsTimeAxis);
        }

        var index = FindNearestIndex(plot.LastXs, cursorX);
        var xAt = plot.LastXs[index];

        var lines = new List<string>
        {
            FormatAxisValue(xAt, plot.IsTimeAxis)
        };

        foreach (var (signal, ys) in plot.LastChannelSeries)
        {
            if (index < 0 || index >= ys.Length)
                continue;

            var name = GetSignalDisplayName(signal);
            var unit = GetSignalUnit(signal);
            lines.Add($"{name}  {ys[index]:0.###} {unit}");
        }

        return string.Join("\n", lines);
    }


    private static string FormatAxisValue(
        double x,
        bool isTimeAxis)
    {
        if (!isTimeAxis)
            return $"X = {x:0.###}";

        try
        {
            var utc = DateTime.SpecifyKind(
                DateTime.FromOADate(x),
                DateTimeKind.Utc);

            // 旧 Simulator 用秒表毫秒当 Timestamp，会落在 1970 附近。
            // 墙钟修好后不应再出现；这里仍给出可读回退，避免再显示 1970。
            if (utc.Year < 2000)
            {
                var elapsedMs = (long)Math.Round(
                    (utc - DateTime.UnixEpoch).TotalMilliseconds);
                if (elapsedMs < 0)
                    elapsedMs = 0;
                var ts = TimeSpan.FromMilliseconds(elapsedMs);
                return ts.TotalHours >= 1
                    ? $"{(int)ts.TotalHours}h{ts.Minutes:D2}m{ts.Seconds:D2}s"
                    : $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
            }

            var beijing =
                TimeZoneInfo.ConvertTimeFromUtc(utc, BeijingTimeZone);
            return beijing.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
        catch
        {
            return $"X = {x:0.###}";
        }
    }


    private static int FindNearestIndex(
        double[] xs,
        double x)
    {
        if (xs.Length == 1)
            return 0;

        var i = Array.BinarySearch(xs, x);
        if (i >= 0)
            return i;

        i = ~i;
        if (i <= 0)
            return 0;
        if (i >= xs.Length)
            return xs.Length - 1;

        return (x - xs[i - 1]) <= (xs[i] - x)
            ? i - 1
            : i;
    }


    private static double GetAxisValue(
        VehicleSample sample,
        string channelId)
    {
        if (string.Equals(channelId, ChannelIds.AxisTime, StringComparison.OrdinalIgnoreCase))
        {
            // UTC 瞬间 → OADate，供 DateTime 轴使用
            var utc =
                DateTimeOffset
                    .FromUnixTimeMilliseconds(sample.Timestamp)
                    .UtcDateTime;
            return utc.ToOADate();
        }

        return GetSignalValue(sample, channelId);
    }


    private static string GetAxisLabel(string channelId)
    {
        if (string.Equals(channelId, ChannelIds.AxisTime, StringComparison.OrdinalIgnoreCase))
            return "Time (Beijing)";

        return $"{GetSignalDisplayName(channelId)} ({GetSignalUnit(channelId)})";
    }


    // ============================================================
    // 获取历史数据
    // ============================================================


    private string? _lastMeasureText;

    private IReadOnlyList<VehicleSample> GetHistorySnapshotForAnalysis()
    {
        var snap = GetHistorySnapshot();
        return MathsEnricher.Apply(snap, MathsChannelStore.Instance.Definitions);
    }

    private void RemergeMathsChannels()
    {
        var maths = MathsChannelStore.Instance.Definitions
            .Select(d => (
                d.Id,
                string.IsNullOrWhiteSpace(d.DisplayName) ? d.Id : d.DisplayName,
                d.Unit ?? ""))
            .ToList();
        ChannelRegistry.Instance.SyncMathsChannels(maths);
    }

    private void OnSessionEditRequested()
    {
        var meta = SessionMetadata.Current.Clone();
        if (SessionEditDialog.Show(this, meta) == true)
        {
            SessionMetadata.Current = meta;
            TestResultsPanelControl.RefreshSessionSummary();
        }
    }

    private void OnMathsChanged()
    {
        RemergeMathsChannels();
        RefreshChannelSelectorsFromRegistry();
        RefreshAllPlots(force: true);
    }

    private void UpdateSelectionMeasure(PlotDefinition plot)
    {
        if (plot.SelectionX1 is not double x1 || plot.SelectionX2 is not double x2 ||
            Math.Abs(x2 - x1) < 1e-12)
        {
            _lastMeasureText = null;
            return;
        }

        var history = GetHistorySnapshotForAnalysis();
        var xSignal = XAxisSelector.SelectedValue as string ?? ChannelIds.AxisTime;
        var channelIds = plot.Channels.Select(c => c.ChannelId).Distinct().ToList();
        if (channelIds.Count == 0)
        {
            _lastMeasureText = null;
            return;
        }

        var measure = SelectionMeasure.Compute(
            history,
            s => GetAxisValue(s, xSignal),
            x1,
            x2,
            channelIds);
        if (measure is null)
        {
            _lastMeasureText = null;
            return;
        }

        _lastMeasureText = SelectionMeasure.Format(
            measure,
            id => ChannelRegistry.Instance.GetDisplayName(id));
    }

    private void CopyMeasureToClipboard()
    {
        if (string.IsNullOrEmpty(_lastMeasureText))
            return;
        try { Clipboard.SetText(_lastMeasureText); }
        catch { /* ignore clipboard races */ }
    }

    private IReadOnlyList<VehicleSample> GetHistorySnapshot()
    {
        return _sampleHistory.Snapshot();
    }

    private IReadOnlyList<SampleSource> GetTestSampleSources()
    {
        if (_isOfflineMode && _offlineFiles.Count > 0)
        {
            var defs = MathsChannelStore.Instance.Definitions;
            return _offlineFiles.Files
                .Select(f => new SampleSource
                {
                    Id = f.Id,
                    Label = f.DisplayName,
                    ColorHex = f.ColorHex,
                    Samples = MathsEnricher.Apply(f.Samples, defs)
                })
                .ToList();
        }

        var live = _sampleHistory.Snapshot();
        if (live.Count == 0)
            return Array.Empty<SampleSource>();

        return new[]
        {
            new SampleSource
            {
                Label = "Live",
                ColorHex = "#C8A34A",
                Samples = MathsEnricher.Apply(live, MathsChannelStore.Instance.Definitions)
            }
        };
    }



    // ============================================================
    // Signal 数值
    // ============================================================

    private static double GetSignalValue(
        VehicleSample sample,
        string channelId)
    {
        if (string.Equals(channelId, ChannelIds.AxisTime, StringComparison.OrdinalIgnoreCase))
            return sample.Timestamp / 1000.0;

        if (sample.Channels.ContainsKey(channelId))
            return sample.Channels[channelId];

        foreach (var def in MathsChannelStore.Instance.Definitions)
        {
            if (!string.Equals(def.Id, channelId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (MathsExpression.TryEvaluate(
                    def.Expression,
                    name => sample.GetChannel(name),
                    out var value,
                    out _))
                return value;
            break;
        }

        return sample.GetChannel(channelId);
    }


    // ============================================================
    // Signal 名称
    // ============================================================

    private static string GetSignalDisplayName(
        string channelId)
    {
        if (string.Equals(channelId, ChannelIds.AxisTime, StringComparison.OrdinalIgnoreCase))
            return "Time";

        return ChannelRegistry.Instance.GetDisplayName(channelId);
    }


    // ============================================================
    // Signal 单位
    // ============================================================

    private static string GetSignalUnit(
        string channelId)
    {
        if (string.Equals(channelId, ChannelIds.AxisTime, StringComparison.OrdinalIgnoreCase))
            return "Beijing";

        return ChannelRegistry.Instance.GetUnit(channelId);
    }


    // ============================================================
    // Reset
    // ============================================================

    private void ResetViewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        XAxisAutoScaleCheckBox.IsChecked =
            true;


        foreach (var plot in _plots)
        {
            plot.AutoScaleY = true;
            plot.CursorX = null;
            plot.SelectionX1 = null;
            plot.SelectionX2 = null;
            plot.IsSelectingRange = false;
        }


        RefreshPlotContainer();

        RefreshAllPlots(force: true);
    }


    // ============================================================
    // 数据源切换（UDP / GSpot）
    // ============================================================

    private async void GSpotButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isOfflineMode)
            await EnterOnlineModeAsync();

        var roomDefault =
            Environment.GetEnvironmentVariable("CMTS_GSPOT_ROOM")
            ?? "";
        var pwDefault =
            Environment.GetEnvironmentVariable("CMTS_GSPOT_PASSWORD")
            ?? "";
        var cNumDefault =
            Environment.GetEnvironmentVariable("CMTS_GSPOT_CNUM");

        if (!TryPromptGSpotOptions(
                roomDefault,
                pwDefault,
                cNumDefault,
                out var options) ||
            options is null)
        {
            return;
        }

        // 先探测连接；只有真正 Connected 才替换当前数据源。
        // 失败时保留原来的 UDP/GSpot，避免「假成功」把可用源切掉。
        var previous = _dataSourceSession.Current;
        var next = _dataSourceSession.CreateGSpot(options);
        GSpotButton.IsEnabled = false;
        UdpSourceButton.IsEnabled = false;

        try
        {
            await next.StartAsync();

            // StartAsync 立即返回；必须等到 Connected，或首轮失败进入 Reconnecting。
            var connected = await DataSourceSession.WaitForConnectedAsync(
                next,
                TimeSpan.FromSeconds(20));

            if (!connected)
            {
                var err = next.LastError
                          ?? "超时：未能进入 Connected（可能卡在 Connecting/Reconnecting）。";

                try
                {
                    await next.StopAsync();
                }
                catch
                {
                    try { next.Dispose(); } catch { /* ignore */ }
                }

                // previous 从未被停掉
                MessageBox.Show(
                    this,
                    "GSpot 连接失败，已保持原数据源不变。\n\n" +
                    $"房间: {options.RoomId}\n" +
                    $"原因: {err}\n\n" +
                    "请核对房间号/密码；Filter cNum 请先留空。\n" +
                    "并请供应商确认同房间在官方客户端能看到实时数据。",
                    "GSpot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // 连接成功：再停旧源、提交切换
            await _dataSourceSession.CommitConnectedAsync(previous, next);
            EnterLiveSourceMode();
            GSpotButton.Content = "GSpot●";

            MessageBox.Show(
                this,
                "GSpot WebSocket 已连接。\n" +
                $"房间: {options.RoomId}\n" +
                "若房间内无在线车辆推流，Rx 仍可能保持 0（事件推送，非固定 100 Hz）。\n" +
                "Lost/OOO 对 GSpot 无意义，显示为 0。",
                "GSpot",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            try
            {
                await next.StopAsync();
            }
            catch
            {
                try { next.Dispose(); } catch { /* ignore */ }
            }

            MessageBox.Show(
                this,
                "GSpot 连接失败，已保持原数据源不变。\n\n" +
                ex.Message,
                "GSpot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            GSpotButton.IsEnabled = true;
            UdpSourceButton.IsEnabled = true;
        }
    }

    private async void UdpSourceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isOfflineMode)
            await EnterOnlineModeAsync();

        try
        {
            // 已是 UDP 且正在监听：提示即可。
            // Faulted（例如端口占用）时允许原地重试 StartAsync。
            if (_dataSourceSession.Current is UdpReceiver existingUdp)
            {
                if (existingUdp.State == DataSourceState.Connected)
                {
                    MessageBox.Show(
                        this,
                        "当前已是 UDP 数据源。",
                        "UDP",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                try
                {
                    await existingUdp.StartAsync();
                    EnterLiveSourceMode();
                    GSpotButton.Content = "GSpot…";
                    MessageBox.Show(
                        this,
                        "UDP 已重新监听。",
                        "UDP",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception retryEx)
                {
                    var detail = !string.IsNullOrWhiteSpace(existingUdp.LastError)
                        ? existingUdp.LastError
                        : retryEx.Message;
                    MessageBox.Show(
                        this,
                        "UDP 重试失败:\n" + detail,
                        "UDP",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                return;
            }

            var next = _dataSourceSession.CreateUdpReceiver();
            await SwitchDataSourceAsync(next);
            GSpotButton.Content = "GSpot…";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "切回 UDP 失败:\n" + ex.Message,
                "UDP",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task SwitchDataSourceAsync(IDataSource next)
    {
        await _dataSourceSession.SwitchAsync(next);
        EnterLiveSourceMode();
    }

    /// <summary>
    /// 切回 UDP / GSpot 等实时源：恢复通道目录，并允许消费循环再写历史。
    /// </summary>
    private void EnterLiveSourceMode()
    {
        _historyOfflineMode = false;
        ChannelRegistry.Instance.SetLiveCore();
        RefreshChannelSelectorsFromRegistry();
    }

    /// <summary>
    /// 临时接线用的房间/密码对话框（Settings 页就绪前）。
    /// 也可用环境变量 CMTS_GSPOT_ROOM / CMTS_GSPOT_PASSWORD / CMTS_GSPOT_CNUM。
    /// </summary>
    private bool TryPromptGSpotOptions(
        string roomDefault,
        string passwordDefault,
        string? cNumDefault,
        out GSpotOptions? options)
    {
        options = null;

        // SizeToContent + MinHeight：避免固定 Height 被标题栏吃掉后裁掉底部按钮。
        var dialog = new Window
        {
            Title = "Connect GSpot",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Width = 380,
            MinHeight = 300,
            SizeToContent = SizeToContent.Height,
            Background = (Brush)FindResource("AppBackground"),
            ShowInTaskbar = false
        };

        var root = new Grid { Margin = new Thickness(16, 16, 16, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var roomBox = new TextBox
        {
            Text = roomDefault,
            Margin = new Thickness(0, 4, 0, 10),
            Style = TryFindResource("DarkTextBoxStyle") as Style
        };
        var pwBox = new PasswordBox
        {
            Margin = new Thickness(0, 4, 0, 10),
            Style = TryFindResource("DarkPasswordBoxStyle") as Style
        };
        if (!string.IsNullOrEmpty(passwordDefault))
            pwBox.Password = passwordDefault;

        var cNumBox = new TextBox
        {
            Text = cNumDefault ?? "",
            Margin = new Thickness(0, 4, 0, 10),
            Style = TryFindResource("DarkTextBoxStyle") as Style
        };

        void AddLabeled(int row, string label, UIElement field)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 12
            });
            stack.Children.Add(field);
            Grid.SetRow(stack, row);
            root.Children.Add(stack);
        }

        AddLabeled(0, "Room ID", roomBox);
        AddLabeled(1, "Password", pwBox);
        AddLabeled(2, "Filter cNum (optional)", cNumBox);

        var hint = new TextBlock
        {
            Text = "Base: weixin.jichexiaozi.com/transponder",
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(hint, 3);
        root.Children.Add(hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var ok = new Button
        {
            Content = "Connect",
            Width = 88,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            IsEnabled = true,
            Style = TryFindResource("ToolButtonStyle") as Style
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 88,
            IsCancel = true,
            IsEnabled = true,
            Style = TryFindResource("ToolButtonStyle") as Style
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        dialog.Content = root;

        var accepted = false;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(roomBox.Text))
            {
                MessageBox.Show(
                    dialog,
                    "请填写 Room ID。",
                    "GSpot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            accepted = true;
            dialog.DialogResult = true;
            dialog.Close();
        };
        cancel.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };

        var result = dialog.ShowDialog();
        if (result != true || !accepted)
            return false;

        options = new GSpotOptions
        {
            RoomId = roomBox.Text.Trim(),
            Password = pwBox.Password,
            FilterCNum = string.IsNullOrWhiteSpace(cNumBox.Text)
                ? null
                : cNumBox.Text.Trim()
        };
        return true;
    }

    // ============================================================
    // Simulator
    // ============================================================

    private void SimulatorButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        // 如果 Simulator 已经打开
        if (_simulatorWindow != null)
        {
            if (_simulatorWindow.WindowState ==
                WindowState.Minimized)
            {
                _simulatorWindow.WindowState =
                    WindowState.Normal;
            }

            _simulatorWindow.Activate();

            return;
        }


        // ========================================================
        // 创建 Simulator
        // ========================================================

        if (_udpSender is null)
        {
            MessageBox.Show(
                this,
                "UDP Sender 尚未初始化。",
                "Simulator",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }


        var simulator =
            new VehicleSimulator(
                _udpSender);


        _simulatorWindow =
            new SimulatorWindow(
                simulator);


        // 建立主窗口与 Simulator 的 Owner 关系
        _simulatorWindow.Owner =
            this;


        _simulatorWindow.Closed +=
            (_, _) =>
            {
                _simulatorWindow =
                    null;
            };


        _simulatorWindow.Show();
    }


    // ============================================================
    // 实时数据显示
    // ============================================================

    private void UiTimer_Tick(
        object? sender,
        EventArgs e)
    {
        UpdateNumericDisplay();

        UpdateNetworkDisplay();

        RefreshAllPlots();
    }


    private void UpdateNumericDisplay()
    {
        var cursorSample =
            TryGetCursorSample();

        var sample =
            cursorSample ?? _latestSample;

        if (sample is null)
            return;

        // ------------------------------------------------------------
        // 有曲线光标时 Dashboard 冻结为选中点（对齐 VBTS）；
        // Escape 清除光标后恢复实时。
        //
        // 注意：VehicleSample 目前没有 SteeringAngleDeg 字段，
        // 也没有对应的 UDP 数据字段，所以方向盘角度暂时显示 0。
        // ------------------------------------------------------------

        var enriched = MathsEnricher.ApplyOne(
            sample,
            MathsChannelStore.Instance.Definitions);
        DashboardPanelControl.ApplyLiveSample(
            enriched,
            cursorFrozen: cursorSample is not null);

        // 多车摘要（线下多文件）；点选芯片后 Primary 对应当前文件
        if (_isOfflineMode && _offlineFiles.Count > 0)
        {
            var selected = _offlineFiles.Selected;
            var summary = _offlineFiles.Files
                .Select(f =>
                {
                    var s = f.Samples.Count > 0 ? f.Samples[^1] : null;
                    // 当前选中文件：有光标时用光标样本
                    if (cursorSample is not null &&
                        selected is not null &&
                        f.Id == selected.Id)
                        s = cursorSample;
                    return (
                        f.DisplayName,
                        f.ColorHex,
                        s?.SpeedKph ?? 0.0);
                })
                .ToList();
            DashboardPanelControl.SetMultiVehicleSummary(
                summary,
                selected?.DisplayName);
        }
        else
        {
            DashboardPanelControl.SetMultiVehicleSummary(
                Array.Empty<(string, string, double)>());
        }

        // 光标移动时 Track Map 车辆位置跟着走；清除光标后回到实时最新点
        TrackMapPanelControl.SetCursorSample(cursorSample);
    }


    /// <summary>
    /// 取当前光标最近邻样本；无光标或无缓存时返回 null。
    /// </summary>
    private VehicleSample? TryGetCursorSample()
    {
        var plot = _cursorSourcePlot;
        if (plot is null ||
            plot.CursorX is not double cursorX ||
            plot.LastXs is null ||
            plot.LastXs.Length == 0 ||
            plot.LastSamples is null ||
            plot.LastSamples.Length == 0)
        {
            return null;
        }

        var index = FindNearestIndex(plot.LastXs, cursorX);
        if (index < 0 || index >= plot.LastSamples.Length)
            return null;

        return plot.LastSamples[index];
    }


    private void UpdateNetworkDisplay()
    {
        var source =
            _dataSourceSession.Current;

        if (source is null)
        {
            if (ReceivedPacketsText is not null)
                ReceivedPacketsText.Text = "0";
            if (ValidPacketsText is not null)
                ValidPacketsText.Text = "0";
            if (LostPacketsText is not null)
                LostPacketsText.Text = "0";
            if (OutOfOrderPacketsText is not null)
                OutOfOrderPacketsText.Text = "0";
            UpdateConnectionStatusUi();
            return;
        }

        var stats = source.Stats;

        ReceivedPacketsText.Text =
            stats.Received.ToString();

        ValidPacketsText.Text =
            stats.Valid.ToString();

        var recordDrops = _recordingSession.DroppedSamples;
        var busDrops = _dataBus.DroppedPublishCount;

        if (recordDrops > 0)
        {
            LostPacketsText.Text =
                $"{stats.Lost}+R{recordDrops}";
            LostPacketsText.ToolTip =
                $"网络丢包 {stats.Lost}；录制队列丢样 {recordDrops}" +
                (busDrops > 0 ? $"；总线 TryPublish 失败 {busDrops}" : "");
        }
        else
        {
            LostPacketsText.Text =
                stats.Lost.ToString();
            LostPacketsText.ToolTip =
                busDrops > 0
                    ? $"总线 TryPublish 失败 {busDrops}"
                    : null;
        }

        OutOfOrderPacketsText.Text =
            stats.OutOfOrder.ToString();

        UpdateConnectionStatusUi(source);
    }

    private void UpdateConnectionStatusUi()
    {
        if (_isOfflineMode)
        {
            if (ConnectionStatusText is not null)
            {
                ConnectionStatusText.Text =
                    _offlineFiles.Count > 0
                        ? "Offline · " + _offlineFiles.Count + " file(s)"
                        : "Offline";
                ConnectionStatusText.ToolTip = "Offline VBO replay";
            }

            if (ConnectionDot is not null)
            {
                try
                {
                    ConnectionDot.Fill =
                        (Brush)new BrushConverter().ConvertFromString("#8A94A6")!;
                }
                catch { /* ignore */ }
            }

            return;
        }

        if (_dataSourceSession.Current is null)
        {
            if (ConnectionStatusText is not null)
            {
                ConnectionStatusText.Text = "Stopped";
                ConnectionStatusText.ToolTip = "Online connection stopped";
            }

            if (ConnectionDot is not null)
            {
                try
                {
                    ConnectionDot.Fill =
                        (Brush)new BrushConverter().ConvertFromString("#8A94A6")!;
                }
                catch { /* ignore */ }
            }

            return;
        }

        UpdateConnectionStatusUi(_dataSourceSession.Current);
    }

    private void UpdateConnectionStatusUi(IDataSource source)
    {
        if (ConnectionStatusText is null || ConnectionDot is null)
            return;

        var (label, color) = source.State switch
        {
            DataSourceState.Connected =>
                ("Online", "#3FBF6F"),
            DataSourceState.Connecting =>
                ("Connecting…", "#E0B060"),
            DataSourceState.Reconnecting =>
                ("Reconnecting…", "#E0B060"),
            DataSourceState.Faulted =>
                ("Faulted", "#E08A8A"),
            _ =>
                ("Offline", "#8A94A6")
        };

        // UDP 无真实会话：保持 Online 语义（绑定成功即视为就绪）
        if (source is UdpReceiver &&
            source.State is DataSourceState.Connected
                or DataSourceState.Disconnected)
        {
            // UdpReceiver StartAsync 后即 Connected；未启动则 Offline
            if (source.State == DataSourceState.Connected)
            {
                label = "Online";
                color = "#3FBF6F";
            }
        }

        ConnectionStatusText.Text = label;
        if (source is GSpotDataSource gspot &&
            !string.IsNullOrWhiteSpace(gspot.LastError) &&
            source.State is DataSourceState.Reconnecting
                or DataSourceState.Faulted)
        {
            ConnectionStatusText.ToolTip =
                $"{source.Name}: {gspot.LastError}";
        }
        else
        {
            ConnectionStatusText.ToolTip =
                $"{source.Name} · {source.State}";
        }

        try
        {
            ConnectionDot.Fill =
                (Brush)new BrushConverter().ConvertFromString(color)!;
        }
        catch
        {
            // ignore brush parse
        }
    }


    // ============================================================
    // 数据消费
    // ============================================================

    private async Task ConsumeDataAsync(
        CancellationToken cancellationToken)
    {
        var reader =
            _dataBus.Reader;

        while (true)
        {
            bool hasData;

            try
            {
                // WaitToReadAsync 在通道正常完成时返回 false，
                // 只有真正取消时才抛 OperationCanceledException。
                hasData =
                    await reader.WaitToReadAsync(
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 程序关闭：正常退出。
                break;
            }

            if (!hasData)
            {
                // DataBus 已 Complete，队列中不再有新数据。
                break;
            }

            while (reader.TryRead(
                       out var sample))
            {
                // 线下模式：丢弃实时包，避免污染 VBO 历史
                if (_historyOfflineMode || _isOfflineMode)
                {
                    Interlocked.Increment(ref _sampleCount);
                    continue;
                }

                _liveSessionOriginMs ??= sample.Timestamp;
                sample = SampleEnricher.EnrichLive(
                    sample,
                    _liveSessionOriginMs.Value,
                    ref _liveDistanceM,
                    _livePreviousSample);
                _livePreviousSample = sample;

                _latestSample =
                    sample;

                // 离线 VBO 回放时不把实时样本写入历史。
                if (!_historyOfflineMode)
                {
                    _sampleHistory.Add(sample);
                }

                Interlocked.Increment(
                    ref _sampleCount);


                // 只有正在录制时才写入文件（RecordingSession）。
                //
                // 暂停时刻意让"经过时间"按真实时间继续走：
                // VboRecorder 内部以第一条数据为原点，
                // 恢复后第一行的 Elapsed_time 会自然跳过暂停时长。
                // TryWrite 失败会计入 DroppedSamples，
                // 并由 UpdateNetworkDisplay 在 Lost 区展示。
                _recordingSession.TryWriteIfRecording(sample);
            }
        }
    }


    // ============================================================
    // 程序启动
    // ============================================================

    private async void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        SyncLayoutMapVisuals();

        _cancellationTokenSource =
            new CancellationTokenSource();


        // ========================================================
        // UDP Sender
        //
        // 这里只创建 Sender，不自动启动 Simulator。
        // Simulator 按钮点击后才会使用这个 Sender。
        // ========================================================

        _udpSender =
            new UdpSender(
                "127.0.0.1",
                50000);


        // ========================================================
        // Data source（默认 UDP；可用 GSpot… / UDP 按钮切换）
        //
        // MainWindow 只依赖 IDataSource。
        // 关闭时由 MainWindow_Closed 调用 StopAsync / Dispose。
        // ========================================================

        var defaultUdp =
            _dataSourceSession.CreateUdpReceiver();
        _dataSourceSession.SetCurrent(defaultUdp);

        try
        {
            await defaultUdp.StartAsync();
        }
        catch (Exception ex)
        {
            // 端口占用等绑定失败：弹窗提示，窗口继续可用（可切 GSpot 或稍后点 UDP 重试）。
            var detail = !string.IsNullOrWhiteSpace(defaultUdp.LastError)
                ? defaultUdp.LastError
                : ex.Message;

            MessageBox.Show(
                this,
                "UDP 监听未能启动，程序仍会打开。\n\n" +
                detail +
                "\n\n请先关掉仍在运行的旧 CMTS / 占用 50000 端口的进程，" +
                "再点工具栏「UDP」重试；或改用「GSpot…」。",
                "UDP",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }


        // ========================================================
        // Recorder
        //
        // 不在启动时创建文件。
        // 顶部栏的 ● Start 按钮会创建文件并开始记录，
        // ■ Stop 会关闭文件。
        //
        // 关闭窗口时由 MainWindow_Closed 统一 Dispose。
        // ========================================================


        // ========================================================
        // 启动数据消费
        // ========================================================

        _ = Task.Run(() =>
            ConsumeDataAsync(
                _cancellationTokenSource.Token));


        // ========================================================
        // UI Timer
        // ========================================================

        _uiTimer.Start();

        // Restore last session (VBOs / gates / Test Results / maths) when files still exist.
        try
        {
            await RestoreSessionMemoryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Session restore failed: " + ex);
        }
    }


    // ============================================================
    // 程序关闭
    // ============================================================

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        try { SaveSessionMemory(); } catch { /* best-effort */ }

        _uiTimer.Stop();


        // ========================================================
        // 关闭 Simulator
        // ========================================================

        if (_simulatorWindow != null)
        {
            _simulatorWindow.Close();

            _simulatorWindow = null;
        }


        // ========================================================
        // 关闭顺序很重要
        //
        // 1. 先 Stop / Dispose 数据源，
        //    让接收循环通过 ObjectDisposedException 自然退出。
        // 2. 再 Complete DataBus，
        //    让数据消费循环通过 WaitToReadAsync 返回 false 退出。
        // 3. 最后取消 CancellationTokenSource。
        //
        // 这样关闭过程中不会产生 OperationCanceledException。
        // ========================================================

        // StopAsync 内部会 Dispose；这里同步 Dispose 即可，
        // 关闭窗口时不必阻塞等待接收循环收尾。
        _dataSourceSession.Dispose();


        _dataBus.Complete();


        // 正常情况下，接收循环和数据消费循环
        // 已经通过上面两步自然退出。
        //
        // 这里再调用一次 Cancel() 只是兜底，
        // 保证异常场景下消费循环不会残留。
        // 若循环已经退出，则不会产生任何异常。
        _cancellationTokenSource?.Cancel();


        // ========================================================
        // 释放资源
        // ========================================================

        _recordingSession.Dispose();


        _udpSender?.Dispose();


        _cancellationTokenSource?.Dispose();
    }


    // ============================================================
    // Plot 数据结构
    // ============================================================

    private sealed class PlotDefinition
    {
        public string Name { get; set; } = "";

        public bool AutoScaleY { get; set; }

        public List<ChannelDefinition> Channels { get; }
            = new();

        public ScottPlot.WPF.WpfPlot? WpfPlot { get; set; }

        /// <summary>Plot panel height in pixels (stacked; may overflow curves viewport).</summary>
        public double HeightPx { get; set; } = 280;


        /// <summary>光标 X（与当前轴同一单位；Time 轴为 OADate）。</summary>
        public double? CursorX { get; set; }

        /// <summary>横向选区起止（可空表示无选区）。</summary>
        public double? SelectionX1 { get; set; }

        public double? SelectionX2 { get; set; }

        public bool IsSelectingRange { get; set; }

        /// <summary>左键按住拖动竖线光标中。</summary>
        public bool IsDraggingCursor { get; set; }

        /// <summary>曲线标题栏 Auto Y 复选框，便于手动缩放时同步关闭。</summary>
        public CheckBox? AutoScaleYCheckBox { get; set; }

        /// <summary>
        /// Auto 关闭时锁定的轴范围；10Hz Refresh 不得从数据重算覆盖。
        /// </summary>
        public ScottPlot.AxisLimits? LockedLimits { get; set; }

        /// <summary>最近一次绘制的 X / 各通道 Y，供光标插值。</summary>
        public double[]? LastXs { get; set; }

        /// <summary>与 LastXs 对齐的样本缓存，供 Dashboard 冻结读数。</summary>
        public VehicleSample[]? LastSamples { get; set; }

        public List<(string ChannelId, double[] Ys)> LastChannelSeries { get; }
            = new();

        public bool IsTimeAxis { get; set; }
    }

    // ============================================================
    // Channel 数据结构
    // ============================================================

    private sealed class ChannelDefinition
    {
        /// <summary>通道 Id（见 ChannelIds / ChannelRegistry）。</summary>
        public string ChannelId { get; set; } = ChannelIds.Velocity;
    }


    // ============================================================
    // Session memory (%LocalAppData%\CMTS\session-memory.json)
    // ============================================================

    private DispatcherTimer? _sessionSaveTimer;
    private bool _restoringSession;

    private void ScheduleSessionMemorySave()
    {
        if (_restoringSession)
        {
            // Drop any pending save queued before restore started (would write empty maths).
            _sessionSaveTimer?.Stop();
            return;
        }
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ScheduleSessionMemorySave);
            return;
        }

        _sessionSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _sessionSaveTimer.Tick -= SessionSaveTimer_Tick;
        _sessionSaveTimer.Tick += SessionSaveTimer_Tick;
        _sessionSaveTimer.Stop();
        _sessionSaveTimer.Start();
    }

    private void SessionSaveTimer_Tick(object? sender, EventArgs e)
    {
        _sessionSaveTimer?.Stop();
        SaveSessionMemory();
    }

    private void SaveSessionMemory()
    {
        if (_restoringSession)
            return;

        CapturePlotRowHeightsFromContainer();

        var snap = new SessionMemorySnapshot
        {
            IsOfflineMode = _isOfflineMode,
            ActiveVboPath = _offlineFiles.Selected?.FilePath,
            LastVbtsPath = SessionMemoryStore.LastImportedVbtsPath,
            SelectedGateId = GateStore.Instance.SelectedId,
            AnalysisStartGateId = GateStore.Instance.AnalysisStartId,
            AnalysisEndGateId = GateStore.Instance.AnalysisEndId,
            TestResults = TestResultsPanelControl?.CaptureSettings(),
            Curves = CaptureCurvesSettings(),
        };

        foreach (var f in _offlineFiles.Files)
            snap.VboFiles.Add(f.FilePath);

        foreach (var g in GateStore.Instance.Gates)
            snap.Gates.Add(SessionMemoryStore.FromGate(g));

        foreach (var m in MathsChannelStore.Instance.Definitions)
            snap.MathsChannels.Add(SessionMemoryStore.FromMaths(m));

        SessionMemoryStore.Save(snap);
    }

    private async Task RestoreSessionMemoryAsync()
    {
        var snap = SessionMemoryStore.Load();
        if (snap is null)
            return;

        _sessionSaveTimer?.Stop();
        _restoringSession = true;
        try
        {
            if (snap.MathsChannels.Count > 0)
            {
                var defs = snap.MathsChannels
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Expression))
                    .Select(SessionMemoryStore.ToMaths)
                    .ToList();
                if (defs.Count > 0)
                {
                    if (!MathsChannelStore.Instance.TryReplaceAll(defs, out var mathsError))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "Session maths restore failed: " + mathsError);
                    }
                    else
                    {
                        RemergeMathsChannels();
                        RefreshChannelSelectorsFromRegistry();
                    }
                }
            }

            if (snap.Gates.Count > 0)
            {
                var gates = snap.Gates.Select(SessionMemoryStore.ToGate).ToList();
                GateStore.Instance.ReplaceAll(
                    gates,
                    selectedId: snap.SelectedGateId,
                    analysisStartId: snap.AnalysisStartGateId,
                    analysisEndId: snap.AnalysisEndGateId);
            }
            else if (!string.IsNullOrWhiteSpace(snap.LastVbtsPath) &&
                     File.Exists(snap.LastVbtsPath))
            {
                try
                {
                    var imported = VbtsGateImporter.ImportFile(snap.LastVbtsPath);
                    GateStore.Instance.Clear();
                    foreach (var g in imported)
                        GateStore.Instance.Add(g);
                    SessionMemoryStore.LastImportedVbtsPath = snap.LastVbtsPath;
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(snap.LastVbtsPath))
                SessionMemoryStore.LastImportedVbtsPath = snap.LastVbtsPath;

            var existing = snap.VboFiles
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (existing.Count > 0)
            {
                await EnterOfflineModeAsync(stopOnline: true);
                foreach (var path in existing)
                    LoadVboFile(path);

                if (!string.IsNullOrWhiteSpace(snap.ActiveVboPath))
                {
                    var hit = _offlineFiles.Files.FirstOrDefault(f =>
                        string.Equals(f.FilePath, snap.ActiveVboPath, StringComparison.OrdinalIgnoreCase));
                    if (hit is not null && _offlineFiles.Selected?.Id != hit.Id)
                        SelectOfflineFileKeepingCursor(hit.Id);
                }

                UpdateOfflineFileStatusLabels();
                ShowPage(dashboard: true);
            }

            if (snap.TestResults is not null)
                TestResultsPanelControl.ApplySettings(snap.TestResults);

            // VBO load rebuilds ChannelRegistry; re-merge maths so selectors / plots see them.
            if (MathsChannelStore.Instance.Definitions.Count > 0)
            {
                RemergeMathsChannels();
                RefreshChannelSelectorsFromRegistry();
            }

            ApplyCurvesSettings(snap.Curves);
        }
        finally
        {
            _sessionSaveTimer?.Stop();
            _restoringSession = false;
        }
    }

    private CurvesSettingsDto CaptureCurvesSettings()
    {
        var dto = new CurvesSettingsDto
        {
            XAxisChannelId = XAxisSelector.SelectedValue as string,
            XAutoScale = XAxisAutoScaleCheckBox.IsChecked == true,
            SinglePlotFillsViewport = _singlePlotFillsViewport && _plots.Count <= 1
        };

        foreach (var plot in _plots)
        {
            dto.Plots.Add(new PlotSettingsDto
            {
                Name = plot.Name,
                AutoScaleY = plot.AutoScaleY,
                HeightPx = plot.HeightPx > 0 ? plot.HeightPx : GetViewportFillHeight(),
                ChannelIds = plot.Channels
                    .Select(c => c.ChannelId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToList()
            });
        }

        return dto;
    }

    private void ApplyCurvesSettings(CurvesSettingsDto? curves)
    {
        if (curves is null || curves.Plots is null || curves.Plots.Count == 0)
        {
            // Keep the default single plot created at init; fill viewport once laid out.
            _singlePlotFillsViewport = true;
            Dispatcher.BeginInvoke(
                () => ApplySinglePlotFillHeight(rebuild: false),
                System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        _plots.Clear();
        _nextPlotNumber = 1;
        _singlePlotFillsViewport = curves.SinglePlotFillsViewport && curves.Plots.Count == 1;

        foreach (var p in curves.Plots)
        {
            var plot = new PlotDefinition
            {
                Name = string.IsNullOrWhiteSpace(p.Name)
                    ? $"Plot {_nextPlotNumber}"
                    : p.Name,
                AutoScaleY = p.AutoScaleY,
                HeightPx = p.HeightPx >= PlotMinHeightPx
                    ? p.HeightPx
                    : GetViewportFillHeight()
            };

            if (plot.Name.StartsWith("Plot ", StringComparison.Ordinal) &&
                int.TryParse(plot.Name.AsSpan("Plot ".Length), out var n) &&
                n >= _nextPlotNumber)
            {
                _nextPlotNumber = n + 1;
            }

            var ids = p.ChannelIds ?? new List<string>();
            foreach (var id in ids.Where(x => !string.IsNullOrWhiteSpace(x)))
                plot.Channels.Add(new ChannelDefinition { ChannelId = id });

            if (plot.Channels.Count == 0)
            {
                plot.Channels.Add(new ChannelDefinition
                {
                    ChannelId = ChannelRegistry.Instance.DefaultPlotChannelId
                });
            }

            _plots.Add(plot);
        }

        if (_nextPlotNumber <= _plots.Count)
            _nextPlotNumber = _plots.Count + 1;

        if (!string.IsNullOrWhiteSpace(curves.XAxisChannelId))
        {
            try { XAxisSelector.SelectedValue = curves.XAxisChannelId; }
            catch { /* channel may not exist yet */ }
        }

        XAxisAutoScaleCheckBox.IsChecked = curves.XAutoScale;

        RefreshPlotContainer();
        RefreshAllPlots(force: true);

        if (_singlePlotFillsViewport)
        {
            Dispatcher.BeginInvoke(
                () => ApplySinglePlotFillHeight(rebuild: false),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

}
