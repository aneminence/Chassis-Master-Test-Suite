namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// 通道 Id 约定：与 Racelogic VBO [column names] 短名一致（大小写不敏感）。
/// 合成时间轴单独用 <see cref="AxisTime"/>，与文件里的 time 列区分。
/// </summary>
public static class ChannelIds
{
    /// <summary>X 轴合成时间（北京时间），不是 VBO 的 time 列。</summary>
    public const string AxisTime = "__axis_time__";

    /// <summary>测试经过时间（秒，相对本会话/本文件第一条样本）。</summary>
    public const string ElapsedTest = "__elapsed_test__";

    /// <summary>行驶距离（米，累计 Haversine）。</summary>
    public const string DistanceTraveled = "__distance_m__";

    public const string Velocity = "velocity";
    public const string Longacc = "Longacc";
    public const string Latacc = "Latacc";
    public const string ZAccel = "Z_Accel";
    public const string YawRate = "Yaw_Rate";
    public const string Heading = "heading";
    public const string Latitude = "lat";
    public const string Longitude = "long";
    public const string Height = "height";
}
