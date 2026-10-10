using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// 实时数据仪表板；支持多车摘要条。
/// </summary>
public partial class DashboardPanel : UserControl
{
    public DashboardPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 更新主卡片数值（实时或光标冻结点）。
    /// </summary>
    public void SetValues(
        double speedKph,
        double longitudinalAcceleration,
        double lateralAcceleration,
        double yawRate,
        double steeringAngleDeg,
        bool cursorFrozen = false)
    {
        SpeedValueText.Text =
            speedKph.ToString("F2", CultureInfo.InvariantCulture);

        LongitudinalAccelerationValueText.Text =
            longitudinalAcceleration.ToString("F2", CultureInfo.InvariantCulture);

        LateralAccelerationValueText.Text =
            lateralAcceleration.ToString("F2", CultureInfo.InvariantCulture);

        YawRateValueText.Text =
            yawRate.ToString("F2", CultureInfo.InvariantCulture);

        SteeringAngleValueText.Text =
            steeringAngleDeg.ToString("F1", CultureInfo.InvariantCulture);

        CursorModeText.Text = cursorFrozen ? "· Cursor" : "";
    }

    /// <summary>点击多车摘要条目时抛出文件名（与线下芯片同名）。</summary>
    public event Action<string>? VehicleSummaryClicked;

    /// <summary>
    /// 多车同步摘要：名称 / 颜色 / 速度；selectedName 高亮为当前 Dashboard 主文件。
    /// </summary>
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
                BorderBrush = TryBrush(colorHex) ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x25, 0x30)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = name,
                ToolTip = isSelected ? $"当前：{name}" : $"点击切换到 {name}"
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
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xA3, 0x4A)),
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
        SpeedValueText.Text = "--";
        LongitudinalAccelerationValueText.Text = "--";
        LateralAccelerationValueText.Text = "--";
        YawRateValueText.Text = "--";
        SteeringAngleValueText.Text = "--";
        CursorModeText.Text = "";
        MultiVehicleHost.Items.Clear();
        MultiVehicleHost.Visibility = Visibility.Collapsed;
    }

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
}
