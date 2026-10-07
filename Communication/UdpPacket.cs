namespace Chassis_Master_Test_Suite.Communication;

/// <summary>
/// CMTS V0.1 UDP 数据包。
/// 当前用于 Simulator 与 CMTS 之间的内部测试通信。
/// </summary>
public sealed class UdpPacket
{
    /// <summary>
    /// 协议版本。
    /// </summary>
    public byte Version { get; init; } = 1;

    /// <summary>
    /// 数据包序号。
    /// 用于检测丢包和乱序。
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// 设备时间戳，单位：ms。
    /// </summary>
    public long Timestamp { get; init; }

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
    /// 海拔，单位：m。
    /// </summary>
    public double Altitude { get; init; }

    /// <summary>
    /// 航向角，单位：deg。
    /// </summary>
    public double Heading { get; init; }
}