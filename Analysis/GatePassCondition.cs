namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// Pass when channel value at the moment the path crosses AtGate is within [Min, Max].
/// </summary>
public sealed class GatePassCondition
{
    public string ChannelId { get; set; } = Core.ChannelIds.Velocity;

    public double Min { get; set; }

    public double Max { get; set; }

    public Guid AtGateId { get; set; }
}
