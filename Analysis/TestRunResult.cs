namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// One detected run for DataGrid / CSV / chart annotation.
/// </summary>
public sealed class TestRunResult
{
    public int RunNumber { get; init; }

    /// <summary>Source file display name (e.g. ons shot 7.vbo); empty for live.</summary>
    public string SourceFile { get; init; } = "";

    public string SourceColorHex { get; init; } = "#C8A34A";

    public Guid? SourceFileId { get; init; }

    /// <summary>Start timestamp (Unix ms), same basis as VehicleSample.Timestamp.</summary>
    public long StartTimestampMs { get; init; }

    public long EndTimestampMs { get; init; }

    /// <summary>Duration in seconds.</summary>
    public double DurationSeconds { get; init; }

    public double StartSpeedKph { get; init; }

    public double EndSpeedKph { get; init; }

    /// <summary>Speed change (End - Start), km/h.</summary>
    public double DeltaSpeedKph { get; init; }

    /// <summary>
    /// Distance of the run (m): DistanceTraveled / speed integral / Haversine.
    /// </summary>
    public double DistanceMeters { get; init; }

    /// <summary>How distance was estimated (debug / CSV).</summary>
    public string DistanceMethod { get; init; } = "";

    public bool Pass { get; init; }

    public string FailReason { get; init; } = "";

    /// <summary>
    /// Per Pass-condition measured values at At-gate crossings (Gate tests).
    /// Empty for Accel/Decel/Custom without Pass conditions.
    /// </summary>
    public IReadOnlyList<PassConditionMeasurement> PassMeasurements { get; init; } =
        Array.Empty<PassConditionMeasurement>();

    /// <summary>Inclusive sample indices in the source buffer.</summary>
    public int StartSampleIndex { get; init; }

    public int EndSampleIndex { get; init; }
}
