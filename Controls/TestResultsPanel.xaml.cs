using Chassis_Master_Test_Suite.Themes;
using Chassis_Master_Test_Suite.Localization;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;

using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;
using Microsoft.Win32;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// Test Results: Accel / Decel / Custom across all open VBOs + checkbox compare.
/// </summary>
public partial class TestResultsPanel : UserControl
{

    // Must match MainWindow.RunAnnotationPalette / TrackMapPanel.RunHighlightPalette (chart spans = map).
    private static readonly string[] RunAnnotationPalette =
    {
        "#3FBF6F", "#4A9FD8", "#C06AD8", "#E08A4A",
        "#5AC8C8", "#E05252", "#D8D84A", ThemeBrushes.Hex("AccentBrush", "#C8A34A")
    };
    private readonly ObservableCollection<ResultRowVm> _rows = new();

    private IReadOnlyList<TestRunResult> _lastResults =
        Array.Empty<TestRunResult>();

    private IReadOnlyList<AnnotatedTestRun> _lastAnnotated =
        Array.Empty<AnnotatedTestRun>();

    private TestDefinition? _lastDefinition;

    private readonly ObservableCollection<PassConditionRowVm> _passConditions = new();
    private readonly ObservableCollection<SectorGateRowVm> _sectorGates = new();
    private readonly List<DataGridColumn> _sectorColumns = new();

    private bool _conditionsCollapsed;
    private GridLength _savedConditionsHeight = new(200);
    private bool _suppressSelectAllSync;

    private bool _suppressTypeDefaults;

    private static readonly TimeZoneInfo BeijingTimeZone = ResolveBeijingTimeZone();

    /// <summary>Legacy single-buffer hook (used if GetSampleSources is null).</summary>
    public Func<IReadOnlyList<VehicleSample>>? GetSamples { get; set; }

    /// <summary>Preferred: every open VBO / live buffer with labels.</summary>
    public Func<IReadOnlyList<SampleSource>>? GetSampleSources { get; set; }

    /// <summary>Compute finished: annotated runs (each carries its own samples).</summary>
    public event Action<IReadOnlyList<AnnotatedTestRun>>? ResultsAnnotated;

    /// <summary>Focused DataGrid row (single selection).</summary>
    public event Action<int?>? SelectedRunChanged;

    /// <summary>Rows whose checkbox is checked (0..N) for overlay compare.</summary>
    public event Action<IReadOnlyList<AnnotatedTestRun>>? CheckedRunsChanged;

    /// <summary>User asked to edit session metadata.</summary>
    public event Action? SessionEditRequested;

    /// <summary>Maths channel store changed 鈥?MainWindow should refresh channel lists.</summary>
    public event Action? MathsChanged;

    public TestResultsPanel()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _rows;
        ResultsGrid.SelectionChanged += ResultsGrid_SelectionChanged;
        PopulateChannelCombo();
        RefreshPassRowChannelOptions();
        MathsChannelStore.Instance.Changed += (_, _) =>
        {
            void Refresh()
            {
                PopulateChannelCombo();
                RefreshPassRowChannelOptions();
            }
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Refresh);
                return;
            }
            Refresh();
        };
        ChannelRegistry.Instance.AvailableChanged += (_, _) =>
        {
            void Refresh()
            {
                PopulateChannelCombo();
                RefreshPassRowChannelOptions();
            }
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Refresh);
                return;
            }
            Refresh();
        };
        GateStore.Instance.Changed += (_, _) =>
        {
            UpdateGateStatusHint();
            RefreshAnalysisGateCombos();
            RefreshLapGateChoices();
        };
        ApplyTypeDefaults();
        EnsureDefaultPassCondition();
        if (PassConditionsHost is not null)
            PassConditionsHost.ItemsSource = _passConditions;
        if (SectorGatesHost is not null)
            SectorGatesHost.ItemsSource = _sectorGates;

        Loc.LanguageChanged += (_, _) =>
        {
            if (!Dispatcher.CheckAccess())
                Dispatcher.Invoke(ApplyLocalizedChrome);
            else
                ApplyLocalizedChrome();
        };
        ApplyLocalizedChrome();
    }

    private void ApplyLocalizedChrome()
    {
        if (TitleText is not null)
            TitleText.Text = Loc.T("Results.Title");
        if (ToggleConditionsButton is not null)
        {
            ToggleConditionsButton.Content = Loc.T(
                _conditionsCollapsed ? "Results.ShowConditions" : "Results.HideConditions");
            ToggleConditionsButton.ToolTip = Loc.T("Results.ToggleConditionsTip");
        }
        if (SessionButton is not null)
        {
            SessionButton.Content = Loc.T("Results.Session");
            SessionButton.ToolTip = Loc.T("Results.SessionTip");
        }
        if (TypeLabel is not null)
            TypeLabel.Text = Loc.T("Results.Type");
        if (ChannelLabel is not null)
            ChannelLabel.Text = Loc.T("Results.Channel");
        if (TypeAccelItem is not null)
            TypeAccelItem.Content = Loc.T("Results.Accel");
        if (TypeDecelItem is not null)
            TypeDecelItem.Content = Loc.T("Results.Decel");
        if (TypeCustomItem is not null)
            TypeCustomItem.Content = Loc.T("Results.Custom");
        if (TypeGateItem is not null)
            TypeGateItem.Content = Loc.T("Results.Gate");
        if (TypeLapTimeItem is not null)
            TypeLapTimeItem.Content = Loc.T("Results.LapTime");
        if (StartConditionsLabel is not null)
            StartConditionsLabel.Text = Loc.T("Results.StartConditions");
        if (EndConditionsLabel is not null)
            EndConditionsLabel.Text = Loc.T("Results.EndConditions");
        if (PassConditionsLabel is not null)
            PassConditionsLabel.Text = Loc.T("Results.PassConditions");
        if (StartWhenLabel is not null)
            StartWhenLabel.Text = Loc.T("Results.StartWhen");
        if (EndWhenLabel is not null)
            EndWhenLabel.Text = Loc.T("Results.EndWhen");
        if (AddPassButton is not null)
        {
            AddPassButton.Content = Loc.T("Results.AddPass");
            AddPassButton.ToolTip = Loc.T("Results.AddPassTip");
        }
        if (StartLabel is not null)
            StartLabel.Text = Loc.T("Results.Start");
        if (EndLabel is not null)
            EndLabel.Text = Loc.T("Results.End");
        if (ComputeButton is not null)
        {
            ComputeButton.Content = Loc.T("Results.Compute");
            ComputeButton.ToolTip = Loc.T("Results.ComputeTip");
        }
        if (ExportCsvButton is not null)
        {
            ExportCsvButton.Content = Loc.T("Results.ExportCsv");
            ExportCsvButton.ToolTip = Loc.T("Results.ExportCsvTip");
        }
        if (MathsChannelsButton is not null)
        {
            MathsChannelsButton.Content = Loc.T("Results.MathsChannels");
            MathsChannelsButton.ToolTip = Loc.T("Results.MathsChannelsTip");
        }
        if (StatusText is not null)
            StatusText.Text = Loc.T("Results.StatusHint");
        if (FooterHintText is not null)
            FooterHintText.Text = Loc.T("Results.FooterHint");
        if (ConditionsSplitter is not null)
            ConditionsSplitter.ToolTip = Loc.T("Results.SplitterTip");
        if (SelectAllCheckBox is not null)
            SelectAllCheckBox.ToolTip = Loc.T("Results.SelectAllTip");

        if (ColSource is not null)
            ColSource.Header = Loc.T("Results.Col.Source");
        if (ColStart is not null)
            ColStart.Header = Loc.T("Results.Col.Start");
        if (ColEnd is not null)
            ColEnd.Header = Loc.T("Results.Col.End");
        if (ColDuration is not null)
            ColDuration.Header = Loc.T(GetSelectedTestType() == TestType.LapTime
                ? "Results.Col.LapTime" : "Results.Col.Duration");
        if (ColDeltaV is not null)
            ColDeltaV.Header = Loc.T("Results.Col.DeltaV");
        if (ColDist is not null)
            ColDist.Header = Loc.T("Results.Col.Dist");
        if (ColPass is not null)
            ColPass.Header = Loc.T("Results.Col.Pass");
        if (ColPassValues is not null)
            ColPassValues.Header = Loc.T("Results.Col.PassValues");
    }

    private void PopulateChannelCombo()
    {
        if (ChannelCombo is null)
            return;

        var prev = (ChannelCombo.SelectedItem as ComboBoxItem)?.Tag as string;

        ChannelCombo.Items.Clear();
        foreach (var (id, label) in BuildAvailableChannelChoices())
        {
            ChannelCombo.Items.Add(new ComboBoxItem
            {
                Content = label,
                Tag = id
            });
        }

        ComboBoxItem? match = null;
        foreach (ComboBoxItem it in ChannelCombo.Items)
        {
            if (it.Tag is string tag &&
                string.Equals(tag, prev, StringComparison.OrdinalIgnoreCase))
            {
                match = it;
                break;
            }
        }

        if (match is null && !string.IsNullOrWhiteSpace(prev))
        {
            // Keep previously saved channel visible even if not in current data yet.
            ChannelCombo.Items.Insert(0, new ComboBoxItem
            {
                Content = FormatChannelLabel(prev),
                Tag = prev
            });
            match = (ComboBoxItem)ChannelCombo.Items[0];
        }

        ChannelCombo.SelectedItem = match ?? (ChannelCombo.Items.Count > 0 ? ChannelCombo.Items[0] : null);
    }

    /// <summary>
    /// Channel ids currently available in live/VBO data (+ maths not yet merged).
    /// </summary>
    private static List<(string Id, string Label)> BuildAvailableChannelChoices()
    {
        var list = new List<(string Id, string Label)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ch in ChannelRegistry.Instance.AvailablePlotChannels)
        {
            if (ch is null || string.IsNullOrWhiteSpace(ch.Id))
                continue;
            if (!seen.Add(ch.Id))
                continue;

            var unit = string.IsNullOrWhiteSpace(ch.Unit) ? "" : $" ({ch.Unit})";
            list.Add((ch.Id, $"{ch.DisplayName}{unit}"));
        }

        foreach (var d in MathsChannelStore.Instance.Definitions)
        {
            if (string.IsNullOrWhiteSpace(d.Id) || !seen.Add(d.Id))
                continue;

            var name = string.IsNullOrWhiteSpace(d.DisplayName) ? d.Id : d.DisplayName;
            var unit = string.IsNullOrWhiteSpace(d.Unit) ? "" : $" ({d.Unit})";
            list.Add((d.Id, $"{name}{unit}  [maths]"));
        }

        return list;
    }

    private static string FormatChannelLabel(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return channelId;

        if (ChannelRegistry.Instance.TryGet(channelId, out var info))
        {
            var unit = string.IsNullOrWhiteSpace(info.Unit) ? "" : $" ({info.Unit})";
            return $"{info.DisplayName}{unit}";
        }

        foreach (var d in MathsChannelStore.Instance.Definitions)
        {
            if (!string.Equals(d.Id, channelId, StringComparison.OrdinalIgnoreCase))
                continue;
            var name = string.IsNullOrWhiteSpace(d.DisplayName) ? d.Id : d.DisplayName;
            var unit = string.IsNullOrWhiteSpace(d.Unit) ? "" : $" ({d.Unit})";
            return $"{name}{unit}  [maths]";
        }

        return channelId;
    }

    private void RefreshPassRowChannelOptions(PassConditionRowVm? only = null)
    {
        var choices = BuildAvailableChannelChoices();

        void Apply(PassConditionRowVm row)
        {
            var prev = row.ChannelId;
            row.ChannelOptions.Clear();
            foreach (var (id, label) in choices)
                row.ChannelOptions.Add(new ChannelPickItem(id, label));

            if (!string.IsNullOrWhiteSpace(prev) &&
                choices.Any(c => string.Equals(c.Id, prev, StringComparison.OrdinalIgnoreCase)))
            {
                row.ChannelId = prev!;
                return;
            }

            if (!string.IsNullOrWhiteSpace(prev) &&
                !choices.Any(c => string.Equals(c.Id, prev, StringComparison.OrdinalIgnoreCase)))
            {
                // Keep orphan selection visible until data catches up / user changes it.
                row.ChannelOptions.Insert(0, new ChannelPickItem(prev!, FormatChannelLabel(prev!)));
                row.ChannelId = prev!;
                return;
            }

            var prefer = choices.FirstOrDefault(c =>
                string.Equals(c.Id, ChannelIds.Velocity, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(prefer.Id))
                row.ChannelId = prefer.Id;
            else if (choices.Count > 0)
                row.ChannelId = choices[0].Id;
        }

        if (only is not null)
            Apply(only);
        else
        {
            foreach (var row in _passConditions)
                Apply(row);
        }
    }

    private void TestTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressTypeDefaults)
            return;

        ApplyTypeDefaults();
    }

    private void ApplyTypeDefaults()
    {
        var type = GetSelectedTestType();
        var isCustom = type == TestType.Custom;

        ChannelLabel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        ChannelCombo.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        if (GatePickPanel is not null)
            GatePickPanel.Visibility = type == TestType.Gate ? Visibility.Visible : Visibility.Collapsed;
        if (LapTimePanel is not null)
            LapTimePanel.Visibility = type == TestType.LapTime ? Visibility.Visible : Visibility.Collapsed;
        if (ThresholdPanel is not null)
            ThresholdPanel.Visibility = type == TestType.LapTime ? Visibility.Collapsed : Visibility.Visible;
        if (type == TestType.Gate)
            RefreshAnalysisGateCombos();
        if (type == TestType.LapTime)
            RefreshLapGateChoices();
        UpdateResultColumnMode(type);

        switch (type)
        {
            case TestType.Accel:
                StartThresholdBox.Text = "0";
                EndThresholdBox.Text = "100";
                StartLabel.Text = "Start";
                EndLabel.Text = "End";
                break;
            case TestType.Decel:
                StartThresholdBox.Text = "100";
                EndThresholdBox.Text = "0";
                StartLabel.Text = "Start";
                EndLabel.Text = "End";
                break;
            case TestType.Custom:
                StartLabel.Text = "Start thr";
                EndLabel.Text = "End thr";
                break;
            case TestType.Gate:
                StartLabel.Text = "Min kph";
                EndLabel.Text = "Max kph";
                StartThresholdBox.Text = "";
                EndThresholdBox.Text = "";
                break;
            case TestType.LapTime:
                StartLabel.Text = "";
                EndLabel.Text = "";
                StartThresholdBox.Text = "";
                EndThresholdBox.Text = "";
                break;
        }
    }

    private void ComputeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildDefinition(out var definition, out var error))
        {
            StatusText.Text = error;
            StatusText.Foreground = ThemeBrushes.Accent();
            return;
        }

        var sources = ResolveSources();
        if (sources.Count == 0)
        {
            StatusText.Text = "No sample sources wired (open a VBO or wait for live history).";
            StatusText.Foreground = ThemeBrushes.Accent();
            return;
        }

        var annotated = new List<AnnotatedTestRun>();
        var flat = new List<TestRunResult>();
        var globalRun = 1;
        var scannedPoints = 0;
        var scannedFiles = 0;

        foreach (var source in sources)
        {
            if (source.Samples.Count < 2)
                continue;

            scannedFiles++;
            scannedPoints += source.Samples.Count;

            IReadOnlyList<TestRunResult> raw;
            if (definition.Type == TestType.LapTime)
            {
                var lapGate = GetSelectedLapGate();
                var sectorGates = GetSelectedSectorGates();
                if (lapGate is null || !lapGate.IsValid)
                {
                    StatusText.Text = "Select a valid Start / Finish gate.";
                    StatusText.Foreground = ThemeBrushes.Accent();
                    return;
                }
                if (sectorGates.Any(gate => gate.Id == lapGate.Id))
                {
                    StatusText.Text = "Start / Finish cannot also be a sector gate.";
                    StatusText.Foreground = ThemeBrushes.Accent();
                    return;
                }
                if (sectorGates.Select(gate => gate.Id).Distinct().Count() != sectorGates.Count)
                {
                    StatusText.Text = "Each sector gate can only be used once.";
                    StatusText.Foreground = ThemeBrushes.Accent();
                    return;
                }
                raw = LapTimeEngine.Compute(
                    MathsEnricher.Apply(source.Samples, MathsChannelStore.Instance.Definitions),
                    lapGate,
                    sectorGates);
            }
            else if (definition.Type == TestType.Gate)
            {
                var startGate = GateStore.Instance.StartGate;
                var endGate = GateStore.Instance.EndGate;
                if (startGate is null || endGate is null || !startGate.IsValid || !endGate.IsValid)
                {
                    StatusText.Text = "Set Start When + End When gates (place on Track Map first).";
                    StatusText.Foreground = ThemeBrushes.Accent();
                    return;
                }

                var passConds = BuildPassConditions();
                raw = GateRunEngine.Compute(
                    MathsEnricher.Apply(source.Samples, MathsChannelStore.Instance.Definitions),
                    startGate,
                    endGate,
                    passConds,
                    id => GateStore.Instance.Find(id));
            }
            else
            {
                raw = SpeedToSpeedEngine.Compute(MathsEnricher.Apply(source.Samples, MathsChannelStore.Instance.Definitions), definition);
            }
            foreach (var r in raw)
            {
                var tagged = new TestRunResult
                {
                    RunNumber = globalRun++,
                    SourceFile = source.Label,
                    SourceColorHex = source.ColorHex,
                    SourceFileId = source.Id,
                    StartTimestampMs = r.StartTimestampMs,
                    EndTimestampMs = r.EndTimestampMs,
                    DurationSeconds = r.DurationSeconds,
                    StartSpeedKph = r.StartSpeedKph,
                    EndSpeedKph = r.EndSpeedKph,
                    DeltaSpeedKph = r.DeltaSpeedKph,
                    DistanceMeters = r.DistanceMeters,
                    DistanceMethod = r.DistanceMethod,
                    Pass = r.Pass,
                    FailReason = r.FailReason,
                    PassMeasurements = r.PassMeasurements,
                    SectorTimes = r.SectorTimes,
                    StartSampleIndex = r.StartSampleIndex,
                    EndSampleIndex = r.EndSampleIndex
                };

                flat.Add(tagged);
                annotated.Add(new AnnotatedTestRun
                {
                    Result = tagged,
                    Samples = source.Samples,
                    SourceLabel = source.Label,
                    // Per-run color = chart HorizontalSpan / TrackMap (not file chip color).
                    ColorHex = RunAnnotationPalette[(tagged.RunNumber - 1) % RunAnnotationPalette.Length],
                    SourceFileId = source.Id
                });
            }
        }

        _lastResults = flat;
        _lastAnnotated = annotated;
        _lastDefinition = definition;

        _rows.Clear();
        foreach (var a in annotated)
        {
            var row = ResultRowVm.From(a, BeijingTimeZone);
            row.PropertyChanged += ResultRow_PropertyChanged;
            _rows.Add(row);
        }
        ApplyFastestHighlights();
        RebuildSectorColumns();

        ExportCsvButton.IsEnabled = flat.Count > 0;

        ResultsAnnotated?.Invoke(annotated);
        RaiseCheckedRunsChanged();
        SyncSelectAllCheckBox();

        if (annotated.Count > 0)
        {
            ResultsGrid.SelectedIndex = 0;
            SelectedRunChanged?.Invoke(annotated[0].RunNumber);
        }
        else
        {
            SelectedRunChanged?.Invoke(null);
        }

        StatusText.Text = flat.Count == 0
            ? $"Scanned {scannedFiles} file(s) / {scannedPoints} pts 鈥?no runs. Check thresholds."
            : $"Found {flat.Count} run(s) across {scannedFiles} file(s) ({scannedPoints} pts). Check rows to compare.";
        StatusText.Foreground = ThemeBrushes.TextSecondary();
    }

    private IReadOnlyList<SampleSource> ResolveSources()
    {
        if (GetSampleSources is not null)
        {
            var list = GetSampleSources() ?? Array.Empty<SampleSource>();
            if (list.Count > 0)
                return list;
        }

        if (GetSamples is not null)
        {
            var samples = GetSamples() ?? Array.Empty<VehicleSample>();
            if (samples.Count > 0)
            {
                return new[]
                {
                    new SampleSource
                    {
                        Label = "History",
                        ColorHex = ThemeBrushes.Hex("AccentBrush", "#C8A34A"),
                        Samples = samples
                    }
                };
            }
        }

        return Array.Empty<SampleSource>();
    }

    private void ResultRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultRowVm.IsChecked))
        {
            RaiseCheckedRunsChanged();
            SyncSelectAllCheckBox();
        }
    }

    private void RaiseCheckedRunsChanged()
    {
        var checkedRuns = _rows
            .Where(r => r.IsChecked)
            .Select(r => r.Annotated)
            .ToList();
        CheckedRunsChanged?.Invoke(checkedRuns);
    }

    private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResults.Count == 0)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export Test Results CSV",
            Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"CMTS_TestResults_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            TestResultsCsvExporter.WriteFile(dialog.FileName, _lastResults, _lastDefinition);
            StatusText.Text = $"Exported: {Path.GetFileName(dialog.FileName)}";
            StatusText.Foreground = ThemeBrushes.TextSecondary();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
            StatusText.Foreground = ThemeBrushes.Accent();
        }
    }

    private bool TryBuildDefinition(out TestDefinition definition, out string error)
    {
        definition = null!;
        error = "";

        var type = GetSelectedTestType();
        if (type == TestType.Gate)
        {
            definition = TestDefinition.Gate();
            return true;
        }
        if (type == TestType.LapTime)
        {
            definition = TestDefinition.LapTime();
            return true;
        }

        if (!TryParseThreshold(StartThresholdBox.Text, out var start))
        {
            error = "Invalid Start threshold.";
            return false;
        }

        if (!TryParseThreshold(EndThresholdBox.Text, out var end))
        {
            error = "Invalid End threshold.";
            return false;
        }

        switch (type)
        {
            case TestType.Accel:
                if (end <= start)
                {
                    error = "Accel requires End > Start.";
                    return false;
                }

                definition = TestDefinition.Accel(start, end);
                return true;

            case TestType.Decel:
                if (start <= end)
                {
                    error = "Decel requires Start > End.";
                    return false;
                }

                definition = TestDefinition.Decel(start, end);
                return true;

            case TestType.Custom:
                var channelId = ChannelIds.Velocity;
                if (ChannelCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                    channelId = tag;

                if (Math.Abs(end - start) < 1e-9)
                {
                    error = "Custom start/end thresholds must differ.";
                    return false;
                }

                definition = TestDefinition.CustomSpeed(start, end, channelId);
                return true;

            case TestType.Gate:
                // Optional Min/Max speed filters are read later in Compute; placeholders OK.
                definition = TestDefinition.Gate();
                return true;
            case TestType.LapTime:
                definition = TestDefinition.LapTime();
                return true;

            default:
                error = "Unknown test type.";
                return false;
        }
    }

    private TestType GetSelectedTestType()
    {
        if (TestTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag switch
            {
                "Decel" => TestType.Decel,
                "Custom" => TestType.Custom,
                "Gate" => TestType.Gate,
                "LapTime" => TestType.LapTime,
                _ => TestType.Accel
            };
        }

        return TestType.Accel;
    }

    private static bool TryParseThreshold(string text, out double value) =>
        double.TryParse(
            text.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value)
        || double.TryParse(
            text.Trim(),
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out value);

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

    private void ToggleConditionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConditionsRowDef is null || SplitterRowDef is null || ConditionsScroll is null)
            return;

        if (!_conditionsCollapsed)
        {
            _savedConditionsHeight = ConditionsRowDef.Height.IsAbsolute && ConditionsRowDef.Height.Value > 0
                ? ConditionsRowDef.Height
                : new GridLength(200);
            ConditionsRowDef.Height = new GridLength(0);
            ConditionsRowDef.MinHeight = 0;
            SplitterRowDef.Height = new GridLength(0);
            ConditionsScroll.Visibility = Visibility.Collapsed;
            if (ConditionsSplitter is not null)
                ConditionsSplitter.Visibility = Visibility.Collapsed;
            _conditionsCollapsed = true;
            if (ToggleConditionsButton is not null)
                ToggleConditionsButton.Content = Loc.T("Results.ShowConditions");
        }
        else
        {
            ConditionsRowDef.Height = _savedConditionsHeight.Value > 0
                ? _savedConditionsHeight
                : new GridLength(200);
            ConditionsRowDef.MinHeight = 0;
            SplitterRowDef.Height = new GridLength(6);
            ConditionsScroll.Visibility = Visibility.Visible;
            if (ConditionsSplitter is not null)
                ConditionsSplitter.Visibility = Visibility.Visible;
            _conditionsCollapsed = false;
            if (ToggleConditionsButton is not null)
                ToggleConditionsButton.Content = Loc.T("Results.HideConditions");
        }
    }

    /// <summary>
    /// Single-click toggle: DataGrid otherwise eats the first click for row selection.
    /// </summary>
    private void RowCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not CheckBox cb)
            return;

        // Toggle immediately; prevent DataGrid from consuming the click for selection first.
        cb.IsChecked = !(cb.IsChecked == true);
        e.Handled = true;
    }

    private void SelectAllCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SelectAllCheckBox is null)
            return;

        e.Handled = true;
        _suppressSelectAllSync = true;
        try
        {
            // None or partial -> select all; all selected -> clear.
            var target = SelectAllCheckBox.IsChecked != true;
            foreach (var row in _rows)
                row.IsChecked = target;
            SelectAllCheckBox.IsChecked = _rows.Count > 0 && target;
        }
        finally
        {
            _suppressSelectAllSync = false;
        }

        RaiseCheckedRunsChanged();
    }

    private void SyncSelectAllCheckBox()
    {
        if (_suppressSelectAllSync || SelectAllCheckBox is null)
            return;

        if (_rows.Count == 0)
        {
            SelectAllCheckBox.IsChecked = false;
            return;
        }

        var n = _rows.Count(r => r.IsChecked);
        if (n == 0)
            SelectAllCheckBox.IsChecked = false;
        else if (n == _rows.Count)
            SelectAllCheckBox.IsChecked = true;
        else
            SelectAllCheckBox.IsChecked = null;
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is ResultRowVm row)
            SelectedRunChanged?.Invoke(row.RunNumber);
        else if (_rows.Count == 0)
            SelectedRunChanged?.Invoke(null);
    }




    /// <summary>Snapshot current Test Results UI for session memory.</summary>
    public TestResultsSettingsDto CaptureSettings()
    {
        var dto = new TestResultsSettingsDto
        {
            Type = GetSelectedTestType() switch
            {
                TestType.Decel => "Decel",
                TestType.Custom => "Custom",
                TestType.Gate => "Gate",
                TestType.LapTime => "LapTime",
                _ => "Accel"
            },
            StartThreshold = StartThresholdBox?.Text ?? "",
            EndThreshold = EndThresholdBox?.Text ?? "",
            ConditionsCollapsed = _conditionsCollapsed
        };

        dto.LapGateId = (LapGateCombo?.SelectedItem as GatePickItem)?.Id;
        dto.SectorGateIds = _sectorGates.Where(row => row.GateId is Guid)
            .Select(row => row.GateId!.Value).ToList();

        if (ChannelCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            dto.ChannelId = tag;

        foreach (var row in _passConditions)
        {
            dto.PassConditions.Add(new PassConditionSettingsDto
            {
                ChannelId = row.ChannelId,
                MinText = row.MinText,
                MaxText = row.MaxText,
                AtGateId = row.AtGateId
            });
        }

        return dto;
    }

    /// <summary>Restore Test Results UI from session memory (gates should already be loaded).</summary>
    public void ApplySettings(TestResultsSettingsDto? dto)
    {
        if (dto is null || TestTypeCombo is null)
            return;

        _suppressTypeDefaults = true;
        try
        {
            ComboBoxItem? match = null;
            foreach (ComboBoxItem it in TestTypeCombo.Items)
            {
                if (it.Tag is string tag &&
                    string.Equals(tag, dto.Type, StringComparison.OrdinalIgnoreCase))
                {
                    match = it;
                    break;
                }
            }
            if (match is not null)
                TestTypeCombo.SelectedItem = match;

            // Visibility for Custom / Gate without overwriting thresholds.
            var type = GetSelectedTestType();
            var isCustom = type == TestType.Custom;
            if (ChannelLabel is not null)
                ChannelLabel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            if (ChannelCombo is not null)
                ChannelCombo.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            if (GatePickPanel is not null)
                GatePickPanel.Visibility = type == TestType.Gate ? Visibility.Visible : Visibility.Collapsed;
            if (LapTimePanel is not null)
                LapTimePanel.Visibility = type == TestType.LapTime ? Visibility.Visible : Visibility.Collapsed;
            if (ThresholdPanel is not null)
                ThresholdPanel.Visibility = type == TestType.LapTime ? Visibility.Collapsed : Visibility.Visible;
            UpdateResultColumnMode(type);

            switch (type)
            {
                case TestType.Accel:
                case TestType.Decel:
                    StartLabel.Text = "Start";
                    EndLabel.Text = "End";
                    break;
                case TestType.Custom:
                    StartLabel.Text = "Start thr";
                    EndLabel.Text = "End thr";
                    break;
                case TestType.Gate:
                    StartLabel.Text = "Min kph";
                    EndLabel.Text = "Max kph";
                    break;
                case TestType.LapTime:
                    StartLabel.Text = "";
                    EndLabel.Text = "";
                    break;
            }

            if (StartThresholdBox is not null)
                StartThresholdBox.Text = dto.StartThreshold ?? "";
            if (EndThresholdBox is not null)
                EndThresholdBox.Text = dto.EndThreshold ?? "";

            if (!string.IsNullOrWhiteSpace(dto.ChannelId) && ChannelCombo is not null)
            {
                ComboBoxItem? ch = null;
                foreach (ComboBoxItem it in ChannelCombo.Items)
                {
                    if (it.Tag is string t &&
                        string.Equals(t, dto.ChannelId, StringComparison.OrdinalIgnoreCase))
                    {
                        ch = it;
                        break;
                    }
                }
                if (ch is not null)
                    ChannelCombo.SelectedItem = ch;
            }

            RefreshAnalysisGateCombos();
            RefreshLapGateChoices();
            if (dto.LapGateId is Guid lapId)
                LapGateCombo.SelectedItem = LapGateCombo.Items.Cast<GatePickItem>()
                    .FirstOrDefault(item => item.Id == lapId);
            _sectorGates.Clear();
            foreach (var sectorId in dto.SectorGateIds ?? new List<Guid>())
            {
                if (GateStore.Instance.Find(sectorId) is null)
                    continue;
                var row = new SectorGateRowVm { GateId = sectorId };
                FillSectorChoices(row);
                _sectorGates.Add(row);
            }
            RenumberSectors();

            _passConditions.Clear();
            if (dto.PassConditions is { Count: > 0 })
            {
                foreach (var p in dto.PassConditions)
                {
                    var row = CreatePassRow();
                    row.ChannelId = string.IsNullOrWhiteSpace(p.ChannelId) ? ChannelIds.Velocity : p.ChannelId;
                    row.MinText = p.MinText ?? "78";
                    row.MaxText = p.MaxText ?? "83";
                    if (p.AtGateId is Guid gid && GateStore.Instance.Find(gid) is not null)
                        row.AtGateId = gid;
                    _passConditions.Add(row);
                }
            }
            else
            {
                EnsureDefaultPassCondition();
            }
            RefreshPassRowChannelOptions();
            RefreshPassRowGateItems();

            // Collapse state
            if (dto.ConditionsCollapsed != _conditionsCollapsed && ToggleConditionsButton is not null)
                ToggleConditionsButton_Click(ToggleConditionsButton, new RoutedEventArgs());
        }
        finally
        {
            _suppressTypeDefaults = false;
            UpdateGateStatusHint();
        }
    }


    public void RefreshSessionSummary()
    {
        if (StatusText is null)
            return;
        // Keep compute hint; session shown when user opens Session dialog / after VBO load.
        var line = Chassis_Master_Test_Suite.Session.SessionMetadata.Current.SummaryLine();
        if (GetSelectedTestType() != TestType.Gate)
            StatusText.Text = $"Session: {line}";
    }


    private void EnsureDefaultPassCondition()
    {
        if (_passConditions.Count > 0)
            return;
        _passConditions.Add(CreatePassRow());
    }

    private PassConditionRowVm CreatePassRow()
    {
        var row = new PassConditionRowVm();
        row.RemoveRequested += r =>
        {
            if (_passConditions.Count <= 1)
                return;
            _passConditions.Remove(r);
        };
        RefreshPassRowChannelOptions(row);
        RefreshPassRowGateItems(row);
        return row;
    }

    private void AddPassCondition_Click(object sender, RoutedEventArgs e)
    {
        _passConditions.Add(CreatePassRow());
    }

    private void RemovePassCondition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PassConditionRowVm row)
            return;
        if (_passConditions.Count <= 1)
            return;
        _passConditions.Remove(row);
    }

    private void RefreshPassRowGateItems(PassConditionRowVm? only = null)
    {
        var items = GateStore.Instance.Gates
            .Select(g => new GatePickItem(g.Id, g.Name))
            .ToList();
        void Apply(PassConditionRowVm row)
        {
            var prev = row.AtGateId;
            row.GateChoices.Clear();
            foreach (var it in items)
                row.GateChoices.Add(it);
            if (prev is Guid id && items.Any(i => i.Id == id))
                row.AtGateId = id;
            else if (items.Count > 0)
                row.AtGateId = items[0].Id;
            else
                row.AtGateId = null;
        }

        if (only is not null)
            Apply(only);
        else
        {
            foreach (var row in _passConditions)
                Apply(row);
        }
    }

    private List<GatePassCondition> BuildPassConditions()
    {
        var list = new List<GatePassCondition>();
        foreach (var row in _passConditions)
        {
            if (row.AtGateId is not Guid gid)
                continue;
            if (!TryParseThreshold(row.MinText, out var min) ||
                !TryParseThreshold(row.MaxText, out var max))
                continue;
            list.Add(new GatePassCondition
            {
                ChannelId = string.IsNullOrWhiteSpace(row.ChannelId) ? ChannelIds.Velocity : row.ChannelId,
                Min = Math.Min(min, max),
                Max = Math.Max(min, max),
                AtGateId = gid
            });
        }
        return list;
    }

    private bool _suppressAnalysisGateCombo;

    private void RefreshAnalysisGateCombos()
    {
        if (GateStartCombo is null || GateEndCombo is null)
            return;
        _suppressAnalysisGateCombo = true;
        try
        {
            FillGateCombo(GateStartCombo, GateStore.Instance.AnalysisStartId);
            FillGateCombo(GateEndCombo, GateStore.Instance.AnalysisEndId);
            RefreshPassRowGateItems();
        }
        finally
        {
            _suppressAnalysisGateCombo = false;
        }
    }

    private static void FillGateCombo(ComboBox combo, Guid? selectedId)
    {
        combo.Items.Clear();
        combo.Items.Add(new GatePickItem(null, "(none)"));
        foreach (var g in GateStore.Instance.Gates)
            combo.Items.Add(new GatePickItem(g.Id, g.Name));

        object? match = null;
        foreach (GatePickItem it in combo.Items)
        {
            if (it.Id == selectedId)
            {
                match = it;
                break;
            }
        }
        combo.SelectedItem = match ?? combo.Items[0];
    }

    private void GateStartCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnalysisGateCombo)
            return;
        if (GateStartCombo.SelectedItem is GatePickItem item)
            GateStore.Instance.SetAnalysisStart(item.Id);
    }

    private void GateEndCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnalysisGateCombo)
            return;
        if (GateEndCombo.SelectedItem is GatePickItem item)
            GateStore.Instance.SetAnalysisEnd(item.Id);
    }

    private void RefreshLapGateChoices()
    {
        if (LapGateCombo is null)
            return;
        var selected = (LapGateCombo.SelectedItem as GatePickItem)?.Id;
        FillGateCombo(LapGateCombo, selected);
        foreach (var row in _sectorGates)
            FillSectorChoices(row);
        RenumberSectors();
    }

    private void FillSectorChoices(SectorGateRowVm row)
    {
        var selected = row.GateId;
        row.GateChoices.Clear();
        foreach (var gate in GateStore.Instance.Gates)
            row.GateChoices.Add(new GatePickItem(gate.Id, gate.Name));
        row.GateId = selected is Guid id && GateStore.Instance.Find(id) is not null
            ? id
            : row.GateChoices.FirstOrDefault()?.Id;
    }

    private GateDefinition? GetSelectedLapGate() =>
        LapGateCombo?.SelectedItem is GatePickItem item && item.Id is Guid id
            ? GateStore.Instance.Find(id)
            : null;

    private List<GateDefinition> GetSelectedSectorGates() =>
        _sectorGates
            .Where(row => row.GateId is Guid)
            .Select(row => GateStore.Instance.Find(row.GateId!.Value))
            .Where(gate => gate is not null)
            .Cast<GateDefinition>()
            .ToList();

    private void AddSectorButton_Click(object sender, RoutedEventArgs e)
    {
        var row = new SectorGateRowVm();
        FillSectorChoices(row);
        _sectorGates.Add(row);
        RenumberSectors();
    }

    private void RemoveSector_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SectorGateRowVm row })
            _sectorGates.Remove(row);
        RenumberSectors();
    }

    private void MoveSectorUp_Click(object sender, RoutedEventArgs e) => MoveSector(sender, -1);
    private void MoveSectorDown_Click(object sender, RoutedEventArgs e) => MoveSector(sender, 1);

    private void MoveSector(object sender, int delta)
    {
        if (sender is not FrameworkElement { DataContext: SectorGateRowVm row })
            return;
        var from = _sectorGates.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= _sectorGates.Count)
            return;
        _sectorGates.Move(from, to);
        RenumberSectors();
    }

    private void RenumberSectors()
    {
        for (var i = 0; i < _sectorGates.Count; i++)
            _sectorGates[i].NumberText = $"Sector {i + 1}";
    }

    private void ApplyFastestHighlights()
    {
        if (_rows.Count == 0 || _lastDefinition?.Type != TestType.LapTime)
            return;
        var purple = new SolidColorBrush(Color.FromRgb(181, 108, 255));
        purple.Freeze();
        var normal = ThemeBrushes.TextPrimary();
        normal.Freeze();
        var fastestLap = _rows.Min(row => row.Annotated.Result.DurationSeconds);
        var sectorCount = _rows.Max(row => row.SectorTimes.Count);
        var fastestSectors = Enumerable.Range(0, sectorCount)
            .Select(index => _rows.Where(row => row.SectorTimes.Count > index)
                .Min(row => row.Annotated.Result.SectorTimes[index].DurationSeconds))
            .ToArray();

        foreach (var row in _rows)
        {
            row.DurationBrush = Math.Abs(row.Annotated.Result.DurationSeconds - fastestLap) < 0.0005
                ? purple : normal;
            row.DurationWeight = row.DurationBrush == purple ? FontWeights.Bold : FontWeights.Normal;
            for (var i = 0; i < row.SectorTimes.Count; i++)
            {
                row.SectorTimes[i].ValueBrush = Math.Abs(
                    row.Annotated.Result.SectorTimes[i].DurationSeconds - fastestSectors[i]) < 0.0005
                    ? purple : normal;
            }
        }
    }

    private void RebuildSectorColumns()
    {
        foreach (var column in _sectorColumns)
            ResultsGrid.Columns.Remove(column);
        _sectorColumns.Clear();
        if (_rows.Count == 0 || _rows.All(row => row.SectorTimes.Count == 0))
            return;

        var count = _rows.Max(row => row.SectorTimes.Count);
        for (var i = 0; i < count; i++)
        {
            var headerName = _rows.Select(row => row.SectorTimes.Count > i ? row.SectorTimes[i].Name : "")
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding($"SectorTimes[{i}].ValueText"));
            text.SetBinding(TextBlock.ForegroundProperty, new Binding($"SectorTimes[{i}].ValueBrush"));
            text.SetBinding(TextBlock.FontWeightProperty, new Binding($"SectorTimes[{i}].ValueWeight"));
            var column = new DataGridTemplateColumn
            {
                Header = $"S{i + 1} {headerName}",
                Width = 92,
                IsReadOnly = true,
                CellTemplate = new DataTemplate { VisualTree = text }
            };
            var durationIndex = ResultsGrid.Columns.IndexOf(ColDuration);
            ResultsGrid.Columns.Insert(durationIndex + 1 + i, column);
            _sectorColumns.Add(column);
        }
    }

    private void UpdateResultColumnMode(TestType type)
    {
        var lap = type == TestType.LapTime;
        if (ColStart is not null) ColStart.Visibility = lap ? Visibility.Collapsed : Visibility.Visible;
        if (ColEnd is not null) ColEnd.Visibility = lap ? Visibility.Collapsed : Visibility.Visible;
        if (ColDeltaV is not null) ColDeltaV.Visibility = lap ? Visibility.Collapsed : Visibility.Visible;
        if (ColPass is not null) ColPass.Visibility = lap ? Visibility.Collapsed : Visibility.Visible;
        if (ColPassValues is not null) ColPassValues.Visibility = lap ? Visibility.Collapsed : Visibility.Visible;
        if (ColDuration is not null)
            ColDuration.Header = Loc.T(lap ? "Results.Col.LapTime" : "Results.Col.Duration");
        if (lap)
            return;
        foreach (var column in _sectorColumns)
            ResultsGrid.Columns.Remove(column);
        _sectorColumns.Clear();
    }


    /// <summary>One Pass Condition row: Channel + Min + Max + At gate.</summary>
    private sealed class PassConditionRowVm : INotifyPropertyChanged
    {
        private string _channelId = ChannelIds.Velocity;
        private string _minText = "78";
        private string _maxText = "83";
        private Guid? _atGateId;

        public ObservableCollection<GatePickItem> GateChoices { get; } = new();

        public string ChannelId
        {
            get => _channelId;
            set { _channelId = value; OnPropertyChanged(); OnPropertyChanged(nameof(ChannelLabel)); }
        }

        public string ChannelLabel => FormatChannelLabel(_channelId);

        public ObservableCollection<ChannelPickItem> ChannelOptions { get; } = new();

        public string MinText
        {
            get => _minText;
            set { _minText = value; OnPropertyChanged(); }
        }

        public string MaxText
        {
            get => _maxText;
            set { _maxText = value; OnPropertyChanged(); }
        }

        public Guid? AtGateId
        {
            get => _atGateId;
            set { _atGateId = value; OnPropertyChanged(); }
        }

        public event Action<PassConditionRowVm>? RemoveRequested;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void RequestRemove() => RemoveRequested?.Invoke(this);

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class ChannelPickItem
    {
        public string Id { get; }
        public string Label { get; }
        public ChannelPickItem(string id, string label)
        {
            Id = id;
            Label = label;
        }
        public override string ToString() => Label;
    }

    private sealed class GatePickItem
    {
        public Guid? Id { get; }
        public string Name { get; }
        public GatePickItem(Guid? id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }

    private sealed class SectorGateRowVm : INotifyPropertyChanged
    {
        private Guid? _gateId;
        private string _numberText = "Sector";
        public ObservableCollection<GatePickItem> GateChoices { get; } = new();
        public Guid? GateId { get => _gateId; set { _gateId = value; OnPropertyChanged(); } }
        public string NumberText { get => _numberText; set { _numberText = value; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void UpdateGateStatusHint()
    {
        // Optional status nudge when gates change; keep Compute messaging primary.
        if (StatusText is null || GetSelectedTestType() != TestType.Gate)
            return;
        var s = GateStore.Instance.StartGate;
        var e = GateStore.Instance.EndGate;
        StatusText.Text =
            $"Gate: Start={(s is null ? "none" : s.Name)}  End={(e is null ? "none" : e.Name)}  " +
            $"({GateStore.Instance.Gates.Count} on map)";
        StatusText.Foreground = ThemeBrushes.TextSecondary();
    }

    private void SessionButton_Click(object sender, RoutedEventArgs e) =>
        SessionEditRequested?.Invoke();

    private void MathsChannelsButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (MathsChannelsDialog.Show(owner) == true)
        {
            PopulateChannelCombo();
            RefreshPassRowChannelOptions();
            StatusText.Text = MathsChannelStore.Instance.Definitions.Count == 0
                ? "Maths channels cleared."
                : $"Maths channels: {MathsChannelStore.Instance.Definitions.Count} defined.";
            StatusText.Foreground = ThemeBrushes.TextSecondary();
            MathsChanged?.Invoke();
        }
    }


    /// <summary>DataGrid row view-model.</summary>
    public sealed class ResultRowVm : INotifyPropertyChanged
    {
        private bool _isChecked;
        private Brush _durationBrush = ThemeBrushes.TextPrimary();
        private FontWeight _durationWeight = FontWeights.Normal;

        public required AnnotatedTestRun Annotated { get; init; }

        public int RunNumber => Annotated.RunNumber;

        public string SourceText { get; init; } = "";

        public string StartTimeText { get; init; } = "";

        public string EndTimeText { get; init; } = "";

        public string DurationText { get; init; } = "";

        public Brush DurationBrush
        {
            get => _durationBrush;
            set { _durationBrush = value; OnPropertyChanged(); }
        }

        public FontWeight DurationWeight
        {
            get => _durationWeight;
            set { _durationWeight = value; OnPropertyChanged(); }
        }

        public string DeltaVText { get; init; } = "";

        public string DistanceText { get; init; } = "";

        public string PassText { get; init; } = "";

        /// <summary>Highlighted Pass-condition measured values for the grid.</summary>
        public IReadOnlyList<PassValueChipVm> PassValues { get; init; } =
            Array.Empty<PassValueChipVm>();

        public IReadOnlyList<SectorTimeCellVm> SectorTimes { get; init; } =
            Array.Empty<SectorTimeCellVm>();

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                    return;
                _isChecked = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public static ResultRowVm From(AnnotatedTestRun a, TimeZoneInfo tz)
        {
            var r = a.Result;
            var chips = r.PassMeasurements
                .Select(PassValueChipVm.From)
                .ToArray();
            return new ResultRowVm
            {
                Annotated = a,
                SourceText = string.IsNullOrWhiteSpace(a.SourceLabel)
                    ? (string.IsNullOrWhiteSpace(r.SourceFile) ? "-" : r.SourceFile)
                    : a.SourceLabel,
                StartTimeText = FormatBeijing(r.StartTimestampMs, tz),
                EndTimeText = FormatBeijing(r.EndTimestampMs, tz),
                DurationText = r.DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture),
                DeltaVText = r.DeltaSpeedKph.ToString("0.##", CultureInfo.InvariantCulture),
                DistanceText = r.DistanceMeters.ToString("0.#", CultureInfo.InvariantCulture),
                PassText = r.Pass ? "Pass" : "Fail",
                PassValues = chips,
                SectorTimes = r.SectorTimes.Select(SectorTimeCellVm.From).ToArray()
            };
        }

        private static string FormatBeijing(long unixMs, TimeZoneInfo tz)
        {
            var utc = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
            var local = TimeZoneInfo.ConvertTime(utc, tz);
            return local.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
    }

    public sealed class SectorTimeCellVm : INotifyPropertyChanged
    {
        private Brush _valueBrush = ThemeBrushes.TextPrimary();
        public string Name { get; init; } = "";
        public string ValueText { get; init; } = "";
        public Brush ValueBrush
        {
            get => _valueBrush;
            set { _valueBrush = value; OnPropertyChanged(); OnPropertyChanged(nameof(ValueWeight)); }
        }
        public FontWeight ValueWeight => ValueBrush is SolidColorBrush brush
            && brush.Color == Color.FromRgb(181, 108, 255)
                ? FontWeights.Bold : FontWeights.Normal;
        public static SectorTimeCellVm From(SectorTimeResult result) => new()
        {
            Name = result.Name,
            ValueText = result.DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture)
        };
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>One Pass measurement chip (value + green/red highlight).</summary>
    public sealed class PassValueChipVm
    {
        public string Label { get; init; } = "";

        public string ValueText { get; init; } = "-";

        public string ToolTip { get; init; } = "";

        public System.Windows.Media.Brush ValueBrush { get; init; } =
            System.Windows.Media.Brushes.White;

        public System.Windows.Media.Brush ChipBackground { get; init; } =
            System.Windows.Media.Brushes.Transparent;

        public static PassValueChipVm From(PassConditionMeasurement m)
        {
            // In-range: green; out-of-range / missing: red
            var ok = m.InRange;
            var fg = ok
                ? ThemeBrushes.Success()
                : ThemeBrushes.Danger();
            var bg = ok
                ? ThemeBrushes.SuccessSoft()
                : ThemeBrushes.DangerSoft();
            fg.Freeze();
            bg.Freeze();

            return new PassValueChipVm
            {
                Label = m.Header,
                ValueText = m.ValueText,
                ToolTip = $"{m.Header} = {m.ValueText}  required {m.RangeText}" +
                          (ok ? " OK" : " FAIL"),
                ValueBrush = fg,
                ChipBackground = bg
            };
        }
    }
}

