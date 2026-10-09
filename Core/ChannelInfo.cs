namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// 可绘制 / 可选择的数据通道描述。
/// </summary>
public sealed class ChannelInfo : IEquatable<ChannelInfo>
{
    public ChannelInfo(
        string id,
        string displayName,
        string unit,
        bool isSyntheticTime = false)
    {
        Id = id;
        DisplayName = displayName;
        Unit = unit;
        IsSyntheticTime = isSyntheticTime;
    }

    /// <summary>稳定 Id（VBO 列短名或合成轴 Id）。</summary>
    public string Id { get; }

    public string DisplayName { get; }

    public string Unit { get; }

    /// <summary>是否为合成时间轴（仅作 X 轴，不用 VBO time 列）。</summary>
    public bool IsSyntheticTime { get; }

    public override string ToString() => DisplayName;

    public bool Equals(ChannelInfo? other) =>
        other is not null &&
        string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) =>
        obj is ChannelInfo other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
}
