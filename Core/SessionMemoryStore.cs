using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using Chassis_Master_Test_Suite.Analysis;

namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// Persisted session snapshot: open VBOs, gates / last .vbts, Test Results UI, maths.
/// Stored under %LocalAppData%\CMTS\session-memory.json (same folder as dashboard-layout.json).
/// </summary>
public sealed class SessionMemorySnapshot
{
    public int Version { get; set; } = 1;

    public bool IsOfflineMode { get; set; }

    public List<string> VboFiles { get; set; } = new();

    public string? ActiveVboPath { get; set; }

    public string? LastVbtsPath { get; set; }

    public List<SessionGateDto> Gates { get; set; } = new();

    public Guid? SelectedGateId { get; set; }

    public Guid? AnalysisStartGateId { get; set; }

    public Guid? AnalysisEndGateId { get; set; }

    public TestResultsSettingsDto? TestResults { get; set; }

    public List<SessionMathsDto> MathsChannels { get; set; } = new();
}

public sealed class SessionGateDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "Gate";

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double WidthMeters { get; set; } = 20;

    public double? HeadingDeg { get; set; }

    public string ColorHex { get; set; } = "#3FBF6F";
}

public sealed class SessionMathsDto
{
    public string Id { get; set; } = "";

    public string Expression { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Unit { get; set; } = "";
}

public sealed class TestResultsSettingsDto
{
    /// <summary>Accel | Decel | Custom | Gate</summary>
    public string Type { get; set; } = "Accel";

    public string StartThreshold { get; set; } = "0";

    public string EndThreshold { get; set; } = "100";

    public string? ChannelId { get; set; }

    public bool ConditionsCollapsed { get; set; }

    public List<PassConditionSettingsDto> PassConditions { get; set; } = new();
}

public sealed class PassConditionSettingsDto
{
    public string ChannelId { get; set; } = "velocity";

    public string MinText { get; set; } = "78";

    public string MaxText { get; set; } = "83";

    public Guid? AtGateId { get; set; }
}

/// <summary>Load / save session memory JSON under LocalAppData\CMTS.</summary>
public static class SessionMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Last imported .vbts path (updated by Track Map Import; included in snapshot).</summary>
    public static string? LastImportedVbtsPath { get; set; }

    /// <summary>Test hook: when set, Load/Save use this path instead of LocalAppData.</summary>
    public static string? StoragePathOverride { get; set; }

    public static string StoragePath =>
        !string.IsNullOrWhiteSpace(StoragePathOverride)
            ? StoragePathOverride!
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CMTS",
                "session-memory.json");

    public static SessionMemorySnapshot? Load()
    {
        try
        {
            var path = StoragePath;
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            var snap = JsonSerializer.Deserialize<SessionMemorySnapshot>(json, JsonOptions);
            if (snap is null)
                return null;

            snap.MathsChannels ??= new List<SessionMathsDto>();
            snap.Gates ??= new List<SessionGateDto>();
            snap.VboFiles ??= new List<string>();

            if (!string.IsNullOrWhiteSpace(snap.LastVbtsPath))
                LastImportedVbtsPath = snap.LastVbtsPath;

            return snap;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(SessionMemorySnapshot snapshot)
    {
        try
        {
            var dir = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (string.IsNullOrWhiteSpace(snapshot.LastVbtsPath) &&
                !string.IsNullOrWhiteSpace(LastImportedVbtsPath))
                snapshot.LastVbtsPath = LastImportedVbtsPath;

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(StoragePath, json);
        }
        catch
        {
            // Best-effort; ignore disk errors.
        }
    }

    public static SessionGateDto FromGate(GateDefinition g) =>
        new()
        {
            Id = g.Id,
            Name = g.Name,
            Latitude = g.Latitude,
            Longitude = g.Longitude,
            WidthMeters = g.WidthMeters,
            HeadingDeg = g.HeadingDeg,
            ColorHex = g.ColorHex
        };

    public static GateDefinition ToGate(SessionGateDto d) =>
        new()
        {
            Id = d.Id == Guid.Empty ? Guid.NewGuid() : d.Id,
            Name = string.IsNullOrWhiteSpace(d.Name) ? "Gate" : d.Name,
            Latitude = d.Latitude,
            Longitude = d.Longitude,
            WidthMeters = d.WidthMeters > 0.1 ? d.WidthMeters : 20,
            HeadingDeg = d.HeadingDeg,
            ColorHex = string.IsNullOrWhiteSpace(d.ColorHex) ? "#3FBF6F" : d.ColorHex
        };

    public static SessionMathsDto FromMaths(MathsChannelDefinition d) =>
        new()
        {
            Id = d.Id,
            Expression = d.Expression,
            DisplayName = d.DisplayName,
            Unit = d.Unit
        };

    public static MathsChannelDefinition ToMaths(SessionMathsDto d) =>
        new()
        {
            Id = d.Id,
            Expression = d.Expression,
            DisplayName = d.DisplayName ?? "",
            Unit = d.Unit ?? ""
        };
}
