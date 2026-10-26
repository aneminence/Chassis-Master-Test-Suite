using System.Globalization;
using System.Xml.Linq;

namespace Chassis_Master_Test_Suite.Analysis;

public enum KmlCutMode
{
    BeforeCursor,
    AfterCursor
}

public sealed record KmlEditMark(Guid Id, int SampleIndex);

/// <summary>In-memory KML working copy and its independent cut marks.</summary>
public sealed class KmlEditSession
{
    private readonly List<KmlEditMark> _marks = new();

    public KmlEditSession(KmlTrack track)
    {
        WorkingTrack = track ?? throw new ArgumentNullException(nameof(track));
    }

    public KmlTrack WorkingTrack { get; private set; }
    public IReadOnlyList<KmlEditMark> Marks => _marks;
    public Guid? SelectedMarkId { get; private set; }
    public bool IsDirty { get; private set; }

    public KmlEditMark AddMark(int sampleIndex)
    {
        if (WorkingTrack.Points.Count == 0)
            throw new InvalidOperationException("The KML track is empty.");
        var mark = new KmlEditMark(
            Guid.NewGuid(),
            Math.Clamp(sampleIndex, 0, WorkingTrack.Points.Count - 1));
        _marks.Add(mark);
        SelectedMarkId = mark.Id;
        return mark;
    }

    public bool SelectMark(Guid id)
    {
        if (_marks.All(m => m.Id != id))
            return false;
        SelectedMarkId = id;
        return true;
    }

    public bool RemoveSelectedMark()
    {
        if (SelectedMarkId is not Guid id)
            return false;
        var removed = _marks.RemoveAll(m => m.Id == id) > 0;
        SelectedMarkId = _marks.LastOrDefault()?.Id;
        return removed;
    }

    public void Cut(KmlCutMode mode)
    {
        var selected = SelectedMarkId is Guid id
            ? _marks.FirstOrDefault(m => m.Id == id)
            : null;
        if (selected is null)
            throw new InvalidOperationException("Select a cut mark first.");

        var cutIndex = selected.SampleIndex;
        WorkingTrack = KmlTrackCutter.Cut(WorkingTrack, mode, cutIndex);

        if (mode == KmlCutMode.BeforeCursor)
        {
            _marks.RemoveAll(m => m.SampleIndex < cutIndex);
            for (var i = 0; i < _marks.Count; i++)
                _marks[i] = _marks[i] with { SampleIndex = _marks[i].SampleIndex - cutIndex };
        }
        else
        {
            _marks.RemoveAll(m => m.SampleIndex > cutIndex);
        }

        if (SelectedMarkId is Guid selectedId && _marks.All(m => m.Id != selectedId))
            SelectedMarkId = _marks.LastOrDefault()?.Id;
        IsDirty = true;
    }

    public void MarkSaved() => IsDirty = false;

    public void Reset(KmlTrack track)
    {
        WorkingTrack = track ?? throw new ArgumentNullException(nameof(track));
        _marks.Clear();
        SelectedMarkId = null;
        IsDirty = false;
    }
}

/// <summary>
/// Non-destructive KML cutting. The source is parsed into a normalized gx:Track and
/// the retained points are written to a new file, leaving the original untouched.
/// </summary>
public static class KmlTrackCutter
{
    private static readonly XNamespace KmlNs = "http://www.opengis.net/kml/2.2";
    private static readonly XNamespace GxNs = "http://www.google.com/kml/ext/2.2";

    public static KmlTrack Cut(KmlTrack source, KmlCutMode mode, int firstIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Points.Count < 2)
            throw new InvalidOperationException("The KML track has fewer than two points.");

        firstIndex = Math.Clamp(firstIndex, 0, source.Points.Count - 1);
        List<KmlTrackPoint> kept;
        switch (mode)
        {
            case KmlCutMode.BeforeCursor:
                kept = source.Points.Skip(firstIndex).ToList();
                break;
            case KmlCutMode.AfterCursor:
                kept = source.Points.Take(firstIndex + 1).ToList();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (kept.Count < 2)
            throw new InvalidOperationException("Cut would leave fewer than two track points.");

        return new KmlTrack
        {
            Name = source.Name,
            SourcePath = null,
            Points = kept
        };
    }

    public static void ExportFile(KmlTrack track, string path)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (track.Points.Count < 2)
            throw new InvalidOperationException("A KML track needs at least two points.");

        var gxTrack = new XElement(GxNs + "Track",
            new XElement(KmlNs + "altitudeMode", "absolute"));

        // KML gx:Track permits missing timestamps poorly across viewers. When the
        // source is untimed, emit a stable synthetic sequence starting at Unix epoch.
        var synthetic = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < track.Points.Count; i++)
        {
            var point = track.Points[i];
            var when = point.TimeUtc ?? synthetic.AddSeconds(i);
            gxTrack.Add(new XElement(KmlNs + "when", when.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)));
            gxTrack.Add(new XElement(GxNs + "coord", string.Create(CultureInfo.InvariantCulture,
                $"{point.Longitude:R} {point.Latitude:R} {(point.AltitudeMeters ?? 0):R}")));
        }

        // Keep the phone-track values understood by KmlTrackImporter. Stored speed
        // is m/s in this extension, while KmlTrackPoint exposes km/h.
        if (track.Points.Any(p => p.SpeedKph is not null || p.AccuracyMeters is not null))
        {
            var values = string.Join(";", track.Points.Select(p =>
                string.Create(CultureInfo.InvariantCulture,
                    $"{(p.SpeedKph is double kph ? kph / 3.6 : 0):R},{(p.AccuracyMeters ?? 0):R}")));
            gxTrack.Add(new XElement(KmlNs + "ExtendedData",
                new XElement(KmlNs + "Data",
                    new XAttribute("name", "GxTrackExtendedData"),
                    new XElement(KmlNs + "value", values))));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(KmlNs + "kml",
                new XAttribute(XNamespace.Xmlns + "gx", GxNs),
                new XElement(KmlNs + "Document",
                    new XElement(KmlNs + "name", track.Name),
                    new XElement(KmlNs + "Placemark",
                        new XElement(KmlNs + "name", track.Name),
                        gxTrack))));

        document.Save(path);
    }
}
