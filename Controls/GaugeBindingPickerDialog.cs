using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// Pick a live channel or Test Results field / Pass measurement for a gauge.
/// </summary>
public sealed class GaugeBindingPickerDialog : Window
{
    private readonly ListBox _list;
    private readonly TextBox _titleBox;
    private readonly TextBox _unitBox;
    private bool _suppressTitleSync;

    public GaugeBinding? SelectedBinding { get; private set; }

    public string SelectedTitle { get; private set; } = "";

    public string SelectedUnit { get; private set; } = "";

    public GaugeBindingPickerDialog(
        GaugeBinding? current,
        string currentTitle,
        string currentUnit,
        IReadOnlyList<PassConditionMeasurement>? passOptions)
    {
        Title = "Select gauge data";
        Width = 420;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x17, 0x1E));
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        _titleBox = new TextBox
        {
            Text = currentTitle,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Gauge title (auto-fills when you pick a source; editable)"
        };
        _unitBox = new TextBox
        {
            Text = currentUnit,
            ToolTip = "Unit label (auto-fills when you pick a source; editable)"
        };
        Grid.SetColumn(_titleBox, 0);
        Grid.SetColumn(_unitBox, 1);
        titleRow.Children.Add(_titleBox);
        titleRow.Children.Add(_unitBox);
        Grid.SetRow(titleRow, 0);
        root.Children.Add(titleRow);

        var hint = new TextBlock
        {
            Text = "Live channels  路  Test Results  路  Pass conditions",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(hint, 1);
        root.Children.Add(hint);

        _list = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x13, 0x1A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x25, 0x30)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF0))
        };
        Grid.SetRow(_list, 2);
        root.Children.Add(_list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var ok = new Button
        {
            Content = "OK",
            Width = 88,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Style = TryFindResource("HeaderButtonStyle") as Style
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 88,
            IsCancel = true,
            Style = TryFindResource("HeaderButtonStyle") as Style
        };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;

        Populate(current, passOptions);
        _list.SelectionChanged += (_, _) => SyncTitleUnitFromSelection();
        _list.MouseDoubleClick += (_, _) => Accept();
    }

    /// <summary>
    /// When the user picks a source, fill Title/Unit from that item's defaults.
    /// Previously the boxes kept the initial "Speed"/"km/h", so OK saved the new
    /// BindingKey but left the visible title as Speed 鈥?gauges looked stuck on Speed.
    /// </summary>
    private void SyncTitleUnitFromSelection()
    {
        if (_suppressTitleSync)
            return;
        if (_list.SelectedItem is not ListBoxItem { Tag: PickTag tag })
            return;

        _titleBox.Text = tag.DefaultTitle;
        _unitBox.Text = tag.DefaultUnit ?? "";
    }

    private void Populate(
        GaugeBinding? current,
        IReadOnlyList<PassConditionMeasurement>? passOptions)
    {
        void AddHeader(string text)
        {
            _list.Items.Add(new ListBoxItem
            {
                IsEnabled = false,
                Content = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xA3, 0x4A)),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Margin = new Thickness(0, 6, 0, 2)
                }
            });
        }

        void AddItem(string label, string detail, GaugeBinding binding, string title, string unit)
        {
            var row = new StackPanel { Margin = new Thickness(2, 2, 2, 2) };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF0)),
                FontSize = 13
            });
            if (!string.IsNullOrWhiteSpace(detail))
            {
                row.Children.Add(new TextBlock
                {
                    Text = detail,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x74)),
                    FontSize = 11
                });
            }

            var item = new ListBoxItem
            {
                Content = row,
                Tag = new PickTag(binding, title, unit),
                Padding = new Thickness(6, 4, 6, 4)
            };
            _list.Items.Add(item);

            if (current is not null &&
                current.Kind == binding.Kind &&
                string.Equals(current.Key, binding.Key, StringComparison.OrdinalIgnoreCase))
            {
                _suppressTitleSync = true;
                try
                {
                    _list.SelectedItem = item;
                }
                finally
                {
                    _suppressTitleSync = false;
                }
            }
        }

        AddHeader("Live channels");
        foreach (var ch in ChannelRegistry.Instance.AvailablePlotChannels)
        {
            AddItem(
                ch.DisplayName,
                $"live 路 {ch.Id}" + (string.IsNullOrEmpty(ch.Unit) ? "" : $" 路 {ch.Unit}"),
                GaugeBinding.Live(ch.Id),
                ch.DisplayName,
                ch.Unit);
        }

        foreach (var maths in MathsChannelStore.Instance.Definitions)
        {
            var title = string.IsNullOrWhiteSpace(maths.DisplayName) ? maths.Id : maths.DisplayName;
            AddItem(
                title,
                $"live / maths / {maths.Expression}",
                GaugeBinding.Live(maths.Id),
                title,
                maths.Unit ?? "");
        }

        AddHeader("Test Results");
        AddItem("Duration", "test 路 s", GaugeBinding.TestField(TestResultFieldIds.Duration), "Duration", "s");
        AddItem("Delta V", "test 路 km/h", GaugeBinding.TestField(TestResultFieldIds.DeltaSpeed), "Delta V", "km/h");
        AddItem("Distance", "test 路 m", GaugeBinding.TestField(TestResultFieldIds.Distance), "Distance", "m");
        AddItem("Start Speed", "test 路 km/h", GaugeBinding.TestField(TestResultFieldIds.StartSpeed), "Start Speed", "km/h");
        AddItem("End Speed", "test 路 km/h", GaugeBinding.TestField(TestResultFieldIds.EndSpeed), "End Speed", "km/h");
        AddItem("Pass / Fail", "test", GaugeBinding.TestField(TestResultFieldIds.PassFail), "Pass", "");
        AddItem("Run #", "test", GaugeBinding.TestField(TestResultFieldIds.RunNumber), "Run", "");

        AddHeader("Pass conditions");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (passOptions is not null)
        {
            foreach (var m in passOptions)
            {
                if (string.IsNullOrWhiteSpace(m.Header) || !seen.Add(m.Header))
                    continue;
                AddItem(
                    m.Header,
                    $"pass 路 range {m.RangeText}",
                    GaugeBinding.Pass(m.Header),
                    m.Header,
                    "");
            }
        }

        if (seen.Count == 0)
        {
            _list.Items.Add(new ListBoxItem
            {
                IsEnabled = false,
                Content = new TextBlock
                {
                    Text = "(Compute a Gate test with Pass conditions to list them here)",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x74)),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }
    }

    private void Accept()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: PickTag tag })
            return;

        SelectedBinding = tag.Binding;
        // Prefer the selected source's defaults when the title box was left as a
        // stale prefill (e.g. Add Gauge still showing "Speed" after picking Lat).
        var typedTitle = _titleBox.Text?.Trim() ?? "";
        var typedUnit = _unitBox.Text?.Trim() ?? "";
        SelectedTitle = string.IsNullOrWhiteSpace(typedTitle) ? tag.DefaultTitle : typedTitle;
        SelectedUnit = string.IsNullOrWhiteSpace(typedUnit) ? tag.DefaultUnit : typedUnit;
        DialogResult = true;
        Close();
    }

    private sealed record PickTag(GaugeBinding Binding, string DefaultTitle, string DefaultUnit);
}
