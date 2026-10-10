using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// 轨迹折线段与虚拟门线段的相交检测（局部 ENU 米制，门中心为原点）。
/// </summary>
public static class GateCrossing
{
    private const double MetersPerDegreeLatitude = 111320.0;

    public static bool TryCross(
        GateDefinition gate,
        double lat0,
        double lon0,
        double lat1,
        double lon1,
        out double t)
    {
        t = 0;
        if (!gate.IsValid)
            return false;

        if (!IsValidGps(lat0, lon0) || !IsValidGps(lat1, lon1) ||
            !IsValidGps(gate.Latitude, gate.Longitude))
            return false;

        var mPerLon = MetersPerDegreeLatitude *
                      Math.Cos(gate.Latitude * Math.PI / 180.0);

        double ToX(double lon) => (lon - gate.Longitude) * mPerLon;
        double ToY(double lat) => (lat - gate.Latitude) * MetersPerDegreeLatitude;

        var x0 = ToX(lon0);
        var y0 = ToY(lat0);
        var x1 = ToX(lon1);
        var y1 = ToY(lat1);

        var (gx0, gy0, gx1, gy1) = GetGateEndpointsLocal(gate, x0, y0, x1, y1);
        return TrySegmentIntersect(x0, y0, x1, y1, gx0, gy0, gx1, gy1, out t);
    }

    public static bool TryCrossSamples(
        GateDefinition gate,
        VehicleSample a,
        VehicleSample b,
        out double t) =>
        TryCross(gate, a.Latitude, a.Longitude, b.Latitude, b.Longitude, out t);

    /// <summary>
    /// 门两端（以门中心为原点的局部米制）。
    /// </summary>
    public static (double X0, double Y0, double X1, double Y1) GetGateEndpointsLocal(
        GateDefinition gate,
        double segX0,
        double segY0,
        double segX1,
        double segY1)
    {
        double headingRad;
        if (gate.HeadingDeg is double h)
        {
            headingRad = h * Math.PI / 180.0;
        }
        else
        {
            var dx = segX1 - segX0;
            var dy = segY1 - segY0;
            headingRad = Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9
                ? 0
                : Math.Atan2(dx, dy);
        }

        // 航向 (东=sin, 北=cos)；门线垂直：(cos, -sin)
        var px = Math.Cos(headingRad);
        var py = -Math.Sin(headingRad);
        var half = gate.WidthMeters * 0.5;
        return (-px * half, -py * half, px * half, py * half);
    }

    /// <summary>返回门两端的 lat/lon（用于地图绘制）。</summary>
    public static (double Lat0, double Lon0, double Lat1, double Lon1) GetGateEndpointsLatLon(
        GateDefinition gate)
    {
        var heading = gate.HeadingDeg ?? 0;
        var g = new GateDefinition
        {
            Name = gate.Name,
            Latitude = gate.Latitude,
            Longitude = gate.Longitude,
            WidthMeters = gate.WidthMeters,
            HeadingDeg = heading
        };
        var (x0, y0, x1, y1) = GetGateEndpointsLocal(g, 0, 0, 0, 1);
        var mPerLon = MetersPerDegreeLatitude *
                      Math.Cos(gate.Latitude * Math.PI / 180.0);
        double Lat(double y) => gate.Latitude + y / MetersPerDegreeLatitude;
        double Lon(double x) => gate.Longitude + x / mPerLon;
        return (Lat(y0), Lon(x0), Lat(y1), Lon(x1));
    }

    public static bool TrySegmentIntersect(
        double ax, double ay, double bx, double by,
        double cx, double cy, double dx, double dy,
        out double t)
    {
        t = 0;
        var rX = bx - ax;
        var rY = by - ay;
        var sX = dx - cx;
        var sY = dy - cy;
        var denom = rX * sY - rY * sX;
        if (Math.Abs(denom) < 1e-12)
            return false;

        var qpX = cx - ax;
        var qpY = cy - ay;
        var tt = (qpX * sY - qpY * sX) / denom;
        var uu = (qpX * rY - qpY * rX) / denom;
        if (tt < 0 || tt > 1 || uu < 0 || uu > 1)
            return false;

        t = tt;
        return true;
    }

    private static bool IsValidGps(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon) ||
            double.IsInfinity(lat) || double.IsInfinity(lon))
            return false;
        if (Math.Abs(lat) < 1e-5 && Math.Abs(lon) < 1e-5)
            return false;
        return lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
    }
}
