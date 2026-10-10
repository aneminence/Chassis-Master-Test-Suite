using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// One computed run bound to the sample list it was detected in (supports multi-file).
/// </summary>
public sealed class AnnotatedTestRun
{
    public required TestRunResult Result { get; init; }

    public required IReadOnlyList<VehicleSample> Samples { get; init; }

    public string SourceLabel { get; init; } = "";

    public string ColorHex { get; init; } = "#C8A34A";

    public Guid? SourceFileId { get; init; }

    public int RunNumber => Result.RunNumber;
}