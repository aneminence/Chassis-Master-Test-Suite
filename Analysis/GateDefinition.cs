namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// Virtual gate: GPS center + segment perpendicular to heading (or track), width in meters.
/// </summary>
public sealed class GateDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Gate";

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>Gate width (meters). Default 20.</summary>
    public double WidthMeters { get; set; } = 20;

    /// <summary>
    /// Travel heading deg (0=N, clockwise). Gate line is perpendicular.
    /// Null = infer from track segment when placing / drawing.
    /// </summary>
    public double? HeadingDeg { get; set; }

    /// <summary>Unique display color for map / legend (e.g. #3FBF6F).</summary>
    public string ColorHex { get; set; } = "#3FBF6F";

    public bool IsValid =>
        !double.IsNaN(Latitude) && !double.IsNaN(Longitude) &&
        WidthMeters > 0.1;
}
