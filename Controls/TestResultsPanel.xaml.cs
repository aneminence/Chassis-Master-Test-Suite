using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        "#5AC8C8", "#E05252", "#D8D84A", "#C8A34A"
    };
    private readonly ObservableCollection<ResultRowVm> _rows = new();

    private IReadOnlyList<TestRunResult> _lastResults =
        Array.Empty<TestRunResult>();

    private IReadOnlyList<AnnotatedTestRun> _lastAnnotated =
        Array.Empty<AnnotatedTestRun>();

    private TestDefinition? _lastDefinition;

    private readonly ObservableCollection<PassConditionRowVm> _passConditions = new();

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
        };
        ApplyTypeDefaults();
        EnsureDefaultPassCondition();
        if (PassConditionsHost is not null)
            PassConditionsHost.ItemsSource = _passConditions;
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
        if (type == TestType.Gate)
            RefreshAnalysisGateCombos();

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
        }
    }

    private void ComputeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildDefinition(out var definition, out var error))
        {
            StatusText.Text = error;
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC8, 0xA3, 0x4A));
            return;
        }

        var sources = ResolveSources();
        if (sources.Count == 0)
        {
            StatusText.Text = "No sample sources wired (open a VBO or wait for live history).";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC8, 0xA3, 0x4A));
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
            if (definition.Type == TestType.Gate)
            {
                var startGate = GateStore.Instance.StartGate;
                var endGate = GateStore.Instance.EndGate;
                if (startGate is null || endGate is null || !startGate.IsValid || !endGate.IsValid)
                {
                    StatusText.Text = "Set Start When + End When gates (place on Track Map first).";
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xC8, 0xA3, 0x4A));
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
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x8A, 0x94, 0xA6));
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
                        ColorHex = "#C8A34A",
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
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x8A, 0x94, 0xA6));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC8, 0xA3, 0x4A));
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
                ToggleConditionsButton.Content = "Show conditions";
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
                ToggleConditionsButton.Content = "Hide conditions";
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
                _ => "Accel"
            },
            StartThreshold = StartThresholdBox?.Text ?? "",
            EndThreshold = EndThresholdBox?.Text ?? "",
            ConditionsCollapsed = _conditionsCollapsed
        };

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
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x8A, 0x94, 0xA6));
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
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x8A, 0x94, 0xA6));
            MathsChanged?.Invoke();
        }
    }


    /// <summary>DataGrid row view-model.</summary>
    public sealed class ResultRowVm : INotifyPropertyChanged
    {
        private bool _isChecked;

        public required AnnotatedTestRun Annotated { get; init; }

        public int RunNumber => Annotated.RunNumber;

        public string SourceText { get; init; } = "";

        public string StartTimeText { get; init; } = "";

        public string EndTimeText { get; init; } = "";

        public string DurationText { get; init; } = "";

        public string DeltaVText { get; init; } = "";

        public string DistanceText { get; init; } = "";

        public string PassText { get; init; } = "";

        /// <summary>Highlighted Pass-condition measured values for the grid.</summary>
        public IReadOnlyList<PassValueChipVm> PassValues { get; init; } =
            Array.Empty<PassValueChipVm>();

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
                PassValues = chips
            };
        }

        private static string FormatBeijing(long unixMs, TimeZoneInfo tz)
        {
            var utc = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
            var local = TimeZoneInfo.ConvertTime(utc, tz);
            return local.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
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
                ? new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x3D, 0xDC, 0x97))
                : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE0, 0x5A, 0x5A));
            var bg = ok
                ? new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0x33, 0x3D, 0xDC, 0x97))
                : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0x33, 0xE0, 0x5A, 0x5A));
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

