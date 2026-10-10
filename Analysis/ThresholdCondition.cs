namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// 单点阈值条件：通道 + 阈值 + 穿越方向。
/// </summary>
public sealed class ThresholdCondition
{
    public required string ChannelId { get; init; }

    public double Threshold { get; init; }

    public CrossingDirection Direction { get; init; }
}