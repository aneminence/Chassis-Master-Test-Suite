namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// Measured channel value at an At-gate crossing for one Pass condition.
/// </summary>
public sealed class PassConditionMeasurement
{
    public string ChannelId { get; init; } = "";

    public string GateName { get; init; } = "";

    public Guid AtGateId { get; init; }

    /// <summary>Sampled value at gate crossing; null if missing / invalid.</summary>
    public double? Value { get; init; }

    public double Min { get; init; }

    public double Max { get; init; }

    /// <summary>True when Value is present and within [Min, Max].</summary>
    public bool InRange { get; init; }

    /// <summary>Column / chip header, e.g. velocity@Gate 2.</summary>
    public string Header =>
        string.IsNullOrWhiteSpace(GateName)
            ? ChannelId
            : $"{ChannelId}@{GateName}";

    public string ValueText =>
        Value is double v
            ? v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "-";

    public string RangeText =>
        $"[{Min.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)},{Max.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}]";
}

