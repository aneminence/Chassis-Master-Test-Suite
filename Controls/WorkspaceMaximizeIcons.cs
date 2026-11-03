using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// 45° double-arrow icons for workspace maximize / restore.
/// Expand = arrows pointing outward (NE + SW); restore = arrows pointing inward.
/// </summary>
public static class WorkspaceMaximizeIcons
{
    private const double Box = 16.0;

    public static FrameworkElement CreateExpandIcon()
        => CreateIcon(
            // NE outward: tip at top-right
            "M 8.8,7.2 L 13.6,2.4 M 10.6,2.4 L 13.6,2.4 L 13.6,5.4",
            // SW outward: tip at bottom-left
            "M 7.2,8.8 L 2.4,13.6 M 2.4,10.6 L 2.4,13.6 L 5.4,13.6");

    public static FrameworkElement CreateRestoreIcon()
        => CreateIcon(
            // From NE corner inward toward center
            "M 13.6,2.4 L 8.8,7.2 M 8.8,4.9 L 8.8,7.2 L 11.1,7.2",
            // From SW corner inward toward center
            "M 2.4,13.6 L 7.2,8.8 M 4.9,8.8 L 7.2,8.8 L 7.2,11.1");

    private static FrameworkElement CreateIcon(string pathA, string pathB)
    {
        var canvas = new Canvas
        {
            Width = Box,
            Height = Box,
            SnapsToDevicePixels = true
        };

        canvas.Children.Add(CreateArrowPath(pathA));
        canvas.Children.Add(CreateArrowPath(pathB));

        return new Viewbox
        {
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            Child = canvas,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };
    }

    private static Path CreateArrowPath(string data)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };

        // Follow the host Button.Foreground so hover/theme styles recolor the icon.
        path.SetBinding(Shape.StrokeProperty, new Binding(Button.ForegroundProperty.Name)
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
        });

        return path;
    }
}