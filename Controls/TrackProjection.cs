using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// Track Map 用到的坐标换算。
///
/// 三个坐标系：
///     经纬度     度（VehicleSample 里的原始值）
///     米         以轨迹中心为原点、正东为 +X、正北为 +Y 的平面
///     墨卡托     Web Mercator 米，给以后的在线地图瓦片用
///
/// 显示用"米"这一层（局部等距投影）。
/// 对一条几公里内的测试轨迹，它和 Web Mercator 的差别小于 1 米，
/// 但换算简单得多。
/// 真贴地图瓦片时瓦片走 ToMercator()，轨迹走 ToMeters()，
/// 两者在几公里内不会错位。
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

        // 经度方向必须按纬度收缩：
        // 北纬 33 度处 1 度经度只有 93 km，而 1 度纬度有 111 km。
        // 不乘这个因子，圆形轨迹会被拉成椭圆。
        MetersPerDegreeLongitude =
            MetersPerDegreeLatitude
            * Math.Cos(originLatitude * Math.PI / 180.0);
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

    /// <summary>
    /// 经纬度 -> 米。
    /// </summary>
    public (double X, double Y) ToMeters(double latitude, double longitude)
    {
        var x =
            (longitude - OriginLongitude)
            * MetersPerDegreeLongitude;

        var y =
            (latitude - OriginLatitude)
            * MetersPerDegreeLatitude;

        return (x, y);
    }

    /// <summary>
    /// 米 -> 经纬度。
    /// </summary>
    public (double Latitude, double Longitude) ToLatLon(
        double x,
        double y)
    {
        var longitude =
            OriginLongitude
            + x / MetersPerDegreeLongitude;

        var latitude =
            OriginLatitude
            + y / MetersPerDegreeLatitude;

        return (latitude, longitude);
    }

    /// <summary>
    /// 用一批样本自动定出投影原点（轨迹的外接矩形中心）。
    /// </summary>
    public static TrackProjection FitToTrack(
        IReadOnlyList<VehicleSample> samples)
    {
        if (samples.Count == 0)
        {
            return new TrackProjection(0.0, 0.0);
        }

        var minLat = double.MaxValue;
        var maxLat = double.MinValue;
        var minLon = double.MaxValue;
        var maxLon = double.MinValue;

        foreach (var sample in samples)
        {
            minLat = Math.Min(minLat, sample.Latitude);
            maxLat = Math.Max(maxLat, sample.Latitude);
            minLon = Math.Min(minLon, sample.Longitude);
            maxLon = Math.Max(maxLon, sample.Longitude);
        }

        return new TrackProjection(
            (minLat + maxLat) / 2.0,
            (minLon + maxLon) / 2.0);
    }

    /// <summary>
    /// 经纬度 -> Web Mercator 米（全局坐标）。
    /// 给以后的地图瓦片图层用，当前显示路径不调用。
    /// </summary>
    public static (double X, double Y) ToMercator(
        double latitude,
        double longitude)
    {
        // 纬度必须夹住，两极的 Mercator 会趋于无穷。
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
