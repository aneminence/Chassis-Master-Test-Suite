using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using Chassis_Master_Test_Suite.Communication;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;
using Chassis_Master_Test_Suite.Simulator;

namespace Chassis_Master_Test_Suite;

public partial class MainWindow : Window
{
    private readonly DataBus _dataBus = new();

    private UdpReceiver? _udpReceiver;
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

                // 坐标轴 / 刻度文字
                AxisColor =
                    ScottPlot.Color.FromHex("#8A94A6"),

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


        scottPlot.Clear();


        // ========================================================
        // 数据不足
        // ========================================================

        if (history.Count < 2)
        {
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
                GetSignalDisplayName(
                    selectedXSignal));

            scottPlot.YLabel(
                "Value");

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
                GetSignalValue(
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
        }


        // ========================================================
        // 标题 / 坐标轴
        // ========================================================

        scottPlot.Title(
            plot.Name);


        scottPlot.XLabel(
            $"{GetSignalDisplayName(selectedXSignal)} " +
            $"({GetSignalUnit(selectedXSignal)})");


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
                var currentLimits =
                    scottPlot.Axes.GetLimits();


                scottPlot.Axes.SetLimits(
                    minX - xPadding,
                    maxX + xPadding,
                    currentLimits.Bottom,
                    currentLimits.Top);
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


                var currentLimits =
                    scottPlot.Axes.GetLimits();


                scottPlot.Axes.SetLimits(
                    currentLimits.Left,
                    currentLimits.Right,
                    minY - yPadding,
                    maxY + yPadding);
            }
        }


        // ScottPlot 5 的 Clear() 会把样式复位，
        // 所以每次重建曲线后都重新套一遍深色主题。
        ApplyDarkPlotStyle(
            plot.WpfPlot);


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
                "s",

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
        }


        RefreshPlotContainer();

        RefreshAllPlots();
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
        var sample =
            _latestSample;

        if (sample is null)
            return;

        // ------------------------------------------------------------
        // 数值显示已迁移到 Controls\DashboardPanel。
        //
        // 注意：VehicleSample 目前没有 SteeringAngleDeg 字段，
        // 也没有对应的 UDP 数据字段，所以方向盘角度暂时显示 0。
        // 等协议扩展后，把 sample.SteeringAngleDeg 传进来即可。
        // ------------------------------------------------------------

        DashboardPanelControl.SetValues(
            speedKph: sample.SpeedKph,
            longitudinalAcceleration: sample.LongitudinalAcceleration,
            lateralAcceleration: sample.LateralAcceleration,
            yawRate: sample.YawRate,
            steeringAngleDeg: 0.0);
    }


    private void UpdateNetworkDisplay()
    {
        var receiver =
            _udpReceiver;

        if (receiver is null)
            return;


        ReceivedPacketsText.Text =
            receiver.ReceivedPackets.ToString();


        ValidPacketsText.Text =
            receiver.ValidPackets.ToString();


        LostPacketsText.Text =
            receiver.LostPackets.ToString();


        OutOfOrderPacketsText.Text =
            receiver.OutOfOrderPackets.ToString();
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
        // UDP Receiver
        // ========================================================

        _udpReceiver =
            new UdpReceiver(
                _dataBus);


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
        // 启动 UDP 接收
        //
        // UdpReceiver 不使用 CancellationToken，
        // 关闭时由 Dispose() 关闭 socket 自然退出。
        // ========================================================

        _ = Task.Run(() =>
            _udpReceiver.RunAsync());


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


        await Task.CompletedTask;
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
        // 1. 先关闭 UDP socket，
        //    让接收循环通过 ObjectDisposedException 自然退出。
        // 2. 再 Complete DataBus，
        //    让数据消费循环通过 WaitToReadAsync 返回 false 退出。
        // 3. 最后取消 CancellationTokenSource。
        //
        // 这样关闭过程中不会产生 OperationCanceledException。
        // ========================================================

        _udpReceiver?.Dispose();


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