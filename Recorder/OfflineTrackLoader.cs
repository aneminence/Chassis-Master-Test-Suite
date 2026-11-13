using System.IO;
using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder.OxTS;
using Chassis_Master_Test_Suite.Session;

namespace Chassis_Master_Test_Suite.Recorder;

public sealed record LoadedOfflineTrack(string Path, string? Name,
    IReadOnlyList<VehicleSample> Samples, IReadOnlyList<string> Columns,
    KmlTrack? Kml = null, bool RememberColor = true);

public sealed record OfflineTrackLoadResult(
    IReadOnlyList<LoadedOfflineTrack> Tracks, SessionMetadata? Metadata = null,
    string? Warning = null);

/// <summary>File parsing and enrichment only; never changes UI or session globals.</summary>
public static class OfflineTrackLoader
{
    public static OfflineTrackLoadResult Load(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".kml" or ".kmz")
        {
            var kml = KmlTrackImporter.ImportFile(path);
            var raw = KmlTrackImporter.ToVehicleSamples(kml);
            if (raw.Count == 0) return new(Array.Empty<LoadedOfflineTrack>());
            var samples = Enrich(raw);
            cancellationToken.ThrowIfCancellationRequested();
            return new(new[] { new LoadedOfflineTrack(path, kml.Name, samples,
                samples[0].Channels.Keys.ToList(), kml) });
        }
        if (ext is ".xcom" or ".ncom")
        {
            var imported = ext == ".ncom" ? null : XcomImporter.ImportFile(path, cancellationToken);
            var rawTracks = imported?.Tracks ?? new[] { XcomImporter.ImportNcomFile(path) };
            var tracks = new List<LoadedOfflineTrack>();
            foreach (var track in rawTracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (track.Samples.Count == 0) continue;
                var samples = Enrich(track.Samples);
                var virtualPath = ext == ".ncom" ? path : $"{path}#{track.StreamSerial}-{track.Kind}";
                tracks.Add(new(virtualPath, track.DisplayName, samples,
                    Columns(track.ColumnNames, samples), RememberColor: false));
            }
            return new(tracks, Warning: imported is not null && tracks.Count == 0
                ? $"XCOM has no usable samples:\n{path}\n\n{imported.Summary}" : null);
        }
        var reader = new VboReader(path);
        var data = reader.ReadAll();
        if (data.Count == 0) return new(Array.Empty<LoadedOfflineTrack>());
        var enriched = Enrich(data);
        cancellationToken.ThrowIfCancellationRequested();
        return new(new[] { new LoadedOfflineTrack(path, null, enriched, Columns(reader.Columns, enriched)) },
            reader.SessionData.Count > 0 ? SessionMetadata.FromVboLines(reader.SessionData) : null);
    }

    private static IReadOnlyList<VehicleSample> Enrich(IReadOnlyList<VehicleSample> samples) =>
        SlopeEnricher.EnrichAll(SampleEnricher.EnrichAll(samples));

    private static List<string> Columns(IReadOnlyList<string> source, IReadOnlyList<VehicleSample> samples)
    {
        var columns = source.Count > 0 ? source.ToList() : samples[0].Channels.Keys.ToList();
        if (!columns.Contains(ChannelIds.SlopePct, StringComparer.OrdinalIgnoreCase))
            columns.Add(ChannelIds.SlopePct);
        return columns;
    }
}
