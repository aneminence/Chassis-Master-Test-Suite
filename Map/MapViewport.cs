using System.Windows;
using System.Windows.Controls;
using Chassis_Master_Test_Suite.Controls;

namespace Chassis_Master_Test_Suite.Map;

/// <summary>
/// An immutable mapping from local Mercator coordinates to the tile canvas.
/// All measurements belong to one completed ScottPlot render.
/// </summary>
internal sealed record MapViewport(
    TrackProjection Projection,
    ScottPlot.AxisLimits Limits,
    Rect DataArea,
    double RenderDataWidth)
{
    public static MapViewport? Capture(
        ScottPlot.WPF.WpfPlot plot,
        Canvas canvas,
        TrackProjection projection,
        ScottPlot.RenderDetails render)
    {
        // Use the actual drawing surface, including its offset inside the control.
        // WPF measures it in DIPs, while FigureRect measures the rendered bitmap.
        if (plot.Template?.FindName("PART_SKElement", plot) is not FrameworkElement surface)
            return null;

        var figure = render.FigureRect;
        var data = render.DataRect;
        var limits = render.AxisLimits;
        var scale = plot.Plot.ScaleFactor;
        if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0 ||
            figure.Width <= 0 || figure.Height <= 0 || data.Width <= 0 || data.Height <= 0 ||
            !double.IsFinite(scale) || scale <= 0 ||
            !double.IsFinite(limits.Left) || !double.IsFinite(limits.Right) ||
            !double.IsFinite(limits.Bottom) || !double.IsFinite(limits.Top) ||
            limits.Right <= limits.Left || limits.Top <= limits.Bottom)
            return null;

        // DataRect is BEFORE Plot.ScaleFactor, FigureRect is AFTER it.
        // Neither is necessarily in WPF DIPs. Convert using the actual rendered
        // size, so DPI, pixel rounding and an independent Plot.ScaleFactor all work.
        var dipPerPixelX = surface.ActualWidth / figure.Width;
        var dipPerPixelY = surface.ActualHeight / figure.Height;
        var topLeft = surface.TranslatePoint(new Point(
            (data.Left * scale - figure.Left) * dipPerPixelX,
            (data.Top * scale - figure.Top) * dipPerPixelY), canvas);
        var bottomRight = surface.TranslatePoint(new Point(
            (data.Right * scale - figure.Left) * dipPerPixelX,
            (data.Bottom * scale - figure.Top) * dipPerPixelY), canvas);

        return new MapViewport(projection, limits, new Rect(topLeft, bottomRight), data.Width * scale);
    }

    public Point ToCanvas(double x, double y) => new(
        DataArea.Left + (x - Limits.Left) / (Limits.Right - Limits.Left) * DataArea.Width,
        DataArea.Top + (Limits.Top - y) / (Limits.Top - Limits.Bottom) * DataArea.Height);

    public Rect TileRectangle(int zoom, int x, int y)
    {
        var (west, north, east, south) = MapTileMath.TileMercatorBounds(x, y, zoom);
        return new Rect(
            ToCanvas(west - Projection.OriginMercatorX, north - Projection.OriginMercatorY),
            ToCanvas(east - Projection.OriginMercatorX, south - Projection.OriginMercatorY));
    }
}
