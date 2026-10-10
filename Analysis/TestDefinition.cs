namespace Chassis_Master_Test_Suite.Analysis;

using Chassis_Master_Test_Suite.Core;

/// <summary>
/// 一次试验定义（P0：速度起止阈值）。
/// </summary>
public sealed class TestDefinition
{
    public TestType Type { get; init; }

    public ThresholdCondition Start { get; init; } = null!;

    public ThresholdCondition End { get; init; } = null!;

    /// <summary>
    /// 可选：最长允许时长（秒）。超过则 Pass=false；未设置则完成即 Pass。
    /// </summary>
    public double? MaxDurationSeconds { get; init; }

    /// <summary>
    /// 穿越迟滞（通道单位，速度为 km/h）。用于跑完后重新布防，避免抖动重复切段。
    /// </summary>
    public double Hysteresis { get; init; } = 0.5;

    public static TestDefinition Accel(double startKph = 0, double endKph = 100) =>
        new()
        {
            Type = TestType.Accel,
            Start = new ThresholdCondition
            {
                ChannelId = ChannelIds.Velocity,
                Threshold = startKph,
                Direction = CrossingDirection.Rising
            },
            End = new ThresholdCondition
            {
                ChannelId = ChannelIds.Velocity,
                Threshold = endKph,
                Direction = CrossingDirection.Rising
            }
        };

    public static TestDefinition Decel(double startKph = 100, double endKph = 0) =>
        new()
        {
            Type = TestType.Decel,
            Start = new ThresholdCondition
            {
                ChannelId = ChannelIds.Velocity,
                Threshold = startKph,
                Direction = CrossingDirection.Falling
            },
            End = new ThresholdCondition
            {
                ChannelId = ChannelIds.Velocity,
                Threshold = endKph,
                Direction = CrossingDirection.Falling
            }
        };

    public static TestDefinition CustomSpeed(
        double startKph,
        double endKph,
        string channelId = ChannelIds.Velocity)
    {
        var rising = endKph >= startKph;
        return new TestDefinition
        {
            Type = TestType.Custom,
            Start = new ThresholdCondition
            {
                ChannelId = channelId,
                Threshold = startKph,
                Direction = rising ? CrossingDirection.Rising : CrossingDirection.Falling
            },
            End = new ThresholdCondition
            {
                ChannelId = channelId,
                Threshold = endKph,
                Direction = rising ? CrossingDirection.Rising : CrossingDirection.Falling
            }
        };
    }

    /// <summary>Gate-to-gate: thresholds unused; Start/End are placeholders.</summary>
    public static TestDefinition Gate() =>
        new()
        {
            Type = TestType.Gate,
            Start = new ThresholdCondition
            {
                ChannelId = ChannelIds.Velocity,
                Threshold = 0,
                Direction = CrossingDirection.Rising
            },
            End = new ThresholdCondition
            {
                ChannelId = ChannelIds.Velocity,
                Threshold = 0,
                Direction = CrossingDirection.Rising
            }
        };
}