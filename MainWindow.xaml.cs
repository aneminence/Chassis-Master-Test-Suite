using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Chassis_Master_Test_Suite.Communication;
using Chassis_Master_Test_Suite.Communication.GSpot;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;
using Chassis_Master_Test_Suite.Simulator;

namespace Chassis_Master_Test_Suite;

public partial class MainWindow : Window
{
    private readonly DataBus _dataBus = new();

    /// <summary>
    /// 当前活动数据源（UDP 或 GSpot）。
    /// Settings 完善前用顶部 GSpot… / UDP 按钮切换。
    /// </summary>
    private IDataSource? _dataSource;

    private UdpSender? _udpSender;

    // ============================================================
    // 录制（VBO）
    //
    // Recorder 在窗口加载时创建，
    // 但只有在 _recordingState == Recording 时才写入数据。
    // ============================================================

    private VboRecorder? _recorder;

    private RecordingState _recordingState = RecordingState.Stopped;

    /// <summary>
    /// 本次测试第一条数据的时间戳（毫秒），用于显示经过时间。
    /// </summary>
    private long? _recordingStartTimestamp;

    private DispatcherTimer? _elapsedTimer;

    /// <summary>
    /// 录制状态机。
    ///
    /// Stopped -> Recording <-> Paused -> Stopped
    /// </summary>
    private enum RecordingState
    {
        Stopped,
        Recording,
        Paused
    }

    private CancellationTokenSource? _cancellationTokenSource;

    private readonly List<VehicleSample> _sampleHistory = new();

    private const int MaxHistorySamples = 1_000_000;

    private long _sampleCount;

    private readonly DispatcherTimer _uiTimer;

    private VehicleSample? _latestSample;

    // ============================================================
    // Simulator
    // ============================================================

    private SimulatorWindow? _simulatorWindow;

    // ============================================================
    // Plot 系统
    // ============================================================

    private readonly List<PlotDefinition> _plots = new();

    private int _nextPlotNumber = 1;

    private bool _isRefreshingPlots;

    /// <summary>
    /// 当前驱动 Dashboard 冻结读数的曲线（有光标时）。
    /// Escape 清除光标后恢复实时。
    /// </summary>
    private PlotDefinition? _cursorSourcePlot;

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
        InitializeComponent();

        InitializeAxisSelector();

        InitializePlotSystem();

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        _uiTimer.Tick += UiTimer_Tick;

        StartClock();

        Loaded += MainWindow_Loaded;

        Closed += MainWindow_Closed;
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

    private void StartRecorder()
    {
        _recorder = new VboRecorder(
            Path.Combine(
                AppContext.BaseDirectory,
                "Recordings",
                $"CMTS_{DateTime.Now:yyyyMMdd_HHmmss}.vbo"));
    }

    private void RecordButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        switch (_recordingState)
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
    /// 切换录制状态，并同步按钮外观和 Elapsed 计时器。
    /// </summary>
    private void ApplyRecordingState(
        RecordingState state)
    {
        switch (state)
        {
            case RecordingState.Stopped:
                // 关闭并落盘当前文件。
                _recorder?.Dispose();
                _recorder = null;

                _recordingStartTimestamp = null;

                _elapsedTimer?.Stop();

                RecordGlyphText.Text = "●";
                RecordLabelText.Text = "Start";
                RecordGlyphText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0xE0, 0x8A, 0x8A));

                StopRecordButton.IsEnabled = false;

                ElapsedTimeText.Text = "--";
                break;

            case RecordingState.Recording:
                // 从停止状态开始时才新建文件；
                // 从暂停恢复时继续写同一个文件。
                if (_recorder is null)
                {
                    // 让第一条样本重新定义经过时间的零点。
                    _recordingStartTimestamp = null;

                    StartRecorder();
                }

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

        _recordingState = state;
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

        if (_recordingStartTimestamp is null ||
            _latestSample is null)
        {
            ElapsedTimeText.Text = "00:00:00.0";
            return;
        }

        var elapsed =
            TimeSpan.FromMilliseconds(
                _latestSample.Timestamp -
                _recordingStartTimestamp.Value);

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

            var samples = reader.ReadAll();

            if (samples.Count == 0)
            {
                return 0;
            }

            // 离线数据替换掉当前历史，
            // 避免和实时采集的数据混在一起。
            _sampleHistory.Clear();

            foreach (var sample in samples)
            {
                _sampleHistory.Add(sample);
            }

            // 控制内存占用：保留最近的样本。
            while (_sampleHistory.Count > MaxHistorySamples)
            {
                _sampleHistory.RemoveAt(0);
            }

            _latestSample = samples[^1];

            Interlocked.Add(
                ref _sampleCount,
                samples.Count);

            RefreshAllPlots();

            UpdateNumericDisplay();

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


    // ============================================================
    // 导航栏页面切换
    // ============================================================

    private void DashboardNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowPage(dashboard: true);
    }


    private void ReplayNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
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

        DashboardNavButton.Style =
            (Style)FindResource(
                dashboard
                    ? "NavButtonActiveStyle"
                    : "NavButtonStyle");

        ReplayNavButton.Style =
            (Style)FindResource(
                dashboard
                    ? "NavButtonStyle"
                    : "NavButtonActiveStyle");
    }


    // ============================================================
    // Replay：打开 VBO 文件
    // ============================================================

    private void OpenVboButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open VBO file",
                Filter =
                    "VBO files (*.vbo)|*.vbo|All files (*.*)|*.*",
                InitialDirectory =
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Recordings")
            };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var count = LoadVboFile(dialog.FileName);

        if (count <= 0)
        {
            return;
        }

        // 让用户马上看到曲线，
        // 否则数据载入了但还停在 Replay 页面上。
        ShowPage(dashboard: true);

        ReplayFileText.Text =
            Path.GetFileName(dialog.FileName);

        ReplayInfoText.Text =
            $"{count} samples loaded";
    }


    // ============================================================
    // X Axis
    // ============================================================

    private void InitializeAxisSelector()
    {
        XAxisSelector.ItemsSource =
            Enum.GetValues<PlotSignal>();

        XAxisSelector.SelectedItem =
            PlotSignal.Time;
    }


    private void AxisSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
            return;

        RefreshAllPlots();
    }


    private void XAxisAutoScaleCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsInitialized)
            return;

        RefreshAllPlots();
    }


    // ============================================================
    // Plot 初始化
    // ============================================================

    private void InitializePlotSystem()
    {
        AddPlot();
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
        var plot = new PlotDefinition
        {
            Name = $"Plot {_nextPlotNumber++}",
            AutoScaleY = true
        };

        plot.Channels.Add(
            new ChannelDefinition
            {
                Signal = PlotSignal.Speed
            });

        _plots.Add(plot);

        RefreshPlotContainer();

        RefreshAllPlots();
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

        RefreshPlotContainer();

        RefreshAllPlots();
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
        PlotContainer.Children.Clear();

        foreach (var plot in _plots)
        {
            var plotControl =
                CreatePlotControl(plot);

            PlotContainer.Children.Add(
                plotControl);
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
                new Thickness(0, 0, 0, 10),

            Padding =
                new Thickness(8)
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
                Height =
                    GridLength.Auto
            });


        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(350)
            });


        // ========================================================
        // Plot Header
        // ========================================================

        var header = new Grid();

        header.Margin =
            new Thickness(0, 0, 0, 8);


        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });


        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(220)
            });


        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });


        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });


        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });


        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });


        // ========================================================
        // Plot 名称
        // ========================================================

        var nameLabel = new TextBlock
        {
            Text = "Plot Name",

            Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        0x8A,
                        0x94,
                        0xA6)),

            FontSize = 11,

            FontWeight =
                FontWeights.Bold,

            VerticalAlignment =
                VerticalAlignment.Center,

            Margin =
                new Thickness(0, 0, 8, 0)
        };

        Grid.SetColumn(
            nameLabel,
            0);

        header.Children.Add(
            nameLabel);


        var nameTextBox = new TextBox
        {
            Text = plot.Name,

            Height = 28,

            VerticalContentAlignment =
                VerticalAlignment.Center
        };


        // 深色样式（App.xaml 提供）
        nameTextBox.SetResourceReference(
            FrameworkElement.StyleProperty,
            "DarkTextBoxStyle");


        nameTextBox.TextChanged +=
            (_, _) =>
            {
                plot.Name =
                    nameTextBox.Text;

                RefreshPlotTitle(plot);
            };


        Grid.SetColumn(
            nameTextBox,
            1);

        header.Children.Add(
            nameTextBox);


        // ========================================================
        // Y Auto Scale
        // ========================================================

        var autoScaleCheckBox = new CheckBox
        {
            Content = "Auto Y",

            IsChecked =
                plot.AutoScaleY,

            VerticalAlignment =
                VerticalAlignment.Center,

            Margin =
                new Thickness(15, 0, 15, 0)
        };

        plot.AutoScaleYCheckBox = autoScaleCheckBox;


        autoScaleCheckBox.Checked +=
            (_, _) =>
            {
                plot.AutoScaleY = true;

                RefreshPlot(plot);
            };


        autoScaleCheckBox.Unchecked +=
            (_, _) =>
            {
                plot.AutoScaleY = false;
            };


        Grid.SetColumn(
            autoScaleCheckBox,
            2);

        header.Children.Add(
            autoScaleCheckBox);


        // ========================================================
        // Add Channel
        // ========================================================

        var addChannelButton = new Button
        {
            Content = "+ Channel",

            Height = 26,

            Margin =
                new Thickness(0, 0, 8, 0)
        };


        // 深色样式（App.xaml 提供）
        addChannelButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ToolButtonStyle");


        addChannelButton.Click +=
            (_, _) =>
            {
                AddChannel(plot);
            };


        Grid.SetColumn(
            addChannelButton,
            3);

        header.Children.Add(
            addChannelButton);


        // ========================================================
        // Remove Plot
        // ========================================================

        var removePlotButton = new Button
        {
            Content = "Remove Plot",

            Height = 26
        };


        // 深色样式 + 危险操作配色（App.xaml 提供）
        removePlotButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "DangerButtonStyle");


        removePlotButton.Click +=
            (_, _) =>
            {
                RemovePlot(plot);
            };


        Grid.SetColumn(
            removePlotButton,
            5);

        header.Children.Add(
            removePlotButton);


        root.Children.Add(header);


        // ========================================================
        // Channel 区域
        // ========================================================

        var channelPanel = new StackPanel
        {
            Orientation =
                Orientation.Vertical,

            Margin =
                new Thickness(0, 0, 0, 8)
        };


        foreach (var channel in plot.Channels)
        {
            var channelControl =
                CreateChannelControl(
                    plot,
                    channel);

            channelPanel.Children.Add(
                channelControl);
        }


        var channelScrollViewer =
            new ScrollViewer
            {
                Content = channelPanel,

                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,

                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,

                MaxHeight = 110
            };


        Grid.SetRow(
            channelScrollViewer,
            1);

        root.Children.Add(
            channelScrollViewer);


        // ========================================================
        // ScottPlot
        // ========================================================

        var wpfPlot =
            new ScottPlot.WPF.WpfPlot();

        wpfPlot.Height = 350;

        ApplyDarkPlotStyle(wpfPlot);

        plot.WpfPlot = wpfPlot;
        wpfPlot.Focusable = true;

        AttachPlotInteraction(plot, wpfPlot);


        Grid.SetRow(
            wpfPlot,
            2);

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
        var grid = new Grid
        {
            Height = 30,

            Margin =
                new Thickness(0, 2, 0, 2)
        };


        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(90)
            });


        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(220)
            });


        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(100)
            });


        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });


        // Channel 标签

        var channelLabel = new TextBlock
        {
            Text = "Channel",

            Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        0x8A,
                        0x94,
                        0xA6)),

            FontSize = 11,

            VerticalAlignment =
                VerticalAlignment.Center
        };


        Grid.SetColumn(
            channelLabel,
            0);

        grid.Children.Add(
            channelLabel);


        // Signal ComboBox

        var comboBox = new ComboBox
        {
            ItemsSource =
                Enum.GetValues<PlotSignal>(),

            SelectedItem =
                channel.Signal,

            Height = 28,

            VerticalContentAlignment =
                System.Windows.VerticalAlignment.Center
        };


        comboBox.SelectionChanged +=
            (_, _) =>
            {
                if (comboBox.SelectedItem
                    is PlotSignal signal)
                {
                    channel.Signal =
                        signal;

                    RefreshPlot(plot);
                }
            };


        Grid.SetColumn(
            comboBox,
            1);

        grid.Children.Add(
            comboBox);


        // 单位

        var unitText = new TextBlock
        {
            Text =
                GetSignalUnit(
                    channel.Signal),

            Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        0x8A,
                        0x94,
                        0xA6)),

            FontSize = 11,

            VerticalAlignment =
                VerticalAlignment.Center,

            Margin =
                new Thickness(10, 0, 0, 0)
        };


        comboBox.SelectionChanged +=
            (_, _) =>
            {
                if (comboBox.SelectedItem
                    is PlotSignal signal)
                {
                    unitText.Text =
                        GetSignalUnit(signal);
                }
            };


        Grid.SetColumn(
            unitText,
            2);

        grid.Children.Add(
            unitText);


        // 删除 Channel

        var removeButton = new Button
        {
            Content = "×",

            Width = 30,

            Height = 26,

            FontSize = 13,

            Padding =
                new Thickness(0)
        };


        // 深色样式 + 危险操作配色（App.xaml 提供）
        removeButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "DangerButtonStyle");


        removeButton.Click +=
            (_, _) =>
            {
                RemoveChannel(
                    plot,
                    channel);
            };


        Grid.SetColumn(
            removeButton,
            3);

        grid.Children.Add(
            removeButton);


        return grid;
    }


    // ============================================================
    // 添加 Channel
    // ============================================================

    private void AddChannel(
        PlotDefinition plot)
    {
        plot.Channels.Add(
            new ChannelDefinition
            {
                Signal =
                    PlotSignal.Speed
            });

        RefreshPlotContainer();

        RefreshAllPlots();
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

        RefreshAllPlots();
    }


    // ============================================================
    // 刷新所有 Plot
    // ============================================================

    private void RefreshAllPlots()
    {
        if (_isRefreshingPlots)
            return;

        _isRefreshingPlots = true;

        try
        {
            foreach (var plot in _plots)
            {
                RefreshPlot(plot);
            }

            // 轨迹图。SetTrack 内部按 250 ms 限流，
            // 所以这里跟着 10 Hz 的 UI 定时器调也不会重画太频繁。
            TrackMapPanelControl.SetTrack(_sampleHistory);
        }
        finally
        {
            _isRefreshingPlots = false;
        }
    }


    // ============================================================
    // 刷新单个 Plot
    // ============================================================

    private void RefreshPlot(
        PlotDefinition plot)
    {
        if (plot.WpfPlot is null)
            return;


        var history =
            GetHistorySnapshot();


        var selectedXSignal =
            XAxisSelector.SelectedItem
                is PlotSignal xSignal
                ? xSignal
                : PlotSignal.Time;


        var selectedChannels =
            plot.Channels.ToList();


        var scottPlot =
            plot.WpfPlot.Plot;


        // 记住用户拖动/缩放后的轴范围（非 Auto 时）
        var previousLimits =
            scottPlot.Axes.GetLimits();


        scottPlot.Clear();


        plot.LastXs = null;
        plot.LastSamples = null;
        plot.LastChannelSeries.Clear();
        plot.IsTimeAxis = selectedXSignal == PlotSignal.Time;


        // ========================================================
        // 数据不足
        // ========================================================

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
        // X 数据
        // ========================================================

        var xs =
            new double[history.Count];


        for (var i = 0;
             i < history.Count;
             i++)
        {
            xs[i] =
                GetAxisValue(
                    history[i],
                    selectedXSignal);
        }


        // ========================================================
        // Channel 曲线
        // ========================================================

        double minY =
            double.MaxValue;

        double maxY =
            double.MinValue;


        foreach (var channel in selectedChannels)
        {
            var ys =
                new double[history.Count];


            for (var i = 0;
                 i < history.Count;
                 i++)
            {
                var value =
                    GetSignalValue(
                        history[i],
                        channel.Signal);

                ys[i] = value;


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
                $"{GetSignalDisplayName(channel.Signal)} " +
                $"({GetSignalUnit(channel.Signal)})";


            scatter.LineWidth = 1;
            scatter.MarkerSize = 0;

            plot.LastChannelSeries.Add(
                (channel.Signal, ys));
        }


        plot.LastXs = xs;
        plot.LastSamples = history.ToArray();


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


        // ========================================================
        // X Auto Scale
        // ========================================================

        if (XAxisAutoScaleCheckBox.IsChecked == true)
        {
            var minX =
                xs.Min();

            var maxX =
                xs.Max();


            if (maxX <= minX)
                maxX = minX + 1;


            var xPadding =
                (maxX - minX) * 0.02;


            if (xPadding <= 0)
                xPadding = 1;


            // ====================================================
            // Y Auto Scale
            // ====================================================

            if (plot.AutoScaleY)
            {
                if (maxY <= minY)
                    maxY = minY + 1;


                var yPadding =
                    (maxY - minY) * 0.05;


                if (yPadding <= 0)
                    yPadding = 1;


                scottPlot.Axes.SetLimits(
                    minX - xPadding,
                    maxX + xPadding,
                    minY - yPadding,
                    maxY + yPadding);
            }
            else
            {
                scottPlot.Axes.SetLimits(
                    minX - xPadding,
                    maxX + xPadding,
                    previousLimits.Bottom,
                    previousLimits.Top);
            }
        }
        else
        {
            // X 不自动缩放时，只处理 Y

            if (plot.AutoScaleY)
            {
                if (maxY <= minY)
                    maxY = minY + 1;


                var yPadding =
                    (maxY - minY) * 0.05;


                if (yPadding <= 0)
                    yPadding = 1;


                scottPlot.Axes.SetLimits(
                    previousLimits.Left,
                    previousLimits.Right,
                    minY - yPadding,
                    maxY + yPadding);
            }
            else
            {
                scottPlot.Axes.SetLimits(
                    previousLimits.Left,
                    previousLimits.Right,
                    previousLimits.Bottom,
                    previousLimits.Top);
            }
        }


        // ScottPlot 5 的 Clear() 会把样式复位，
        // 所以每次重建曲线后都重新套一遍深色主题。
        ApplyDarkPlotStyle(
            plot.WpfPlot);

        ApplyDateTimeAxisIfNeeded(
            plot,
            selectedXSignal);

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

    private void AttachPlotInteraction(
        PlotDefinition plot,
        ScottPlot.WPF.WpfPlot wpfPlot)
    {
        ConfigurePlotMouseBindings(wpfPlot);

        wpfPlot.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle ||
                e.ChangedButton == MouseButton.Right)
            {
                // 用户手动平移/缩放后，关掉 Auto，否则下一帧 RefreshPlot 会把视图拽回去
                SuspendAutoScaleForUserInteraction(plot);
                return;
            }

            if (e.ChangedButton != MouseButton.Left)
                return;

            Keyboard.Focus(wpfPlot);

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
                e.Handled = true;
            }
            else
            {
                plot.IsDraggingCursor = true;
                plot.CursorX = x;
                _cursorSourcePlot = plot;
                // 左键专用于光标，避免与其它左键交互抢事件
                e.Handled = true;
            }

            ApplyPlotOverlays(plot);
            wpfPlot.Refresh();
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

                plot.CursorX = cx;
                _cursorSourcePlot = plot;
                ApplyPlotOverlays(plot);
                wpfPlot.Refresh();
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
            if (e.ChangedButton != MouseButton.Left)
                return;

            if (plot.IsDraggingCursor)
            {
                plot.IsDraggingCursor = false;
                var cx = GetPlotMouseX(wpfPlot, e);
                if (cx is not null)
                {
                    plot.CursorX = cx;
                    _cursorSourcePlot = plot;
                }

                ApplyPlotOverlays(plot);
                wpfPlot.Refresh();
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

            // 松手后把光标放到选区终点（或点击位置）
            if (plot.SelectionX2 is double endX)
            {
                plot.CursorX = endX;
                _cursorSourcePlot = plot;
            }

            ApplyPlotOverlays(plot);
            wpfPlot.Refresh();
            UpdateNumericDisplay();
        };

        wpfPlot.MouseLeave += (_, _) =>
        {
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
            if (e.Key != Key.Escape)
                return;

            plot.CursorX = null;
            plot.SelectionX1 = null;
            plot.SelectionX2 = null;
            plot.IsSelectingRange = false;
            plot.IsDraggingCursor = false;
            if (ReferenceEquals(_cursorSourcePlot, plot))
                _cursorSourcePlot = null;
            wpfPlot.UserInputProcessor.Enable();
            ApplyPlotOverlays(plot);
            wpfPlot.Refresh();
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
        processor.DoubleLeftClickBenchmark(false);

        // 中键拖拽 = 平移
        processor.UserActionResponses.Add(
            new ScottPlot.Interactivity.UserActionResponses.MouseDragPan(
                ScottPlot.Interactivity.StandardMouseButtons.Middle));

        // 右键拖拽 = 缩放（与原先默认一致）
        processor.RightClickDragZoom(enable: true);
    }


    /// <summary>
    /// 用户手动平移/缩放时关掉 Auto X / Auto Y，避免 10 Hz 刷新把视图重置。
    /// </summary>
    private void SuspendAutoScaleForUserInteraction(
        PlotDefinition plot)
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


    private void ApplyDateTimeAxisIfNeeded(
        PlotDefinition plot,
        PlotSignal selectedXSignal)
    {
        if (plot.WpfPlot is null)
            return;

        if (selectedXSignal != PlotSignal.Time)
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


    private void ApplyPlotOverlays(PlotDefinition plot)
    {
        if (plot.WpfPlot is null)
            return;

        var scottPlot = plot.WpfPlot.Plot;

        // 清掉旧的交互层（曲线本身在 RefreshPlot 里重建；
        // 这里在 Clear 之后调用时图上还没有 overlay）
        // 若被 Mouse 事件单独调用，需先移除旧 overlay。
        RemovePlotOverlays(scottPlot);

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
        PlotSignal signal)
    {
        if (signal == PlotSignal.Time)
        {
            // UTC 瞬间 → OADate，供 DateTime 轴使用
            var utc =
                DateTimeOffset
                    .FromUnixTimeMilliseconds(sample.Timestamp)
                    .UtcDateTime;
            return utc.ToOADate();
        }

        return GetSignalValue(sample, signal);
    }


    private static string GetAxisLabel(PlotSignal signal)
    {
        if (signal == PlotSignal.Time)
            return "Time (Beijing)";

        return $"{GetSignalDisplayName(signal)} ({GetSignalUnit(signal)})";
    }


    // ============================================================
    // 获取历史数据
    // ============================================================

    private List<VehicleSample> GetHistorySnapshot()
    {
        return _sampleHistory.ToList();
    }


    // ============================================================
    // Signal 数值
    // ============================================================

    private static double GetSignalValue(
        VehicleSample sample,
        PlotSignal signal)
    {
        return signal switch
        {
            PlotSignal.Time =>
                sample.Timestamp / 1000.0,

            PlotSignal.Speed =>
                sample.SpeedKph,

            PlotSignal.LongitudinalAcceleration =>
                sample.LongitudinalAcceleration,

            PlotSignal.LateralAcceleration =>
                sample.LateralAcceleration,

            PlotSignal.VerticalAcceleration =>
                sample.VerticalAcceleration,

            PlotSignal.YawRate =>
                sample.YawRate,

            PlotSignal.Heading =>
                sample.Heading,

            PlotSignal.Latitude =>
                sample.Latitude,

            PlotSignal.Longitude =>
                sample.Longitude,

            PlotSignal.Altitude =>
                sample.Altitude,

            _ => 0
        };
    }


    // ============================================================
    // Signal 名称
    // ============================================================

    private static string GetSignalDisplayName(
        PlotSignal signal)
    {
        return signal switch
        {
            PlotSignal.Time =>
                "Time",

            PlotSignal.Speed =>
                "Speed",

            PlotSignal.LongitudinalAcceleration =>
                "Longitudinal Accel",

            PlotSignal.LateralAcceleration =>
                "Lateral Accel",

            PlotSignal.VerticalAcceleration =>
                "Vertical Accel",

            PlotSignal.YawRate =>
                "Yaw Rate",

            PlotSignal.Heading =>
                "Heading",

            PlotSignal.Latitude =>
                "Latitude",

            PlotSignal.Longitude =>
                "Longitude",

            PlotSignal.Altitude =>
                "Altitude",

            _ => "Value"
        };
    }


    // ============================================================
    // Signal 单位
    // ============================================================

    private static string GetSignalUnit(
        PlotSignal signal)
    {
        return signal switch
        {
            PlotSignal.Time =>
                "Beijing",

            PlotSignal.Speed =>
                "km/h",

            PlotSignal.LongitudinalAcceleration =>
                "m/s²",

            PlotSignal.LateralAcceleration =>
                "m/s²",

            PlotSignal.VerticalAcceleration =>
                "m/s²",

            PlotSignal.YawRate =>
                "deg/s",

            PlotSignal.Heading =>
                "deg",

            PlotSignal.Latitude =>
                "deg",

            PlotSignal.Longitude =>
                "deg",

            PlotSignal.Altitude =>
                "m",

            _ => ""
        };
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

        RefreshAllPlots();
    }


    // ============================================================
    // 数据源切换（UDP / GSpot）
    // ============================================================

    private async void GSpotButton_Click(
        object sender,
        RoutedEventArgs e)
    {
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
        var previous = _dataSource;
        var next = new GSpotDataSource(_dataBus, options);
        GSpotButton.IsEnabled = false;
        UdpSourceButton.IsEnabled = false;

        try
        {
            await next.StartAsync();

            // StartAsync 立即返回；必须等到 Connected，或首轮失败进入 Reconnecting。
            var connected = await WaitForDataSourceConnectedAsync(
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
            _dataSource = null;
            if (previous is not null)
            {
                try
                {
                    await previous.StopAsync();
                }
                catch
                {
                    try { previous.Dispose(); } catch { /* ignore */ }
                }
            }

            _dataSource = next;
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
        try
        {
            // 已是 UDP 且正在监听：提示即可。
            // Faulted（例如端口占用）时允许原地重试 StartAsync。
            if (_dataSource is UdpReceiver existingUdp)
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

            var next = new UdpReceiver(_dataBus);
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
        var old = _dataSource;
        _dataSource = null;

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

        _dataSource = next;
        await next.StartAsync();
    }

    private static async Task<bool> WaitForDataSourceConnectedAsync(
        IDataSource source,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (source.State == DataSourceState.Connected)
                return true;

            if (source.State == DataSourceState.Faulted)
                return false;

            // Reconnecting 且已有 LastError：首轮已失败，不必空等满超时
            if (source is GSpotDataSource gspot &&
                source.State == DataSourceState.Reconnecting &&
                !string.IsNullOrWhiteSpace(gspot.LastError))
            {
                // 再给一次瞬间机会，避免刚写下 LastError 时误判
                await Task.Delay(400);
                if (source.State == DataSourceState.Connected)
                    return true;
                return false;
            }

            await Task.Delay(200);
        }

        return source.State == DataSourceState.Connected;
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

        DashboardPanelControl.SetValues(
            speedKph: sample.SpeedKph,
            longitudinalAcceleration: sample.LongitudinalAcceleration,
            lateralAcceleration: sample.LateralAcceleration,
            yawRate: sample.YawRate,
            steeringAngleDeg: 0.0,
            cursorFrozen: cursorSample is not null);

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
            _dataSource;

        if (source is null)
            return;

        var stats = source.Stats;

        ReceivedPacketsText.Text =
            stats.Received.ToString();

        ValidPacketsText.Text =
            stats.Valid.ToString();

        LostPacketsText.Text =
            stats.Lost.ToString();

        OutOfOrderPacketsText.Text =
            stats.OutOfOrder.ToString();

        UpdateConnectionStatusUi(source);
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
                _latestSample =
                    sample;


                _sampleHistory.Add(
                    sample);


                if (_sampleHistory.Count >
                    MaxHistorySamples)
                {
                    _sampleHistory.RemoveAt(0);
                }


                Interlocked.Increment(
                    ref _sampleCount);


                // 只有正在录制时才写入文件。
                //
                // 暂停时刻意让"经过时间"按真实时间继续走：
                // VboRecorder 内部以第一条数据为原点，
                // 恢复后第一行的 Elapsed_time 会自然跳过暂停时长。
                if (_recordingState ==
                    RecordingState.Recording)
                {
                    _recordingStartTimestamp ??=
                        sample.Timestamp;

                    _recorder?.TryWrite(
                        sample);
                }
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

        _dataSource =
            new UdpReceiver(
                _dataBus);

        try
        {
            await _dataSource.StartAsync();
        }
        catch (Exception ex)
        {
            // 端口占用等绑定失败：弹窗提示，窗口继续可用（可切 GSpot 或稍后点 UDP 重试）。
            var detail = _dataSource is UdpReceiver udp &&
                         !string.IsNullOrWhiteSpace(udp.LastError)
                ? udp.LastError
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
    }


    // ============================================================
    // 程序关闭
    // ============================================================

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
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
        _dataSource?.Dispose();
        _dataSource = null;


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

        _recorder?.Dispose();


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

        /// <summary>最近一次绘制的 X / 各通道 Y，供光标插值。</summary>
        public double[]? LastXs { get; set; }

        /// <summary>与 LastXs 对齐的样本缓存，供 Dashboard 冻结读数。</summary>
        public VehicleSample[]? LastSamples { get; set; }

        public List<(PlotSignal Signal, double[] Ys)> LastChannelSeries { get; }
            = new();

        public bool IsTimeAxis { get; set; }
    }


    // ============================================================
    // Channel 数据结构
    // ============================================================

    private sealed class ChannelDefinition
    {
        public PlotSignal Signal { get; set; }
    }


    // ============================================================
    // 可选数据
    // ============================================================

    private enum PlotSignal
    {
        Time,

        Speed,

        LongitudinalAcceleration,

        LateralAcceleration,

        VerticalAcceleration,

        YawRate,

        Heading,

        Latitude,

        Longitude,

        Altitude
    }
}