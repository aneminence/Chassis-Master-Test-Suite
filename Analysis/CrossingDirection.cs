namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// 阈值穿越方向。
/// </summary>
public enum CrossingDirection
{
    /// <summary>自下而上穿越（加速过点）。</summary>
    Rising,

    /// <summary>自上而下穿越（减速过点）。</summary>
    Falling
}