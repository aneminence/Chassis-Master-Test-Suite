using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chassis_Master_Test_Suite.Session;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// 简易 SessionData 编辑对话框（深色主题）。
/// </summary>
public static class SessionEditDialog
{
    public static bool? Show(Window owner, SessionMetadata metadata)
    {
        var win = new Window
        {
            Title = "Session Data",
            Owner = owner,
            Width = 420,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x17, 0x1E)),
            ResizeMode = ResizeMode.NoResize
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var stack = new StackPanel();
        TextBox AddField(string label, string value)
        {
            stack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6)),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 2)
            });
            var box = new TextBox
            {
                Text = value,
                Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x13, 0x1A)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF0)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x25, 0x30)),
                CaretBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xA3, 0x4A)),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0)
            };
            stack.Children.Add(box);
            return box;
        }

        var driver = AddField("DriverName", metadata.DriverName);
        var vNum = AddField("VehicleNumber", metadata.VehicleNumber);
        var vModel = AddField("VehicleModel", metadata.VehicleModel);
        var track = AddField("TestTrack", metadata.TestTrack);
        var facility = AddField("TestFacility", metadata.TestFacility);
        var comments = AddField("Comments", metadata.Comments);
        var weather = AddField("Weather", metadata.Weather);
        var temp = AddField("Temperature", metadata.Temperature);
        var wind = AddField("WindSpeed", metadata.WindSpeed);

        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 0);
        root.Children.Add(scroll);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "OK", Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        ok.Click += (_, _) =>
        {
            metadata.DriverName = driver.Text.Trim();
            metadata.VehicleNumber = vNum.Text.Trim();
            metadata.VehicleModel = vModel.Text.Trim();
            metadata.TestTrack = track.Text.Trim();
            metadata.TestFacility = facility.Text.Trim();
            metadata.Comments = comments.Text.Trim();
            metadata.Weather = weather.Text.Trim();
            metadata.Temperature = temp.Text.Trim();
            metadata.WindSpeed = wind.Text.Trim();
            win.DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        win.Content = root;
        return win.ShowDialog();
    }
}
