using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// VBTS-like customizable Dashboard: free-form Default numeric gauges
/// bound to live channels or Test Results / Pass condition values.
/// </summary>
public partial class DashboardPanel : UserControl
{
    private readonly List<GaugeVisual> _visuals = new();
    private VehicleSample? _liveSample;
    private IReadOnlyList<AnnotatedTestRun> _testRuns = Array.Empty<AnnotatedTestRun>();
    private int? _selectedRunNumber;
    private bool _cursorFrozen;
    private bool _suspendPersist;

    private static readonly Brush CardBg =
        new SolidColorBrush(Color.FromRgb(0x0E, 0x13, 0x1A));
    private static readonly Brush CardBorder =
        new SolidColorBrush(Color.FromRgb(0x1E, 0x25, 0x30));
    private static readonly Brush CardBorderActive =
        new SolidColorBrush(Color.FromRgb(0xC8, 0xA3, 0x4A));
    private static readonly Brush TitleFg =
        new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6));
    private static readonly Brush ValueFg =
        new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF0));
    private static readonly Brush AccentFg =
        new SolidColorBrush(Color.FromRgb(0xC8, 0xA3, 0x4A));
    private static readonly Brush UnitFg =
        new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x74));
    private static readonly Brush PassFg =
        new SolidColorBrush(Color.FromRgb(0x3F, 0xBF, 0x6F));
    private static readonly Brush FailFg =
        new SolidColorBrush(Color.FromRgb(0xE0, 0x52, 0x52));

    public DashboardPanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DashboardLayoutStore.Instance.Load();
            RebuildFromStore();
        };
    }

    /// <summary>Raised when a multi-vehicle chip is clicked.</summary>
    public event Action<string>? VehicleSummaryClicked;

    /// <summary>
    /// Push the current live / cursor sample; updates all live-channel gauges.
    /// </summary>
    public void ApplyLiveSample(VehicleSample? sample, bool cursorFrozen = false)
    {
        _liveSample = sample;
        _cursorFrozen = cursorFrozen;
        CursorModeText.Text = cursorFrozen ? "Cursor" : "";
        RefreshAllValues();
    }

    /// <summary>
    /// Push latest Test Results (selected run drives test/pass gauges).
    /// </summary>
    public void ApplyTestResults(
        IReadOnlyList<AnnotatedTestRun>? runs,
        int? selectedRunNumber)
    {
        _testRuns = runs ?? Array.Empty<AnnotatedTestRun>();
        _selectedRunNumber = selectedRunNumber;
        RefreshAllValues();
    }

    /// <summary>
    /// Legacy fixed-field updater kept for call-site compatibility.
    /// Prefer <see cref="ApplyLiveSample"/>.
    /// </summary>
    public void SetValues(
        double speedKph,
        double longitudinalAcceleration,
        double lateralAcceleration,
        double yawRate,
        double steeringAngleDeg,
        bool cursorFrozen = false)
    {
        var sample = new VehicleSample
        {
            SpeedKph = speedKph,
            LongitudinalAcceleration = longitudinalAcceleration,
            LateralAcceleration = lateralAcceleration,
            YawRate = yawRate,
            Heading = steeringAngleDeg,
            Channels = VehicleSample.BuildCoreChannels(
                speedKph,
                longitudinalAcceleration,
                lateralAcceleration,
                0,
                yawRate,
                steeringAngleDeg,
                0, 0, 0)
        };
        ApplyLiveSample(sample, cursorFrozen);
    }

    public void SetMultiVehicleSummary(
        IReadOnlyList<(string Name, string ColorHex, double SpeedKph)> vehicles,
        string? selectedName = null)
    {
        MultiVehicleHost.Items.Clear();

        if (vehicles.Count <= 1)
        {
            MultiVehicleHost.Visibility = Visibility.Collapsed;
            return;
        }

        MultiVehicleHost.Visibility = Visibility.Visible;

        foreach (var (name, colorHex, speed) in vehicles)
        {
            var isSelected = selectedName is not null &&
                string.Equals(name, selectedName, StringComparison.OrdinalIgnoreCase);

            var border = new Border
            {
                Background = new SolidColorBrush(
                    isSelected
                        ? Color.FromRgb(0x24, 0x30, 0x40)
                        : Color.FromRgb(0x0E, 0x13, 0x1A)),
                BorderBrush = TryBrush(colorHex) ?? CardBorder,
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 4),
                Cursor = Cursors.Hand,
                Tag = name,
                ToolTip = isSelected ? $"Current: {name}" : $"Switch to {name}"
            };

            border.MouseLeftButtonUp += (_, e) =>
            {
                if (border.Tag is string n)
                    VehicleSummaryClicked?.Invoke(n);
                e.Handled = true;
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = TryBrush(colorHex) ?? Brushes.Gray,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC3, 0xCB, 0xD8)),
                FontSize = 11,
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                MaxWidth = 120,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{speed:0.0} km/h",
                Foreground = AccentFg,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            });

            border.Child = stack;
            MultiVehicleHost.Items.Add(border);
        }
    }

    public void Clear()
    {
        _liveSample = null;
        _cursorFrozen = false;
        CursorModeText.Text = "";
        MultiVehicleHost.Items.Clear();
        MultiVehicleHost.Visibility = Visibility.Collapsed;
        RefreshAllValues();
    }

    private void AddGaugeButton_Click(object sender, RoutedEventArgs e)
    {
        var layout = new DashboardGaugeLayout
        {
            Title = "Speed",
            Unit = "km/h",
            BindingKey = $"live:{ChannelIds.Velocity}",
            X = 16 + (_visuals.Count % 4) * 24,
            Y = 16 + (_visuals.Count % 4) * 24,
            Width = 140,
            Height = 96
        };

        if (!TryPickBinding(layout))
            return;

        DashboardLayoutStore.Instance.Add(layout);
        RebuildFromStore();
    }

    private void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        DashboardLayoutStore.Instance.ResetToDefaults();
        RebuildFromStore();
    }

    private void GaugeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Absolute layout: Dashboard panel resize must NOT move/resize gauges.
        // Overflow on the right/bottom stays put and is clipped (ClipToBounds).
    }

    private void RebuildFromStore()
    {
        _suspendPersist = true;
        try
        {
            GaugeCanvas.Children.Clear();
            _visuals.Clear();
            foreach (var layout in DashboardLayoutStore.Instance.Gauges)
                _visuals.Add(CreateVisual(layout));
            RefreshAllValues();
        }
        finally
        {
            _suspendPersist = false;
        }
    }

    private GaugeVisual CreateVisual(DashboardGaugeLayout layout)
    {
        var titleText = new TextBlock
        {
            Text = layout.Title,
            Foreground = TitleFg,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Cursor = Cursors.Hand,
            ToolTip = "Click to change data source"
        };

        var deleteBtn = new Button
        {
            Content = "x",
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            Width = 22,
            Height = 22,
            FontSize = 14,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TitleFg,
            Cursor = Cursors.Hand,
            ToolTip = "Remove gauge",
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(deleteBtn, Dock.Right);
        header.Children.Add(deleteBtn);
        header.Children.Add(titleText);

        var valueText = new TextBlock
        {
            Text = "--",
            Foreground = AccentFg,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var unitText = new TextBlock
        {
            Text = layout.Unit,
            Foreground = UnitFg,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var body = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(valueText, 0);
        Grid.SetRow(unitText, 1);
        body.Children.Add(valueText);
        body.Children.Add(unitText);

        var resizeGrip = new Border
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeNWSE,
            ToolTip = "Drag to resize"
        };
        resizeGrip.Child = new Path
        {
            Data = Geometry.Parse("M0,10 L10,0 M4,10 L10,4 M8,10 L10,8"),
            Stroke = UnitFg,
            StrokeThickness = 1.2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 1, 1),
            IsHitTestVisible = false
        };

        var content = new Grid { Margin = new Thickness(10) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(body, 1);
        content.Children.Add(header);
        content.Children.Add(body);

        var root = new Border
        {
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Width = Math.Max(96, layout.Width),
            Height = Math.Max(72, layout.Height),
            Cursor = Cursors.SizeAll,
            Child = new Grid()
        };

        var overlay = (Grid)root.Child;
        overlay.Children.Add(content);
        overlay.Children.Add(resizeGrip);

        Canvas.SetLeft(root, layout.X);
        Canvas.SetTop(root, layout.Y);
        GaugeCanvas.Children.Add(root);

        var visual = new GaugeVisual(layout, root, titleText, valueText, unitText);
        root.Tag = visual;

        void OnPickSource(object? s, MouseButtonEventArgs e)
        {
            if (visual.SuppressClick)
            {
                visual.SuppressClick = false;
                return;
            }
            OpenPickerForVisual(visual);
            e.Handled = true;
        }

        titleText.MouseLeftButtonUp += OnPickSource;
        valueText.MouseLeftButtonUp += OnPickSource;
        valueText.Cursor = Cursors.Hand;
        valueText.ToolTip = "Click to change data source";

        deleteBtn.Click += (_, _) =>
        {
            DashboardLayoutStore.Instance.Remove(visual.Layout.Id);
            RebuildFromStore();
        };

        AttachDrag(visual);
        AttachResize(visual, resizeGrip);

        return visual;
    }

    private void OpenPickerForVisual(GaugeVisual visual)
    {
        if (!TryPickBinding(visual.Layout))
            return;

        // Persist binding/title/unit (not only geometry) and refresh display.
        DashboardLayoutStore.Instance.Update(visual.Layout);
        var updated = DashboardLayoutStore.Instance.Gauges.FirstOrDefault(g => g.Id == visual.Layout.Id);
        if (updated is not null)
        {
            visual.Layout.Title = updated.Title;
            visual.Layout.Unit = updated.Unit;
            visual.Layout.BindingKey = updated.BindingKey;
        }
        RefreshVisualValue(visual);
    }

    private bool TryPickBinding(DashboardGaugeLayout layout)
    {
        var passOpts = CollectPassOptions();
        var dlg = new GaugeBindingPickerDialog(
            layout.Binding,
            layout.Title,
            layout.Unit,
            passOpts)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() != true || dlg.SelectedBinding is null)
            return false;

        layout.Binding = dlg.SelectedBinding;
        layout.Title = dlg.SelectedTitle;
        layout.Unit = dlg.SelectedUnit;
        return true;
    }

    private IReadOnlyList<PassConditionMeasurement> CollectPassOptions()
    {
        var list = new List<PassConditionMeasurement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var run in _testRuns)
        {
            foreach (var m in run.Result.PassMeasurements)
            {
                if (seen.Add(m.Header))
                    list.Add(m);
            }
        }
        return list;
    }

    private void AttachDrag(GaugeVisual visual)
    {
        var root = visual.Root;
        Point? startMouse = null;
        Point startPos = default;
        var dragging = false;
        var armed = false;

        root.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Button)
                return;
            // Don't start drag from resize grip (handled separately).
            if (FindAncestor<Border>((DependencyObject)e.OriginalSource) is Border b &&
                b.Cursor == Cursors.SizeNWSE)
                return;

            startMouse = e.GetPosition(GaugeCanvas);
            startPos = new Point(Canvas.GetLeft(root), Canvas.GetTop(root));
            dragging = false;
            armed = true;
            visual.SuppressClick = false;
            root.BorderBrush = CardBorderActive;
        };

        root.PreviewMouseMove += (_, e) =>
        {
            if (!armed || startMouse is null)
                return;
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            var now = e.GetPosition(GaugeCanvas);
            var dx = now.X - startMouse.Value.X;
            var dy = now.Y - startMouse.Value.Y;
            if (!dragging && Math.Abs(dx) + Math.Abs(dy) > 4)
            {
                dragging = true;
                visual.SuppressClick = true;
                root.CaptureMouse();
            }

            if (!dragging)
                return;

            Canvas.SetLeft(root, startPos.X + dx);
            Canvas.SetTop(root, startPos.Y + dy);
            ClampVisual(visual);
            e.Handled = true;
        };

        root.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!armed)
                return;

            if (root.IsMouseCaptured)
                root.ReleaseMouseCapture();
            root.BorderBrush = CardBorder;

            if (dragging)
            {
                PersistVisual(visual);
                e.Handled = true;
            }

            armed = false;
            dragging = false;
            startMouse = null;
        };
    }

    private void AttachResize(GaugeVisual visual, Border grip)
    {
        var root = visual.Root;
        Point? startMouse = null;
        Size startSize = default;

        grip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            startMouse = e.GetPosition(GaugeCanvas);
            startSize = new Size(root.Width, root.Height);
            grip.CaptureMouse();
            root.BorderBrush = CardBorderActive;
            e.Handled = true;
        };

        grip.PreviewMouseMove += (_, e) =>
        {
            if (startMouse is null || !grip.IsMouseCaptured)
                return;
            var now = e.GetPosition(GaugeCanvas);
            var w = Math.Max(96, startSize.Width + (now.X - startMouse.Value.X));
            var h = Math.Max(72, startSize.Height + (now.Y - startMouse.Value.Y));
            root.Width = w;
            root.Height = h;
            AdjustValueFont(visual);
            ClampVisual(visual);
            visual.SuppressClick = true;
            e.Handled = true;
        };

        grip.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!grip.IsMouseCaptured)
                return;
            grip.ReleaseMouseCapture();
            root.BorderBrush = CardBorder;
            startMouse = null;
            PersistVisual(visual);
            e.Handled = true;
        };
    }

    private void ClampVisual(GaugeVisual visual)
    {
        // Used while dragging/resizing a gauge only.
        // Keep a sliver on-canvas from the left/top so the gauge is not lost;
        // allow overflow on the right/bottom (clipped by the panel). Never change W/H here.
        var root = visual.Root;
        var left = Canvas.GetLeft(root);
        var top = Canvas.GetTop(root);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        const double minVisible = 32.0;
        var maxLeft = Math.Max(0, GaugeCanvas.ActualWidth - minVisible);
        var maxTop = Math.Max(0, GaugeCanvas.ActualHeight - minVisible);
        if (left < 0) left = 0;
        if (top < 0) top = 0;
        if (left > maxLeft) left = maxLeft;
        if (top > maxTop) top = maxTop;
        Canvas.SetLeft(root, left);
        Canvas.SetTop(root, top);
    }

    private void PersistVisual(GaugeVisual visual)
    {
        if (_suspendPersist)
            return;

        visual.Layout.X = Canvas.GetLeft(visual.Root);
        visual.Layout.Y = Canvas.GetTop(visual.Root);
        visual.Layout.Width = visual.Root.Width;
        visual.Layout.Height = visual.Root.Height;
        DashboardLayoutStore.Instance.Update(visual.Layout);
    }

    private void RefreshAllValues()
    {
        foreach (var v in _visuals)
            RefreshVisualValue(v);
    }

    private void RefreshVisualValue(GaugeVisual visual)
    {
        AdjustValueFont(visual);
        var binding = visual.Layout.Binding;
        var (text, brush) = ResolveValue(binding);
        visual.ValueText.Text = text;
        visual.ValueText.Foreground = brush;
        visual.TitleText.Text = visual.Layout.Title;
        visual.UnitText.Text = visual.Layout.Unit;
    }

    private void AdjustValueFont(GaugeVisual visual)
    {
        // Fit numeric value into the gauge body: grow/shrink with outer box, stay centered.
        var availH = Math.Max(18, visual.Root.Height - 52);
        var availW = Math.Max(32, visual.Root.Width - 28);
        var text = visual.ValueText.Text;
        if (string.IsNullOrEmpty(text))
            text = "--";
        var charCount = Math.Max(2, text.Length);
        var byHeight = availH * 0.78;
        var byWidth = availW / (charCount * 0.62);
        var size = Math.Max(12, Math.Min(byHeight, byWidth)); // no hard max: grows with box
        visual.ValueText.FontSize = size;
        visual.ValueText.HorizontalAlignment = HorizontalAlignment.Center;
        visual.ValueText.TextAlignment = TextAlignment.Center;
        visual.ValueText.VerticalAlignment = VerticalAlignment.Center;
        visual.ValueText.Foreground =
            visual.Layout.Binding.Kind == GaugeBindingKind.LiveChannel &&
            string.Equals(visual.Layout.Binding.Key, ChannelIds.Velocity, StringComparison.OrdinalIgnoreCase)
                ? AccentFg
                : ValueFg;
    }

    private (string Text, Brush Brush) ResolveValue(GaugeBinding binding)
    {
        switch (binding.Kind)
        {
            case GaugeBindingKind.LiveChannel:
            {
                if (_liveSample is null)
                    return ("--", ValueFg);
                // Align with plot GetSignalValue: full Channels + maths.
                var enriched = MathsEnricher.ApplyOne(
                    _liveSample,
                    MathsChannelStore.Instance.Definitions);
                var v = enriched.GetChannel(binding.Key);
                return (FormatNumber(v), string.Equals(binding.Key, ChannelIds.Velocity, StringComparison.OrdinalIgnoreCase) ? AccentFg : ValueFg);
            }
            case GaugeBindingKind.TestResultField:
            {
                var run = GetFocusRun();
                if (run is null)
                    return ("--", ValueFg);
                return FormatTestField(run.Result, binding.Key);
            }
            case GaugeBindingKind.PassMeasurement:
            {
                var run = GetFocusRun();
                if (run is null)
                    return ("--", ValueFg);
                var m = run.Result.PassMeasurements.FirstOrDefault(p =>
                    string.Equals(p.Header, binding.Key, StringComparison.OrdinalIgnoreCase));
                if (m is null)
                    return ("--", ValueFg);
                var brush = m.Value is null ? ValueFg : m.InRange ? PassFg : FailFg;
                return (m.ValueText, brush);
            }
            default:
                return ("--", ValueFg);
        }
    }

    private AnnotatedTestRun? GetFocusRun()
    {
        if (_testRuns.Count == 0)
            return null;
        if (_selectedRunNumber is int n)
        {
            var match = _testRuns.FirstOrDefault(r => r.RunNumber == n);
            if (match is not null)
                return match;
        }
        return _testRuns[0];
    }

    private (string Text, Brush Brush) FormatTestField(TestRunResult r, string fieldId)
    {
        return fieldId switch
        {
            TestResultFieldIds.Duration =>
                (FormatNumber(r.DurationSeconds), ValueFg),
            TestResultFieldIds.DeltaSpeed =>
                (FormatNumber(r.DeltaSpeedKph), ValueFg),
            TestResultFieldIds.Distance =>
                (FormatNumber(r.DistanceMeters), ValueFg),
            TestResultFieldIds.StartSpeed =>
                (FormatNumber(r.StartSpeedKph), ValueFg),
            TestResultFieldIds.EndSpeed =>
                (FormatNumber(r.EndSpeedKph), ValueFg),
            TestResultFieldIds.RunNumber =>
                (r.RunNumber.ToString(CultureInfo.InvariantCulture), ValueFg),
            TestResultFieldIds.PassFail =>
                (r.Pass ? "PASS" : "FAIL", r.Pass ? PassFg : FailFg),
            _ => ("--", ValueFg)
        };
    }

    private static string FormatNumber(double v) =>
        v.ToString("0.##", CultureInfo.InvariantCulture);

    private static Brush? TryBrush(string hex)
    {
        try
        {
            return (Brush)new BrushConverter().ConvertFromString(hex)!;
        }
        catch
        {
            return null;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        while (start is not null)
        {
            if (start is T match)
                return match;
            start = VisualTreeHelper.GetParent(start);
        }
        return null;
    }

    private sealed class GaugeVisual
    {
        public DashboardGaugeLayout Layout { get; }
        public Border Root { get; }
        public TextBlock TitleText { get; }
        public TextBlock ValueText { get; }
        public TextBlock UnitText { get; }
        public bool SuppressClick { get; set; }

        public GaugeVisual(
            DashboardGaugeLayout layout,
            Border root,
            TextBlock titleText,
            TextBlock valueText,
            TextBlock unitText)
        {
            Layout = layout;
            Root = root;
            TitleText = titleText;
            ValueText = valueText;
            UnitText = unitText;
        }
    }
}
