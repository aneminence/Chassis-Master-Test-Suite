namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// Named virtual gates shared by Track Map and Test Results (analysis Start/End pick).
/// </summary>
public sealed class GateStore
{
    public static GateStore Instance { get; } = new();

    private readonly List<GateDefinition> _gates = new();

    public IReadOnlyList<GateDefinition> Gates => _gates;

    public Guid? SelectedId { get; private set; }

    public Guid? AnalysisStartId { get; private set; }

    public Guid? AnalysisEndId { get; private set; }

    public event EventHandler? Changed;

    public GateDefinition? Selected =>
        SelectedId is Guid id ? Find(id) : null;

    public GateDefinition? StartGate =>
        AnalysisStartId is Guid id ? Find(id) : null;

    public GateDefinition? EndGate =>
        AnalysisEndId is Guid id ? Find(id) : null;

    public GateDefinition? Find(Guid id) =>
        _gates.FirstOrDefault(g => g.Id == id);

    private static readonly string[] ColorPalette =
    {
        "#3FBF6F", "#4A9FD8", "#E08A4A", "#C06AD8", "#5AC8C8",
        "#E05252", "#D8D84A", "#C8A34A", "#7EB6FF", "#FF7EB6",
        "#A0E060", "#FFB347", "#B388FF", "#64FFDA", "#FF8A80"
    };

    public GateDefinition Add(GateDefinition gate)
    {
        if (string.IsNullOrWhiteSpace(gate.Name))
            gate.Name = NextDefaultName();
        if (string.IsNullOrWhiteSpace(gate.ColorHex) ||
            _gates.Any(g => string.Equals(g.ColorHex, gate.ColorHex, StringComparison.OrdinalIgnoreCase)))
            gate.ColorHex = NextUniqueColor();
        _gates.Add(gate);
        SelectedId = gate.Id;
        // Auto-assign analysis Start then End when unset.
        if (AnalysisStartId is null)
            AnalysisStartId = gate.Id;
        else if (AnalysisEndId is null && AnalysisStartId != gate.Id)
            AnalysisEndId = gate.Id;
        Raise();
        return gate;
    }

    public bool Remove(Guid id)
    {
        var n = _gates.RemoveAll(g => g.Id == id);
        if (n == 0)
            return false;
        if (SelectedId == id)
            SelectedId = _gates.FirstOrDefault()?.Id;
        if (AnalysisStartId == id)
            AnalysisStartId = null;
        if (AnalysisEndId == id)
            AnalysisEndId = null;
        Raise();
        return true;
    }

    /// <summary>
    /// Replace all gates atomically (session restore). Does not recolor or auto-assign roles.
    /// </summary>
    public void ReplaceAll(
        IEnumerable<GateDefinition> gates,
        Guid? selectedId = null,
        Guid? analysisStartId = null,
        Guid? analysisEndId = null)
    {
        _gates.Clear();
        foreach (var gate in gates)
        {
            if (string.IsNullOrWhiteSpace(gate.Name))
                gate.Name = NextDefaultName();
            if (string.IsNullOrWhiteSpace(gate.ColorHex))
                gate.ColorHex = NextUniqueColor();
            _gates.Add(gate);
        }

        SelectedId = selectedId is Guid sid && Find(sid) is not null
            ? sid
            : _gates.FirstOrDefault()?.Id;
        AnalysisStartId = analysisStartId is Guid a && Find(a) is not null ? a : null;
        AnalysisEndId = analysisEndId is Guid e && Find(e) is not null ? e : null;
        Raise();
    }

    public void Clear()
    {
        _gates.Clear();
        SelectedId = null;
        AnalysisStartId = null;
        AnalysisEndId = null;
        Raise();
    }

    public void Select(Guid? id)
    {
        if (id is Guid g && Find(g) is null)
            return;
        SelectedId = id;
        Raise();
    }

    public bool Rename(Guid id, string name)
    {
        var gate = Find(id);
        if (gate is null)
            return false;
        name = name.Trim();
        if (name.Length == 0)
            return false;
        gate.Name = name;
        Raise();
        return true;
    }

    public bool SetWidth(Guid id, double widthMeters)
    {
        var gate = Find(id);
        if (gate is null || widthMeters < 0.5)
            return false;
        gate.WidthMeters = widthMeters;
        Raise();
        return true;
    }

    public bool UpdatePose(Guid id, double lat, double lon, double? headingDeg)
    {
        var gate = Find(id);
        if (gate is null)
            return false;
        gate.Latitude = lat;
        gate.Longitude = lon;
        if (headingDeg is not null)
            gate.HeadingDeg = headingDeg;
        Raise();
        return true;
    }

    public void SetAnalysisStart(Guid? id)
    {
        if (id is Guid g && Find(g) is null)
            return;
        AnalysisStartId = id;
        Raise();
    }

    public void SetAnalysisEnd(Guid? id)
    {
        if (id is Guid g && Find(g) is null)
            return;
        AnalysisEndId = id;
        Raise();
    }

    /// <summary>Compatibility: add/replace analysis Start gate.</summary>
    public void SetStart(GateDefinition gate)
    {
        if (string.IsNullOrWhiteSpace(gate.Name))
            gate.Name = "Start";
        AddOrReplaceByAnalysisRole(gate, isStart: true);
    }

    public void SetEnd(GateDefinition gate)
    {
        if (string.IsNullOrWhiteSpace(gate.Name))
            gate.Name = "End";
        AddOrReplaceByAnalysisRole(gate, isStart: false);
    }

    private void AddOrReplaceByAnalysisRole(GateDefinition gate, bool isStart)
    {
        var roleId = isStart ? AnalysisStartId : AnalysisEndId;
        if (roleId is Guid existingId)
        {
            var existing = Find(existingId);
            if (existing is not null)
            {
                existing.Name = gate.Name;
                existing.Latitude = gate.Latitude;
                existing.Longitude = gate.Longitude;
                existing.WidthMeters = gate.WidthMeters > 0.1 ? gate.WidthMeters : existing.WidthMeters;
                existing.HeadingDeg = gate.HeadingDeg ?? existing.HeadingDeg;
                SelectedId = existing.Id;
                Raise();
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(gate.ColorHex) ||
            _gates.Any(g => string.Equals(g.ColorHex, gate.ColorHex, StringComparison.OrdinalIgnoreCase)))
            gate.ColorHex = NextUniqueColor();
        _gates.Add(gate);
        SelectedId = gate.Id;
        if (isStart)
            AnalysisStartId = gate.Id;
        else
            AnalysisEndId = gate.Id;
        Raise();
    }

    private string NextDefaultName()
    {
        var n = 1;
        while (_gates.Any(g =>
                   string.Equals(g.Name, $"Gate {n}", StringComparison.OrdinalIgnoreCase)))
            n++;
        return $"Gate {n}";
    }


    private string NextUniqueColor()
    {
        var used = new HashSet<string>(
            _gates.Select(g => g.ColorHex),
            StringComparer.OrdinalIgnoreCase);
        foreach (var c in ColorPalette)
        {
            if (!used.Contains(c))
                return c;
        }

        // Palette exhausted: synthesize distinct-ish colors.
        for (var i = 0; i < 64; i++)
        {
            var hue = (i * 47) % 360;
            var hex = HslToHex(hue, 0.65, 0.55);
            if (!used.Contains(hex))
                return hex;
        }

        return ColorPalette[_gates.Count % ColorPalette.Length];
    }

    private static string HslToHex(int h, double s, double l)
    {
        static double Hue2Rgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        var hk = h / 360.0;
        var r = Hue2Rgb(p, q, hk + 1.0 / 3);
        var g = Hue2Rgb(p, q, hk);
        var b = Hue2Rgb(p, q, hk - 1.0 / 3);
        return $"#{(int)(r * 255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}";
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
