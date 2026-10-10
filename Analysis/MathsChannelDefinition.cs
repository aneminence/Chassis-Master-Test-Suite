namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// 用户定义的数学通道：表达式结果写入 Channels[Id]。
/// </summary>
public sealed class MathsChannelDefinition
{
    public required string Id { get; init; }

    public required string Expression { get; init; }

    public string DisplayName { get; init; } = "";

    public string Unit { get; init; } = "";
}