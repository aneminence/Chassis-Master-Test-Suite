using Chassis_Master_Test_Suite.Core;
using System.IO;

namespace Chassis_Master_Test_Suite.Recorder.OxTS;

/// <summary>
/// Imports an OxTS XCOM file into one or more CMTS offline datasets
/// (one <see cref="ImportedTrack"/> per NCOM stream; optional RCOM track with relative channels).
/// </summary>
public static class XcomImporter
{

    public sealed class ImportedTrack
    {
        public required string DisplayName { get; init; }
        public required string SourcePath { get; init; }
        public required XcomStreamKind Kind { get; init; }
        public required uint StreamSerial { get; init; }
        public required IReadOnlyList<VehicleSample> Samples { get; init; }
        public required IReadOnlyList<string> ColumnNames { get; init; }
        public string? Diagnostics { get; init; }
    }

    public sealed class ImportResult
    {
        public required IReadOnlyList<ImportedTrack> Tracks { get; init; }
        public required int PageCount { get; init; }
        public required string Summary { get; init; }
    }

    public static ImportResult ImportFile(string path, CancellationToken cancellationToken = default)
    {
        var xcom = XcomContainerReader.ReadFile(path, cancellationToken);
        return Import(xcom, path, cancellationToken);
    }

    public static ImportResult Import(XcomFile xcom, string sourcePath, CancellationToken cancellationToken = default)
    {
        var tracks = new List<ImportedTrack>();
        var baseName = Path.GetFileName(sourcePath);

        foreach (var stream in xcom.Streams)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefineLabel(stream);

            switch (stream.Kind)
            {
                case XcomStreamKind.Ncom:
                    tracks.Add(ImportNcomStream(stream, sourcePath, baseName));
                    break;
                case XcomStreamKind.Rcom:
                    tracks.Add(ImportRcomStream(stream, sourcePath, baseName));
                    break;
                default:
                    // Unsupported codec — skip with note in summary only.
                    break;
            }
        }

        var summary =
            $"{xcom.PageCount} pages · {xcom.Streams.Count} streams · " +
            $"{tracks.Count} imported track(s) · " +
            string.Join(", ",
                xcom.Streams.Select(s => $"s{s.Serial}:{s.Kind}"));

        return new ImportResult
        {
            Tracks = tracks,
            PageCount = xcom.PageCount,
            Summary = summary
        };
    }

    /// <summary>Import a raw standalone .ncom file (no XCOM wrapper).</summary>
    public static ImportedTrack ImportNcomFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var measurements = NcomDecoder.DecodeAll(bytes);
        var samples = ToVehicleSamples(measurements);
        var cols = samples.Count > 0
            ? samples[0].Channels.Keys.ToList()
            : new List<string>();
        return new ImportedTrack
        {
            DisplayName = Path.GetFileName(path),
            SourcePath = path,
            Kind = XcomStreamKind.Ncom,
            StreamSerial = 0,
            Samples = samples,
            ColumnNames = cols
        };
    }

    private static void RefineLabel(XcomStream stream)
    {
        var who = stream.FindAuxValue("WhoAmI");
        var grouping = stream.FindAuxValue("Grouping");
        var ip = stream.FindAuxValue("Address.IPv4");
        var parts = new List<string> { $"s{stream.Serial}", stream.Kind.ToString() };
        if (!string.IsNullOrWhiteSpace(who))
            parts.Add(who!);
        if (!string.IsNullOrWhiteSpace(grouping))
            parts.Add("g" + grouping);
        if (!string.IsNullOrWhiteSpace(ip))
            parts.Add(ip!);
        stream.Label = string.Join("-", parts);
    }

    private static ImportedTrack ImportNcomStream(XcomStream stream, string sourcePath, string baseName)
    {
        var framed = stream.DataPackets
            .Select(p => (p.Payload, p.TimePrefixNs))
            .ToList();
        var measurements = NcomDecoder.DecodeFramedPackets(framed);
        var samples = ToVehicleSamples(measurements);
        var cols = samples.Count > 0
            ? samples[0].Channels.Keys.ToList()
            : new List<string>();

        return new ImportedTrack
        {
            DisplayName = $"{baseName}#{stream.Label}",
            SourcePath = sourcePath,
            Kind = XcomStreamKind.Ncom,
            StreamSerial = stream.Serial,
            Samples = samples,
            ColumnNames = cols,
            Diagnostics = $"ncom packets decoded={measurements.Count} dataPkts={framed.Count}"
        };
    }

    private static ImportedTrack ImportRcomStream(XcomStream stream, string sourcePath, string baseName)
    {
        var payloads = stream.DataPackets.Select(p => p.Payload).ToList();
        var times = stream.DataPackets.Select(p => p.TimePrefixNs).ToList();
        var decoded = RcomDecoder.DecodeFramedPackets(payloads);

        // Align extended-range rows to time prefixes of type-0x02 packets only — approximate
        // by enumerating data packets and decoding one-by-one for timing.
        var samples = new List<VehicleSample>();
        long seq = 0;
        var extIndex = 0;
        foreach (var pkt in stream.DataPackets)
        {
            if (pkt.Payload.Length < 2 || pkt.Payload[0] != RcomDecoder.SyncByte)
                continue;
            if (pkt.Payload[1] != RcomDecoder.TypeExtendedRange)
                continue;
            if (extIndex >= decoded.ExtendedRanges.Count)
                break;
            var er = decoded.ExtendedRanges[extIndex++];
            samples.Add(ToRcomSample(er, seq++, pkt.TimePrefixNs));
        }

        var cols = samples.Count > 0
            ? samples[0].Channels.Keys.ToList()
            : new List<string>();

        var skipped = string.Join(",", decoded.SkippedTypeCounts.Select(kv => $"0x{kv.Key:X2}={kv.Value}"));
        return new ImportedTrack
        {
            DisplayName = $"{baseName}#{stream.Label}",
            SourcePath = sourcePath,
            Kind = XcomStreamKind.Rcom,
            StreamSerial = stream.Serial,
            Samples = samples,
            ColumnNames = cols,
            Diagnostics =
                $"rcom totalPkts={decoded.TotalPackets} extRange={decoded.ExtendedRanges.Count} skipped=[{skipped}]"
        };
    }

    private static List<VehicleSample> ToVehicleSamples(IReadOnlyList<NcomMeasurement> measurements)
    {
        var list = new List<VehicleSample>(measurements.Count);
        long seq = 0;
        foreach (var m in measurements)
        {
            if (!m.IsKinematicsValid && !m.IsImuValid)
                continue;

            var speedKph = m.IsKinematicsValid ? m.SpeedHorizontalMps * 3.6 : double.NaN;
            var lat = m.IsKinematicsValid ? m.LatitudeDeg : double.NaN;
            var lon = m.IsKinematicsValid ? m.LongitudeDeg : double.NaN;
            var alt = m.IsKinematicsValid ? m.AltitudeM : double.NaN;
            var heading = m.IsKinematicsValid ? NormalizeHeading(m.HeadingDeg) : double.NaN;

            // Host-frame accel: OxTS X forward, Y right, Z down (typical). Map to CMTS Long/Lat/Vert.
            var longAcc = m.IsImuValid ? m.AccelXMps2 : double.NaN;
            var latAcc = m.IsImuValid ? m.AccelYMps2 : double.NaN;
            var vertAcc = m.IsImuValid ? -m.AccelZMps2 : double.NaN; // CMTS Z up-ish vs OxTS down
            var yaw = m.IsImuValid ? m.YawRateDegps : double.NaN;

            var channels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                [ChannelIds.Velocity] = speedKph,
                [ChannelIds.Longacc] = longAcc,
                [ChannelIds.Latacc] = latAcc,
                [ChannelIds.ZAccel] = vertAcc,
                [ChannelIds.YawRate] = yaw,
                [ChannelIds.Heading] = heading,
                [ChannelIds.Latitude] = lat,
                [ChannelIds.Longitude] = lon,
                [ChannelIds.Height] = alt,
                ["vn_mps"] = m.VelocityNorthMps,
                ["ve_mps"] = m.VelocityEastMps,
                ["vd_mps"] = m.VelocityDownMps,
                ["pitch_deg"] = m.PitchDeg,
                ["roll_deg"] = m.RollDeg,
                ["nav_status"] = m.NavigationStatus,
                ["wx_degps"] = m.AngRateXRadps * (180.0 / Math.PI),
                ["wy_degps"] = m.AngRateYRadps * (180.0 / Math.PI),
                ["ax_mps2"] = m.AccelXMps2,
                ["ay_mps2"] = m.AccelYMps2,
                ["az_mps2"] = m.AccelZMps2
            };

            list.Add(new VehicleSample
            {
                Timestamp = ToTimestampMs(m.TimePrefixNs),
                Sequence = seq++,
                SpeedKph = double.IsNaN(speedKph) ? 0 : speedKph,
                LongitudinalAcceleration = double.IsNaN(longAcc) ? 0 : longAcc,
                LateralAcceleration = double.IsNaN(latAcc) ? 0 : latAcc,
                VerticalAcceleration = double.IsNaN(vertAcc) ? 0 : vertAcc,
                YawRate = double.IsNaN(yaw) ? 0 : yaw,
                Heading = double.IsNaN(heading) ? 0 : heading,
                Latitude = double.IsNaN(lat) ? 0 : lat,
                Longitude = double.IsNaN(lon) ? 0 : lon,
                Altitude = double.IsNaN(alt) ? 0 : alt,
                Channels = channels
            });
        }

        return list;
    }

    private static VehicleSample ToRcomSample(RcomExtendedRange er, long seq, long? timeNs)
    {
        static double OrNan(double? v) => v ?? double.NaN;

        var channels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["rcom_target"] = er.TargetNumber,
            ["rcom_targets"] = er.TotalTargets,
            ["range_lat_m"] = OrNan(er.LateralRangeM),
            ["range_long_m"] = OrNan(er.LongitudinalRangeM),
            ["range_lat_rate_mps"] = OrNan(er.LateralRangeRateMps),
            ["range_long_rate_mps"] = OrNan(er.LongitudinalRangeRateMps),
            ["hunter_hdg_deg"] = OrNan(er.HunterHeadingDeg),
            ["target_hdg_deg"] = OrNan(er.TargetHeadingDeg),
            ["hunter_fwd_mps"] = OrNan(er.HunterForwardVelocityMps),
            ["hunter_lat_mps"] = OrNan(er.HunterLateralVelocityMps),
            ["range_lat_acc_mps2"] = OrNan(er.LateralRangeAccelMps2),
            ["range_long_acc_mps2"] = OrNan(er.LongitudinalRangeAccelMps2),
            ["range_status"] = er.RangeStatus,
            // No absolute lat/lon in Ext.range alone — leave core GPS at 0 so TrackMap skips.
            [ChannelIds.Latitude] = 0,
            [ChannelIds.Longitude] = 0,
            [ChannelIds.Height] = 0,
            [ChannelIds.Velocity] = er.HunterForwardVelocityMps is double v ? Math.Abs(v) * 3.6 : 0,
            [ChannelIds.Heading] = er.HunterHeadingDeg ?? 0,
            [ChannelIds.Longacc] = 0,
            [ChannelIds.Latacc] = 0,
            [ChannelIds.ZAccel] = 0,
            [ChannelIds.YawRate] = 0
        };

        return new VehicleSample
        {
            Timestamp = ToTimestampMs(timeNs),
            Sequence = seq,
            SpeedKph = channels[ChannelIds.Velocity],
            Heading = channels[ChannelIds.Heading],
            Channels = channels
        };
    }

    private static long ToTimestampMs(long? timePrefixNs)
    {
        if (timePrefixNs is null || timePrefixNs.Value <= 0)
            return 0;
        var ns = timePrefixNs.Value;
        if (ns <= XcomContainerReader.GpsKnownGranuleThresholdNs)
        {
            // Free-running / unknown GPS — use as relative ns from 0.
            return ns / 1_000_000L;
        }

        // GPS time in nanoseconds since GPS epoch.
        try
        {
            var ms = ns / 1_000_000L;
            return ms;
        }
        catch
        {
            return 0;
        }
    }

    private static double NormalizeHeading(double deg)
    {
        // OxTS heading is ±180° from north; CMTS/VBO often 0..360. Keep signed; UI tolerates both.
        return deg;
    }
}
