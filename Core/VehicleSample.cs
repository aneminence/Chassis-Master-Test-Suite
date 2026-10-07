namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// CMTS 中统一的车辆动态数据样本。
/// 所有数据源最终都应该转换成 VehicleSample。
/// </summary>
public sealed class VehicleSample
{
    /// <summary>
    /// 数据采集时间戳。
    /// 优先使用设备提供的时间。
    /// </summary>
    public long Timestamp { get; init; }

    /// <summary>
    /// 数据包序号。
    /// 用于检测丢包、乱序等情况。
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// 车辆速度，单位：km/h。
    /// </summary>
    public double SpeedKph { get; init; }

    /// <summary>
    /// 纵向加速度，单位：m/s²。
    /// </summary>
    public double LongitudinalAcceleration { get; init; }

    /// <summary>
    /// 横向加速度，单位：m/s²。
    /// </summary>
    public double LateralAcceleration { get; init; }

    /// <summary>
    /// 垂向加速度，单位：m/s²。
    /// </summary>
    public double VerticalAcceleration { get; init; }

    /// <summary>
    /// 横摆角速度，单位：deg/s。
    /// </summary>
    public double YawRate { get; init; }

    /// <summary>
    /// 纬度，单位：deg。
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// 经度，单位：deg。
    /// </summary>
    public double Longitude { get; init; }

    /// <summary>
    /// 海拔高度，单位：m。
    /// </summary>
    public double Altitude { get; init; }

    /// <summary>
    /// 航向角，单位：deg。
    /// </summary>
    public double Heading { get; init; }
}