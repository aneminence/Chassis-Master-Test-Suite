using System.Buffers.Binary;

namespace Chassis_Master_Test_Suite.Recorder.OxTS;

/// <summary>
/// Decodes OxTS NCOM structure-A packets (72 bytes, sync 0xE7).
/// Field layouts and scales from the official NCOM Manual (rev 260702).
/// Structure-B (nav status 11) and reserved statuses are skipped.
/// </summary>
public static class NcomDecoder
{
    public const int PacketLength = 72;
    public const byte SyncByte = 0xE7;

    private static readonly HashSet<byte> StructureAStatuses = new()
    {
        0, 1, 2, 3, 4, 5, 6, 7, 10, 20, 21, 22
    };

    /// <summary>Statuses where Batch A/B kinematics should be treated as usable navigation.</summary>
    private static readonly HashSet<byte> UsableNavStatuses = new()
    {
        3, 4, 20, 21, 22
    };

    public static IReadOnlyList<NcomMeasurement> DecodeAll(
        ReadOnlySpan<byte> buffer,
        IReadOnlyList<long?>? packetTimesNs = null)
    {
        var list = new List<NcomMeasurement>();
        var seq = 0;
        var i = 0;
        var timeIndex = 0;

        while (i + PacketLength <= buffer.Length)
        {
            if (buffer[i] != SyncByte)
            {
                i++;
                continue;
            }

            long? t = null;
            if (packetTimesNs is not null && timeIndex < packetTimesNs.Count)
                t = packetTimesNs[timeIndex];

            if (TryDecode(buffer.Slice(i, PacketLength), seq, t, out var m) && m is not null)
                list.Add(m);

            seq++;
            timeIndex++;
            i += PacketLength;
        }

        return list;
    }

    /// <summary>
    /// Decode consecutive 72-byte aligned packets (no resync hunt) — for XCOM demux payloads
    /// that are already packet-framed.
    /// </summary>
    public static IReadOnlyList<NcomMeasurement> DecodeFramedPackets(
        IEnumerable<(byte[] Payload, long? TimeNs)> packets)
    {
        var list = new List<NcomMeasurement>();
        long seq = 0;
        foreach (var (payload, timeNs) in packets)
        {
            if (payload.Length < PacketLength)
                continue;
            // One NCOM per XCOM data packet in our samples; if longer, walk 72-byte strides.
            for (var off = 0; off + PacketLength <= payload.Length; off += PacketLength)
            {
                if (payload[off] != SyncByte)
                    break;
                if (TryDecode(payload.AsSpan(off, PacketLength), seq, timeNs, out var m) && m is not null)
                    list.Add(m);
                seq++;
            }
        }

        return list;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> pkt,
        long sequence,
        long? timePrefixNs,
        out NcomMeasurement? measurement)
    {
        measurement = null;
        if (pkt.Length < PacketLength || pkt[0] != SyncByte)
            return false;

        var nav = pkt[21];
        if (nav == 11 || !StructureAStatuses.Contains(nav))
            return false; // structure-B or reserved — discard per manual

        if (!ChecksumOk(pkt, 1, 21, pkt[22]))
            return false;
        if (!ChecksumOk(pkt, 1, 60, pkt[61]))
            return false;
        if (!ChecksumOk(pkt, 1, 70, pkt[71]))
            return false;

        var timeMs = BinaryPrimitives.ReadUInt16LittleEndian(pkt.Slice(1, 2));
        var ax = ReadInt24(pkt, 3) * 1e-4;
        var ay = ReadInt24(pkt, 6) * 1e-4;
        var az = ReadInt24(pkt, 9) * 1e-4;
        var wx = ReadInt24(pkt, 12) * 1e-5;
        var wy = ReadInt24(pkt, 15) * 1e-5;
        var wz = ReadInt24(pkt, 18) * 1e-5;

        var latRad = BinaryPrimitives.ReadDoubleLittleEndian(pkt.Slice(23, 8));
        var lonRad = BinaryPrimitives.ReadDoubleLittleEndian(pkt.Slice(31, 8));
        var alt = BinaryPrimitives.ReadSingleLittleEndian(pkt.Slice(39, 4));
        var vn = ReadInt24(pkt, 43) * 1e-4;
        var ve = ReadInt24(pkt, 46) * 1e-4;
        var vd = ReadInt24(pkt, 49) * 1e-4;
        var headingRad = ReadInt24(pkt, 52) * 1e-6;
        var pitchRad = ReadInt24(pkt, 55) * 1e-6;
        var rollRad = ReadInt24(pkt, 58) * 1e-6;
        var statusChannel = pkt[62];

        var usable = UsableNavStatuses.Contains(nav) && nav != 0 && nav != 1;
        // nav 0 = all invalid; nav 1 = raw IMU only
        if (nav == 0)
            usable = false;

        measurement = new NcomMeasurement
        {
            Sequence = sequence,
            TimePrefixNs = timePrefixNs,
            TimeMsIntoMinute = timeMs,
            NavigationStatus = nav,
            StatusChannel = statusChannel,
            IsKinematicsValid = usable && nav != 1,
            IsImuValid = nav is 1 or 2 or 3 or 4 or 20 or 21 or 22,
            AccelXMps2 = ax,
            AccelYMps2 = ay,
            AccelZMps2 = az,
            AngRateXRadps = wx,
            AngRateYRadps = wy,
            AngRateZRadps = wz,
            LatitudeDeg = latRad * (180.0 / Math.PI),
            LongitudeDeg = lonRad * (180.0 / Math.PI),
            AltitudeM = alt,
            VelocityNorthMps = vn,
            VelocityEastMps = ve,
            VelocityDownMps = vd,
            HeadingDeg = headingRad * (180.0 / Math.PI),
            PitchDeg = pitchRad * (180.0 / Math.PI),
            RollDeg = rollRad * (180.0 / Math.PI)
        };
        return true;
    }

    private static bool ChecksumOk(ReadOnlySpan<byte> pkt, int from, int toInclusive, byte expected)
    {
        var sum = 0;
        for (var j = from; j <= toInclusive; j++)
            sum += pkt[j];
        return (byte)sum == expected;
    }

    private static int ReadInt24(ReadOnlySpan<byte> pkt, int offset)
    {
        var v = pkt[offset] | (pkt[offset + 1] << 8) | (pkt[offset + 2] << 16);
        if ((v & 0x800000) != 0)
            v -= 0x1000000;
        return v;
    }
}

public sealed class NcomMeasurement
{
    public long Sequence { get; init; }
    public long? TimePrefixNs { get; init; }
    public ushort TimeMsIntoMinute { get; init; }
    public byte NavigationStatus { get; init; }
    public byte StatusChannel { get; init; }
    public bool IsKinematicsValid { get; init; }
    public bool IsImuValid { get; init; }

    public double AccelXMps2 { get; init; }
    public double AccelYMps2 { get; init; }
    public double AccelZMps2 { get; init; }
    public double AngRateXRadps { get; init; }
    public double AngRateYRadps { get; init; }
    public double AngRateZRadps { get; init; }

    public double LatitudeDeg { get; init; }
    public double LongitudeDeg { get; init; }
    public double AltitudeM { get; init; }
    public double VelocityNorthMps { get; init; }
    public double VelocityEastMps { get; init; }
    public double VelocityDownMps { get; init; }
    public double HeadingDeg { get; init; }
    public double PitchDeg { get; init; }
    public double RollDeg { get; init; }

    public double SpeedHorizontalMps =>
        Math.Sqrt(VelocityNorthMps * VelocityNorthMps + VelocityEastMps * VelocityEastMps);

    public double YawRateDegps => AngRateZRadps * (180.0 / Math.PI);
}
