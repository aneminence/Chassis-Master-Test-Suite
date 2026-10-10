using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// Import virtual gates from a Racelogic VBOX Test Suite .vbts project
/// (PersistenceObject with nested escaped plugin XML).
/// </summary>
public static class VbtsGateImporter
{
    private const double MetersPerDegreeLatitude = 111320.0;

    /// <summary>Convert VBTS lat/lon stored in minutes to decimal degrees.</summary>
    public static double MinutesToDegrees(double minutes) => minutes / 60.0;

    public static IReadOnlyList<GateDefinition> ImportFile(string path)
    {
        var xml = File.ReadAllText(path);
        return ImportXml(xml);
    }

    public static IReadOnlyList<GateDefinition> ImportXml(string xmlOrEscaped)
    {
        if (string.IsNullOrWhiteSpace(xmlOrEscaped))
            return Array.Empty<GateDefinition>();

        // Decode nested HTML/XML entities until GateSettings appear or we stabilize.
        var text = DecodeUntilGatesVisible(xmlOrEscaped);
        var raw = ExtractGateSettingsBlocks(text);
        var gates = new List<GateDefinition>(raw.Count);
        foreach (var block in raw)
        {
            if (TryParseGateBlock(block, out var gate))
                gates.Add(gate);
        }

        return gates;
    }

    public static string DecodeUntilGatesVisible(string input)
    {
        var current = input;
        for (var i = 0; i < 8; i++)
        {
            // Decode first so "&lt;GateSettings&gt;" becomes a real tag, then stop.
            var next = System.Net.WebUtility.HtmlDecode(current);
            if (string.Equals(next, current, StringComparison.Ordinal))
                break;
            current = next;
            if (HasUnescapedGateTags(current))
                return current;
        }

        return current;
    }

    public static IReadOnlyList<string> ExtractGateSettingsBlocks(string text)
    {
        var list = new List<string>();
        // Match with or without namespace prefix: <d3p1:GateSettings>...</d3p1:GateSettings>
        var re = new Regex(
            @"<(?:[\w.]+:)?GateSettings\b[^>]*>(.*?)</(?:[\w.]+:)?GateSettings>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (Match m in re.Matches(text))
            list.Add(m.Groups[1].Value);
        return list;
    }

    public static bool TryParseGateBlock(string innerXml, out GateDefinition gate)
    {
        gate = null!;
        var caption = ReadTag(innerXml, "caption");
        var widthStr = ReadTag(innerXml, "gateWidth");
        var lat1Str = ReadTag(innerXml, "latiudeMinutes1") ?? ReadTag(innerXml, "latitudeMinutes1");
        var lat2Str = ReadTag(innerXml, "latiudeMinutes2") ?? ReadTag(innerXml, "latitudeMinutes2");
        var lon1Str = ReadTag(innerXml, "longitudeMinutes1");
        var lon2Str = ReadTag(innerXml, "longitudeMinutes2");

        if (lat1Str is null || lat2Str is null || lon1Str is null || lon2Str is null)
            return false;

        if (!TryParseDouble(lat1Str, out var lat1m) ||
            !TryParseDouble(lat2Str, out var lat2m) ||
            !TryParseDouble(lon1Str, out var lon1m) ||
            !TryParseDouble(lon2Str, out var lon2m))
            return false;

        var lat1 = MinutesToDegrees(lat1m);
        var lat2 = MinutesToDegrees(lat2m);
        var lon1 = MinutesToDegrees(lon1m);
        var lon2 = MinutesToDegrees(lon2m);

        var centerLat = (lat1 + lat2) * 0.5;
        var centerLon = (lon1 + lon2) * 0.5;

        // VBTS endpoint chord runs along the path; gate bar is drawn perpendicular
        // to HeadingDeg (travel). Use chord bearing as travel so bars cross the track.
        var lineBearing = BearingDeg(lat1, lon1, lat2, lon2);
        var travelHeading = NormalizeHeading(lineBearing);

        var width = 10.0;
        if (widthStr is not null && TryParseDouble(widthStr, out var w) && w > 0.1)
            width = w;
        else
        {
            // Fallback: chord length between endpoints.
            var chord = DistanceMeters(lat1, lon1, lat2, lon2);
            if (chord > 0.1)
                width = chord;
        }

        gate = new GateDefinition
        {
            Name = string.IsNullOrWhiteSpace(caption) ? "Gate" : caption.Trim(),
            Latitude = centerLat,
            Longitude = centerLon,
            WidthMeters = width,
            HeadingDeg = travelHeading
        };
        return gate.IsValid;
    }

    /// <summary>
    /// Bearing from (lat0,lon0) to (lat1,lon1): 0 = North, clockwise degrees.
    /// </summary>
    public static double BearingDeg(double lat0, double lon0, double lat1, double lon1)
    {
        var mPerLon = MetersPerDegreeLatitude * Math.Cos(lat0 * Math.PI / 180.0);
        var dx = (lon1 - lon0) * mPerLon; // east
        var dy = (lat1 - lat0) * MetersPerDegreeLatitude; // north
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
            return 0;
        return NormalizeHeading(Math.Atan2(dx, dy) * 180.0 / Math.PI);
    }

    public static double DistanceMeters(double lat0, double lon0, double lat1, double lon1)
    {
        var mPerLon = MetersPerDegreeLatitude * Math.Cos(lat0 * Math.PI / 180.0);
        var dx = (lon1 - lon0) * mPerLon;
        var dy = (lat1 - lat0) * MetersPerDegreeLatitude;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double NormalizeHeading(double deg)
    {
        deg %= 360.0;
        if (deg < 0) deg += 360.0;
        return deg;
    }

    private static string? ReadTag(string xml, string localName)
    {
        var re = new Regex(
            $@"<(?:[\w.]+:)?{Regex.Escape(localName)}\b[^>]*>(.*?)</(?:[\w.]+:)?{Regex.Escape(localName)}>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var m = re.Match(xml);
        if (!m.Success)
            return null;
        return m.Groups[1].Value.Trim();
    }

    private static bool HasUnescapedGateTags(string text) =>
        Regex.IsMatch(text, @"<(?:[\w.]+:)?GateSettings\b") &&
        Regex.IsMatch(text, @"<(?:[\w.]+:)?(?:latiudeMinutes1|latitudeMinutes1)\b", RegexOptions.IgnoreCase);

    private static bool TryParseDouble(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
