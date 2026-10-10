using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// Track Map coordinate transforms.
///
/// Plot space is local Web Mercator (EPSG:3857) meters relative to the track
/// origin — the same projection XYZ satellite tiles use. Trajectory points and
/// basemap tile corners therefore share one CRS, so alignment holds at every zoom.
/// Local ENU (equirectangular) was previously used for the track while tiles were
/// still Mercator imagery stretched into ENU boxes; that mismatch grew when zoomed out.
/// </summary>
public sealed class TrackProjection
{
    /// <summary>
    /// 纬度 1 度对应的米数。
    /// 地球子午线方向基本是常数，可以直接用固定值。
    /// </summary>
    public const double MetersPerDegreeLatitude = 111320.0;

    /// <summary>
    /// 地球半径（Web Mercator 用），单位米。
    /// </summary>
    public const double EarthRadius = 6378137.0;

    public TrackProjection(double originLatitude, double originLongitude)
    {
        OriginLatitude = originLatitude;
        OriginLongitude = originLongitude;

        // Kept for callers that still need approximate ground degrees→meters.
        MetersPerDegreeLongitude =
            MetersPerDegreeLatitude
            * Math.Cos(originLatitude * Math.PI / 180.0);

        var (ox, oy) = ToMercator(originLatitude, originLongitude);
        OriginMercatorX = ox;
        OriginMercatorY = oy;
    }

    /// <summary>
    /// 投影原点（轨迹中心）的纬度，度。
    /// </summary>
    public double OriginLatitude { get; }

    /// <summary>
    /// 投影原点（轨迹中心）的经度，度。
    /// </summary>
    public double OriginLongitude { get; }

    /// <summary>
    /// 该纬度下 1 度经度对应的米数。
    /// </summary>
    public double MetersPerDegreeLongitude { get; }

    
    /// <summary>Origin easting in absolute Web Mercator meters.</summary>
    public double OriginMercatorX { get; }

    /// <summary>Origin northing in absolute Web Mercator meters.</summary>
    public double OriginMercatorY { get; }

    /// <summary>
    /// Local scale: ground meters ≈ mercator meters × cos(origin latitude).
    /// Web Mercator is conformal; use this when labelling distances on the plot.
    /// </summary>
    public double GroundMetersPerMercatorMeter =>
        Math.Max(0.05, Math.Cos(OriginLatitude * Math.PI / 180.0));

/// <summary>
    /// 经纬度 -> 米。
    /// </summary>
    public (double X, double Y) ToMeters(double latitude, double longitude)
    {
        var (mx, my) = ToMercator(latitude, longitude);
        return (mx - OriginMercatorX, my - OriginMercatorY);
    }

    /// <summary>
    /// 米 -> 经纬度。
    /// </summary>
    public (double Latitude, double Longitude) ToLatLon(
        double x,
        double y)
    {
        return FromMercator(x + OriginMercatorX, y + OriginMercatorY);
    }

    /// <summary>
    /// 用一批样本自动定出投影原点（轨迹的外接矩形中心）。
    /// </summary>
    /// <summary>
    /// 有效 GPS：非 NaN/Inf，且不能落在 (0,0) 附近（未定位占位）。
    /// </summary>
    public static bool IsValidGps(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsNaN(longitude) ||
            double.IsInfinity(latitude) || double.IsInfinity(longitude))
        {
            return false;
        }

        // 未定位常见占位
        if (Math.Abs(latitude) < 1e-5 && Math.Abs(longitude) < 1e-5)
        {
            return false;
        }

        if (latitude < -90.0 || latitude > 90.0 ||
            longitude < -180.0 || longitude > 180.0)
        {
            return false;
        }

        return true;
    }

    public static bool IsValidGps(VehicleSample sample) =>
        IsValidGps(sample.Latitude, sample.Longitude);

    /// <summary>
    /// 用一批样本自动定出投影原点（轨迹的外接矩形中心）。
    /// 自动跳过无效 GPS；若有效点不足则回退 (0,0)。
    /// </summary>
    public static TrackProjection FitToTrack(
        IReadOnlyList<VehicleSample> samples)
    {
        var minLat = double.MaxValue;
        var maxLat = double.MinValue;
        var minLon = double.MaxValue;
        var maxLon = double.MinValue;
        var any = false;

        foreach (var sample in samples)
        {
            if (!IsValidGps(sample))
                continue;

            any = true;
            minLat = Math.Min(minLat, sample.Latitude);
            maxLat = Math.Max(maxLat, sample.Latitude);
            minLon = Math.Min(minLon, sample.Longitude);
            maxLon = Math.Max(maxLon, sample.Longitude);
        }

        if (!any)
        {
            return new TrackProjection(0.0, 0.0);
        }

        return new TrackProjection(
            (minLat + maxLat) / 2.0,
            (minLon + maxLon) / 2.0);
    }

    /// <summary>
    /// 去掉离每条轨迹中位数过远的毛刺点（默认 5 km），
    /// 避免单个坏点把 Track Map 拉成跨城对角斜线。
    /// </summary>
    public static List<VehicleSample> FilterGpsOutliers(
        IReadOnlyList<VehicleSample> samples,
        double maxDistanceMeters = 5_000.0)
    {
        var valid = samples.Where(IsValidGps).ToList();
        if (valid.Count < 3)
            return valid;

        var lats = valid.Select(s => s.Latitude).OrderBy(v => v).ToList();
        var lons = valid.Select(s => s.Longitude).OrderBy(v => v).ToList();
        var medLat = lats[lats.Count / 2];
        var medLon = lons[lons.Count / 2];

        // 粗略平面距离（与 TrackProjection 同量级）
        var mPerLon = MetersPerDegreeLatitude *
                      Math.Cos(medLat * Math.PI / 180.0);
        var maxSq = maxDistanceMeters * maxDistanceMeters;

        return valid.Where(s =>
        {
            var dx = (s.Longitude - medLon) * mPerLon;
            var dy = (s.Latitude - medLat) * MetersPerDegreeLatitude;
            return dx * dx + dy * dy <= maxSq;
        }).ToList();
    }

    /// <summary>
    /// 经纬度 -> Web Mercator 米（全局坐标）。
    /// 给以后的地图瓦片图层用，当前显示路径不调用。
    /// </summary>
    public static (double X, double Y) ToMercator(
        double latitude,
        double longitude)
    {
        // Clamp latitude — Mercator diverges at the poles.
        var clamped = Math.Clamp(latitude, -85.05112878, 85.05112878);

        var x =
            longitude
            * Math.PI
            / 180.0
            * EarthRadius;

        var y =
            Math.Log(
                Math.Tan(
                    (90.0 + clamped)
                    * Math.PI
                    / 360.0))
            * EarthRadius;

        return (x, y);
    }

    /// <summary>
    /// Absolute Web Mercator meters → WGS84 lat/lon.
    /// </summary>
    public static (double Latitude, double Longitude) FromMercator(
        double x,
        double y)
    {
        var longitude = x / EarthRadius * 180.0 / Math.PI;
        var latitude =
            90.0
            - 2.0 * Math.Atan(Math.Exp(-y / EarthRadius)) * 180.0 / Math.PI;
        return (latitude, longitude);
    }

    /// <summary>
    /// 把一个"原始间隔"收敛成好看的刻度值：
    /// 1 / 2 / 5 / 10 / 20 / 50 / 100 / 200 / 500 / 1000 ...
    ///
    /// 只负责取整，不负责决定取多少格 —— 那是调用方按像素密度算的。
    /// </summary>
    public static double NiceDistanceStep(double rawStep)
    {
        if (rawStep <= 0.0 || double.IsNaN(rawStep))
        {
            return 1.0;
        }

        var magnitude =
            Math.Pow(10.0, Math.Floor(Math.Log10(rawStep)));

        var normalized = rawStep / magnitude;

        var nice = normalized switch
        {
            <= 1.0 => 1.0,
            <= 2.0 => 2.0,
            <= 5.0 => 5.0,
            _ => 10.0
        };

        return nice * magnitude;
    }

    /// <summary>
    /// 把距离格式化成刻度标签。
    /// 小于 1000 m 用米，超过用千米。
    /// </summary>
    public static string FormatDistance(double meters)
    {
        return Math.Abs(meters) >= 1000.0
            ? $"{meters / 1000.0:0.##}km"
            : $"{meters:0.##}m";
    }
}
