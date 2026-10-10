using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// Labeled sample buffer (one open VBO / live history) for multi-file Compute.
/// </summary>
public sealed class SampleSource
{
    public Guid? Id { get; init; }

    public required string Label { get; init; }

    public string ColorHex { get; init; } = "#C8A34A";

    public required IReadOnlyList<VehicleSample> Samples { get; init; }
}