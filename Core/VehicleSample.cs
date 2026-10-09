namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// CMTS 中统一的车辆动态数据样本。
/// 所有数据源最终都应该转换成 VehicleSample。
/// </summary>
public sealed class VehicleSample
{
    private static readonly IReadOnlyDictionary<string, double> EmptyChannels =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

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
    /// 纬度，单位：deg。正 = 北纬，负 = 南纬。
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// 经度，单位：deg。正 = 东经，负 = 西经。
    ///
    /// 注意：这个约定和 Racelogic VBO 文件里的符号是相反的
    /// （VBO 里负值是东经）。换算在 VboReader / VboRecorder 里做，
    /// 不许直接拷贝，否则轨迹会左右镜像。
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

    /// <summary>
    /// 全部通道数值，键为 VBO 列短名（或 live 核心 Id）。
    /// UI / 曲线统一通过 <see cref="GetChannel"/> 读取。
    /// </summary>
    public IReadOnlyDictionary<string, double> Channels { get; init; } =
        EmptyChannels;

    /// <summary>
    /// 按通道 Id 取值。优先 Channels；核心字段有 typed 回退。
    /// </summary>
    public double GetChannel(string channelId)
    {
        if (Channels.TryGetValue(channelId, out var value))
            return value;

        return channelId switch
        {
            ChannelIds.Velocity => SpeedKph,
            ChannelIds.Longacc => LongitudinalAcceleration,
            ChannelIds.Latacc => LateralAcceleration,
            ChannelIds.ZAccel => VerticalAcceleration,
            ChannelIds.YawRate => YawRate,
            ChannelIds.Heading => Heading,
            ChannelIds.Latitude => Latitude,
            ChannelIds.Longitude => Longitude,
            ChannelIds.Height => Altitude,
            _ => 0.0
        };
    }

    /// <summary>
    /// 用当前 typed 核心字段生成 Channels 字典（供 live 源构造样本）。
    /// </summary>
    public static Dictionary<string, double> BuildCoreChannels(
        double speedKph,
        double longitudinalAcceleration,
        double lateralAcceleration,
        double verticalAcceleration,
        double yawRate,
        double heading,
        double latitude,
        double longitude,
        double altitude) =>
        ChannelRegistry.BuildCoreChannelMap(
            speedKph,
            longitudinalAcceleration,
            lateralAcceleration,
            verticalAcceleration,
            yawRate,
            heading,
            latitude,
            longitude,
            altitude);
}
