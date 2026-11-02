using System.Collections.Concurrent;

namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// Thread-safe roster of live vehicles (GSpot multi-car rooms).
/// Keeps latest sample + a short trail per vehicle for map markers / chips.
/// </summary>
public sealed class LiveVehicleRoster
{
    private static readonly string[] Palette =
    {
        "#3FBF6F", "#4A9FD8", "#E08A4A", "#C06AD8", "#5AC8C8",
        "#E05252", "#D8D84A", "#C8A34A", "#7EB6FF", "#FF7EB6",
        "#A0E060", "#FFB347", "#B388FF", "#64FFDA", "#FF8A80"
    };

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _focusGate = new();
    private string? _focusId;
    private int _colorCursor;

    public int Count => _entries.Count;

    public string? FocusId
    {
        get
        {
            lock (_focusGate)
                return _focusId;
        }
    }

    public void Clear()
    {
        _entries.Clear();
        lock (_focusGate)
            _focusId = null;
        Interlocked.Exchange(ref _colorCursor, 0);
    }

    /// <summary>
    /// Upsert a sample. Returns the resolved vehicle key.
    /// First vehicle becomes focus when none is set.
    /// </summary>
    public string Upsert(VehicleSample sample, int trailCapacity = 240)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (trailCapacity < 1)
            trailCapacity = 1;

        var key = ResolveKey(sample);
        var label = string.IsNullOrWhiteSpace(sample.VehicleLabel)
            ? key
            : sample.VehicleLabel!;

        var entry = _entries.GetOrAdd(key, _ =>
        {
            var colorIndex = Interlocked.Increment(ref _colorCursor) - 1;
            return new Entry(key, label, Palette[colorIndex % Palette.Length], trailCapacity);
        });

        entry.Update(sample, label);

        lock (_focusGate)
        {
            _focusId ??= key;
        }

        return key;
    }

    public bool TrySetFocus(string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(vehicleId))
            return false;
        if (!_entries.ContainsKey(vehicleId))
            return false;

        lock (_focusGate)
            _focusId = vehicleId;
        return true;
    }

    public bool TrySetFocusByLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;

        foreach (var kv in _entries)
        {
            if (string.Equals(kv.Value.Label, label, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kv.Key, label, StringComparison.OrdinalIgnoreCase))
            {
                lock (_focusGate)
                    _focusId = kv.Key;
                return true;
            }
        }

        return false;
    }

    public VehicleSample? GetFocusLatest()
    {
        var focus = FocusId;
        if (focus is null)
            return null;
        return _entries.TryGetValue(focus, out var e) ? e.Latest : null;
    }

    public IReadOnlyList<VehicleSnapshot> Snapshot()
    {
        var focus = FocusId;
        var list = new List<VehicleSnapshot>(_entries.Count);
        foreach (var kv in _entries)
        {
            var e = kv.Value;
            list.Add(new VehicleSnapshot(
                e.Id,
                e.Label,
                e.ColorHex,
                e.Latest,
                e.TrailSnapshot(),
                string.Equals(e.Id, focus, StringComparison.Ordinal)));
        }

        list.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static string ResolveKey(VehicleSample sample) =>
        !string.IsNullOrWhiteSpace(sample.VehicleId)
            ? sample.VehicleId.Trim()
            : (!string.IsNullOrWhiteSpace(sample.VehicleLabel)
                ? sample.VehicleLabel.Trim()
                : "default");

    public readonly record struct VehicleSnapshot(
        string Id,
        string Label,
        string ColorHex,
        VehicleSample? Latest,
        IReadOnlyList<VehicleSample> Trail,
        bool IsFocus);

    private sealed class Entry
    {
        private readonly object _gate = new();
        private readonly VehicleSample?[] _trail;
        private int _head;
        private int _count;

        public Entry(string id, string label, string colorHex, int trailCapacity)
        {
            Id = id;
            Label = label;
            ColorHex = colorHex;
            _trail = new VehicleSample?[trailCapacity];
        }

        public string Id { get; }
        public string ColorHex { get; }
        public string Label { get; private set; }
        public VehicleSample? Latest { get; private set; }

        public void Update(VehicleSample sample, string label)
        {
            lock (_gate)
            {
                Label = label;
                Latest = sample;
                _trail[_head] = sample;
                _head = (_head + 1) % _trail.Length;
                if (_count < _trail.Length)
                    _count++;
            }
        }

        public IReadOnlyList<VehicleSample> TrailSnapshot()
        {
            lock (_gate)
            {
                if (_count == 0)
                    return Array.Empty<VehicleSample>();

                var result = new VehicleSample[_count];
                var start = _count < _trail.Length ? 0 : _head;
                for (var i = 0; i < _count; i++)
                {
                    var index = (start + i) % _trail.Length;
                    result[i] = _trail[index]!;
                }

                return result;
            }
        }
    }
}
