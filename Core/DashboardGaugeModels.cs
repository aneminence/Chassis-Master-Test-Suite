using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chassis_Master_Test_Suite.Core;

/// <summary>Where a dashboard gauge reads its value.</summary>
public enum GaugeBindingKind
{
    /// <summary>Live / replay VehicleSample channel (ChannelIds / maths).</summary>
    LiveChannel = 0,

    /// <summary>Field from the selected Test Results run.</summary>
    TestResultField = 1,

    /// <summary>Pass-condition measurement (e.g. velocity@Gate 2).</summary>
    PassMeasurement = 2
}

/// <summary>Stable ids for <see cref="GaugeBindingKind.TestResultField"/>.</summary>
public static class TestResultFieldIds
{
    public const string Duration = "duration";
    public const string DeltaSpeed = "delta_v";
    public const string Distance = "distance";
    public const string StartSpeed = "start_speed";
    public const string EndSpeed = "end_speed";
    public const string PassFail = "pass";
    public const string RunNumber = "run_number";
}

/// <summary>One gauge binding (live channel, test field, or pass measurement).</summary>
public sealed class GaugeBinding
{
    public GaugeBindingKind Kind { get; set; } = GaugeBindingKind.LiveChannel;

    /// <summary>
    /// Live: channel id. Test: TestResultFieldIds. Pass: header e.g. velocity@Gate 2.
    /// </summary>
    public string Key { get; set; } = ChannelIds.Velocity;

    public static GaugeBinding Live(string channelId) =>
        new() { Kind = GaugeBindingKind.LiveChannel, Key = channelId };

    public static GaugeBinding TestField(string fieldId) =>
        new() { Kind = GaugeBindingKind.TestResultField, Key = fieldId };

    public static GaugeBinding Pass(string header) =>
        new() { Kind = GaugeBindingKind.PassMeasurement, Key = header };

    public string ToStorageKey() =>
        Kind switch
        {
            GaugeBindingKind.LiveChannel => $"live:{Key}",
            GaugeBindingKind.TestResultField => $"test:{Key}",
            GaugeBindingKind.PassMeasurement => $"pass:{Key}",
            _ => $"live:{Key}"
        };

    public static GaugeBinding Parse(string? storage)
    {
        if (string.IsNullOrWhiteSpace(storage))
            return Live(ChannelIds.Velocity);

        var s = storage.Trim();
        var colon = s.IndexOf(':');
        if (colon <= 0 || colon >= s.Length - 1)
            return Live(s);

        var prefix = s[..colon];
        var key = s[(colon + 1)..];
        return prefix.ToLowerInvariant() switch
        {
            "live" => Live(key),
            "test" => TestField(key),
            "pass" => Pass(key),
            _ => Live(s)
        };
    }
}

/// <summary>Persisted layout of one Default numeric gauge tile.</summary>
public sealed class DashboardGaugeLayout
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "Speed";

    public string Unit { get; set; } = "km/h";

    /// <summary>Stored as live:/test:/pass: key.</summary>
    public string BindingKey { get; set; } = "live:velocity";

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 140;

    public double Height { get; set; } = 96;

    [JsonIgnore]
    public GaugeBinding Binding
    {
        get => GaugeBinding.Parse(BindingKey);
        set => BindingKey = value.ToStorageKey();
    }
}

/// <summary>Session / disk persistence for the customizable Dashboard canvas.</summary>
public sealed class DashboardLayoutStore
{
    public static DashboardLayoutStore Instance { get; } = new(persistToDisk: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly bool _persistToDisk;

    private List<DashboardGaugeLayout> _gauges = CreateDefaults();

    public DashboardLayoutStore() : this(persistToDisk: false)
    {
    }

    private DashboardLayoutStore(bool persistToDisk)
    {
        _persistToDisk = persistToDisk;
    }

    public IReadOnlyList<DashboardGaugeLayout> Gauges => _gauges;

    public event EventHandler? Changed;

    public static string StoragePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CMTS",
            "dashboard-layout.json");

    public void ReplaceAll(IEnumerable<DashboardGaugeLayout> gauges)
    {
        _gauges = gauges.Select(Clone).ToList();
        Raise();
    }

    public DashboardGaugeLayout Add(DashboardGaugeLayout gauge)
    {
        var copy = Clone(gauge);
        if (copy.Id == Guid.Empty)
            copy.Id = Guid.NewGuid();
        _gauges.Add(copy);
        Raise();
        return copy;
    }

    public bool Remove(Guid id)
    {
        var n = _gauges.RemoveAll(g => g.Id == id);
        if (n > 0)
            Raise();
        return n > 0;
    }

    public void Update(DashboardGaugeLayout gauge)
    {
        var i = _gauges.FindIndex(g => g.Id == gauge.Id);
        if (i < 0)
            return;
        _gauges[i] = Clone(gauge);
        Raise();
    }

    public void ResetToDefaults()
    {
        _gauges = CreateDefaults();
        Raise();
    }

    public void Load()
    {
        try
        {
            var path = StoragePath;
            if (!File.Exists(path))
            {
                _gauges = CreateDefaults();
                return;
            }

            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<DashboardGaugeLayout>>(json, JsonOptions);
            _gauges = list is { Count: > 0 }
                ? list.Select(Clone).ToList()
                : CreateDefaults();
        }
        catch
        {
            _gauges = CreateDefaults();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_gauges, JsonOptions);
            File.WriteAllText(StoragePath, json);
        }
        catch
        {
            // Best-effort session persistence; ignore disk errors.
        }
    }

    public static List<DashboardGaugeLayout> CreateDefaults() =>
        new()
        {
            new()
            {
                Title = "Speed",
                Unit = "km/h",
                BindingKey = $"live:{ChannelIds.Velocity}",
                X = 8, Y = 8, Width = 268, Height = 118
            },
            new()
            {
                Title = "Yaw Rate",
                Unit = "deg/s",
                BindingKey = $"live:{ChannelIds.YawRate}",
                X = 8, Y = 136, Width = 130, Height = 96
            },
            new()
            {
                Title = "Lateral Accel.",
                Unit = "m/s2",
                BindingKey = $"live:{ChannelIds.Latacc}",
                X = 146, Y = 136, Width = 130, Height = 96
            },
            new()
            {
                Title = "Longitudinal Acc.",
                Unit = "m/s2",
                BindingKey = $"live:{ChannelIds.Longacc}",
                X = 8, Y = 242, Width = 130, Height = 96
            },
            new()
            {
                Title = "Heading",
                Unit = "deg",
                BindingKey = $"live:{ChannelIds.Heading}",
                X = 146, Y = 242, Width = 130, Height = 96
            }
        };

    private static DashboardGaugeLayout Clone(DashboardGaugeLayout g) =>
        new()
        {
            Id = g.Id == Guid.Empty ? Guid.NewGuid() : g.Id,
            Title = g.Title,
            Unit = g.Unit,
            BindingKey = g.BindingKey,
            X = g.X,
            Y = g.Y,
            Width = g.Width,
            Height = g.Height
        };

    private void Raise()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        if (_persistToDisk)
            Save();
    }
}