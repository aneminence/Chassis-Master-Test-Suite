using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// One track point from a KML/KMZ file (WGS84).
/// Speed/accuracy are optional (e.g. 两步路 GxTrackExtendedData) and are
/// phone-track values — not VBOX Doppler speed.
/// </summary>
public sealed class KmlTrackPoint
{
    public double Longitude { get; init; }
    public double Latitude { get; init; }
    public double? AltitudeMeters { get; init; }
    public DateTimeOffset? TimeUtc { get; init; }
    public double? SpeedKph { get; init; }
    public double? AccuracyMeters { get; init; }
}

/// <summary>
/// Parsed KML/KMZ track ready for TrackMap / offline overlay.
/// </summary>
public sealed class KmlTrack
{
    public required string Name { get; init; }
    public string? SourcePath { get; init; }
    public required IReadOnlyList<KmlTrackPoint> Points { get; init; }
}

/// <summary>
/// Import trajectories from KML / KMZ.
/// Supports gx:Track (incl. 两步路: all coords then all when + GxTrackExtendedData)
/// and basic LineString coordinates (draw-only, no time).
/// </summary>
public static class KmlTrackImporter
{
    private static readonly XNamespace KmlNs = "http://www.opengis.net/kml/2.2";
    private static readonly XNamespace GxNs = "http://www.google.com/kml/ext/2.2";

    public static KmlTrack ImportFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var ext = System.IO.Path.GetExtension(path);
        if (ext.Equals(".kmz", StringComparison.OrdinalIgnoreCase))
            return ImportKmz(path);

        var xml = System.IO.File.ReadAllText(path);
        return ImportXml(xml, path);
    }

    public static KmlTrack ImportKmz(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.EndsWith("/", StringComparison.Ordinal))
            ?? throw new InvalidDataException("KMZ contains no .kml entry.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd();
        return ImportXml(xml, path);
    }

    public static KmlTrack ImportXml(string xml, string? sourcePath = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidDataException("KML is empty.");

        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = doc.Root
            ?? throw new InvalidDataException("KML has no root element.");

        var name = FirstDescendantValue(root, "name")
            ?? (sourcePath is not null ? System.IO.Path.GetFileNameWithoutExtension(sourcePath) : "KML track");

        // Prefer gx:Track (timed); fall back to LineString.
        var trackEl = root.Descendants(GxNs + "Track").FirstOrDefault()
            ?? root.Descendants().FirstOrDefault(e =>
                e.Name.LocalName.Equals("Track", StringComparison.OrdinalIgnoreCase)
                && e.Name.NamespaceName.Contains("google.com/kml/ext", StringComparison.OrdinalIgnoreCase));

        if (trackEl is not null)
        {
            var points = ParseGxTrack(trackEl);
            if (points.Count == 0)
                throw new InvalidDataException("gx:Track has no coordinates.");
            return new KmlTrack
            {
                Name = name,
                SourcePath = sourcePath,
                Points = points
            };
        }

        var lineString = root.Descendants(KmlNs + "LineString").FirstOrDefault()
            ?? root.Descendants().FirstOrDefault(e =>
                e.Name.LocalName.Equals("LineString", StringComparison.OrdinalIgnoreCase));

        if (lineString is not null)
        {
            var points = ParseLineString(lineString);
            if (points.Count == 0)
                throw new InvalidDataException("LineString has no coordinates.");
            return new KmlTrack
            {
                Name = name,
                SourcePath = sourcePath,
                Points = points
            };
        }

        throw new InvalidDataException("KML has neither gx:Track nor LineString.");
    }

    /// <summary>
    /// Convert a parsed track into VehicleSample rows for OfflineFileSet / TrackMap.
    /// Timestamps are Unix ms when TimeUtc is present; otherwise sequential 1 Hz.
    /// Speed from GxTrackExtendedData is stored as velocity (phone track, not VBOX).
    /// </summary>
    public static List<VehicleSample> ToVehicleSamples(KmlTrack track)
    {
        var list = new List<VehicleSample>(track.Points.Count);
        long seq = 0;
        foreach (var p in track.Points)
        {
            long ts;
            if (p.TimeUtc is DateTimeOffset dto)
                ts = dto.ToUnixTimeMilliseconds();
            else
                ts = seq * 1000; // synthetic 1 Hz for LineString-only

            var speed = p.SpeedKph ?? 0.0;
            var alt = p.AltitudeMeters ?? 0.0;
            var channels = VehicleSample.BuildCoreChannels(
                speed,
                longitudinalAcceleration: 0,
                lateralAcceleration: 0,
                verticalAcceleration: 0,
                yawRate: 0,
                heading: 0,
                latitude: p.Latitude,
                longitude: p.Longitude,
                altitude: alt);

            // Keep accuracy as an extra channel for future use (not shown in phase1 UI).
            if (p.AccuracyMeters is double acc)
                channels["kml_accuracy_m"] = acc;

            list.Add(new VehicleSample
            {
                Timestamp = ts,
                Sequence = seq,
                SpeedKph = speed,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Altitude = alt,
                Channels = channels
            });
            seq++;
        }

        return list;
    }

    private static List<KmlTrackPoint> ParseGxTrack(XElement trackEl)
    {
        // 两步路 (and some exporters) emit ALL gx:coord first, then ALL when.
        // Spec allows interleaving; zip by index either way.
        var coords = new List<(double Lon, double Lat, double? Alt)>();
        var whens = new List<DateTimeOffset?>();

        foreach (var child in trackEl.Elements())
        {
            var local = child.Name.LocalName;
            if (local.Equals("coord", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseCoord(child.Value, out var lon, out var lat, out var alt))
                    coords.Add((lon, lat, alt));
            }
            else if (local.Equals("when", StringComparison.OrdinalIgnoreCase))
            {
                whens.Add(TryParseWhen(child.Value));
            }
        }

        var speeds = new List<(double? Speed, double? Acc)>();
        var ext = trackEl.Descendants().FirstOrDefault(e =>
            e.Name.LocalName.Equals("Data", StringComparison.OrdinalIgnoreCase)
            && string.Equals((string?)e.Attribute("name"), "GxTrackExtendedData", StringComparison.OrdinalIgnoreCase));

        if (ext is not null)
        {
            var valueEl = ext.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase));
            var raw = valueEl?.Value ?? ext.Value;
            speeds = ParseGxTrackExtendedData(raw);
        }

        var count = coords.Count;
        var points = new List<KmlTrackPoint>(count);
        for (var i = 0; i < count; i++)
        {
            var (lon, lat, alt) = coords[i];
            DateTimeOffset? when = i < whens.Count ? whens[i] : null;
            double? speed = null;
            double? acc = null;
            if (i < speeds.Count)
            {
                speed = speeds[i].Speed;
                acc = speeds[i].Acc;
            }

            points.Add(new KmlTrackPoint
            {
                Longitude = lon,
                Latitude = lat,
                AltitudeMeters = alt,
                TimeUtc = when,
                SpeedKph = speed,
                AccuracyMeters = acc
            });
        }

        return points;
    }

    private static List<KmlTrackPoint> ParseLineString(XElement lineString)
    {
        var coordEl = lineString.Elements().FirstOrDefault(e =>
            e.Name.LocalName.Equals("coordinates", StringComparison.OrdinalIgnoreCase));
        if (coordEl is null)
            return new List<KmlTrackPoint>();

        var points = new List<KmlTrackPoint>();
        // coordinates: "lon,lat[,alt] lon,lat[,alt] ..." whitespace-separated tuples
        var tokens = coordEl.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var parts = token.Split(',');
            if (parts.Length < 2)
                continue;
            if (!TryParseDouble(parts[0], out var lon) ||
                !TryParseDouble(parts[1], out var lat))
                continue;
            double? alt = null;
            if (parts.Length >= 3 && TryParseDouble(parts[2], out var a))
                alt = a;

            points.Add(new KmlTrackPoint
            {
                Longitude = lon,
                Latitude = lat,
                AltitudeMeters = alt
            });
        }

        return points;
    }

    internal static List<(double? Speed, double? Acc)> ParseGxTrackExtendedData(string raw)
    {
        var list = new List<(double? Speed, double? Acc)>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;

        foreach (var pair in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = pair.Trim();
            if (trimmed.Length == 0)
                continue;
            var comma = trimmed.IndexOf(',');
            if (comma < 0)
            {
                if (TryParseDouble(trimmed, out var onlySpeed))
                    list.Add((onlySpeed, null));
                else
                    list.Add((null, null));
                continue;
            }

            var speedStr = trimmed[..comma].Trim();
            var accStr = trimmed[(comma + 1)..].Trim();
            double? speed = TryParseDouble(speedStr, out var s) ? s : null;
            double? acc = TryParseDouble(accStr, out var a) ? a : null;
            list.Add((speed, acc));
        }

        return list;
    }

    private static bool TryParseCoord(string text, out double lon, out double lat, out double? alt)
    {
        lon = 0;
        lat = 0;
        alt = null;
        var parts = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;
        if (!TryParseDouble(parts[0], out lon) || !TryParseDouble(parts[1], out lat))
            return false;
        if (parts.Length >= 3 && TryParseDouble(parts[2], out var a))
            alt = a;
        return true;
    }

    private static DateTimeOffset? TryParseWhen(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (DateTimeOffset.TryParse(
                text.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
            return dto;
        return null;
    }

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    private static string? FirstDescendantValue(XElement root, string localName)
    {
        var el = root.Descendants().FirstOrDefault(e =>
            e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase)
            && e.Parent is not null
            && (e.Parent.Name.LocalName.Equals("Document", StringComparison.OrdinalIgnoreCase)
                || e.Parent.Name.LocalName.Equals("Placemark", StringComparison.OrdinalIgnoreCase)
                || e.Parent.Name.LocalName.Equals("Folder", StringComparison.OrdinalIgnoreCase)));
        // Prefer Document/name over nested Style names etc.
        var docName = root.Descendants().FirstOrDefault(e =>
            e.Name.LocalName.Equals("Document", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase));
        if (docName is not null && !string.IsNullOrWhiteSpace(docName.Value))
            return docName.Value.Trim();

        return el is null || string.IsNullOrWhiteSpace(el.Value) ? null : el.Value.Trim();
    }
}
