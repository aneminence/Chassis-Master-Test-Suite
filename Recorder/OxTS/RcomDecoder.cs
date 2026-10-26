using System.Buffers.Binary;

namespace Chassis_Master_Test_Suite.Recorder.OxTS;

/// <summary>
/// Decodes OxTS RCOM packets (RT-Range). Header: sync 0x57, type, length.
/// Extended range (type 0x02) fields from RCOM Manual Table 16.
/// Unknown / unsupported types are counted and skipped (do not fail the file).
/// </summary>
public static class RcomDecoder
{
    public const byte SyncByte = 0x57;
    public const byte TypeLane = 0x01;
    public const byte TypeExtendedRange = 0x02;
    public const byte TypeWrappedNcom = 0x03;
    public const byte TypeTrigger = 0x04;
    public const byte TypePolygon = 0x05;
    public const byte TypeMultiSensor = 0x06;

    public static RcomDecodeResult DecodeAll(ReadOnlySpan<byte> buffer)
    {
        var ext = new List<RcomExtendedRange>();
        var skipped = new Dictionary<byte, int>();
        var i = 0;
        var packets = 0;

        while (i + 4 <= buffer.Length)
        {
            if (buffer[i] != SyncByte)
            {
                var next = buffer.Slice(i + 1).IndexOf(SyncByte);
                if (next < 0)
                    break;
                i += 1 + next;
                continue;
            }

            var type = buffer[i + 1];
            var dataLen = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(i + 2, 2));
            var total = 4 + dataLen;
            if (i + total > buffer.Length)
                break;

            packets++;
            var pkt = buffer.Slice(i, total);
            switch (type)
            {
                case TypeExtendedRange:
                    if (TryParseExtended(pkt, out var er) && er is not null)
                        ext.Add(er);
                    break;
                case TypeWrappedNcom:
                    // Navigation belongs in the NCOM stream — skip here.
                    Count(skipped, type);
                    break;
                default:
                    Count(skipped, type);
                    break;
            }

            i += total;
        }

        return new RcomDecodeResult(ext, skipped, packets);
    }

    public static RcomDecodeResult DecodeFramedPackets(IEnumerable<byte[]> payloads)
    {
        // Concatenate framed payloads (each starts with 0x57) then decode.
        var total = payloads.Sum(p => p.Length);
        var buf = new byte[total];
        var o = 0;
        foreach (var p in payloads)
        {
            Buffer.BlockCopy(p, 0, buf, o, p.Length);
            o += p.Length;
        }

        return DecodeAll(buf);
    }

    private static void Count(Dictionary<byte, int> map, byte type)
    {
        map.TryGetValue(type, out var n);
        map[type] = n + 1;
    }

    private static bool TryParseExtended(ReadOnlySpan<byte> pkt, out RcomExtendedRange? result)
    {
        result = null;
        // Minimum: header(4) + time(2) + targets(2) + lat(4) + lon(4) + rates(4) = 20 bytes total? 
        // Table 16: bytes 0..19 at least for core ranges. data section includes checksum at end.
        if (pkt.Length < 20)
            return false;

        var timeIntoMinMs = BinaryPrimitives.ReadUInt16LittleEndian(pkt.Slice(4, 2));
        var targetNumber = pkt[6];
        var totalTargets = pkt[7];

        double? latRange = ReadInvalidableInt32(pkt, 8, 0x80000000, 0.001);
        double? longRange = ReadInvalidableInt32(pkt, 12, 0x80000000, 0.001);
        double? latRate = null;
        double? longRate = null;
        if (pkt.Length >= 20)
        {
            latRate = ReadInvalidableInt16(pkt, 16, unchecked((short)0x8000), 0.01);
            longRate = ReadInvalidableInt16(pkt, 18, unchecked((short)0x8000), 0.01);
        }

        double? hunterHdg = null;
        double? targetHdg = null;
        byte rangeStatus = 0;
        if (pkt.Length >= 41)
        {
            var hh = BinaryPrimitives.ReadUInt16LittleEndian(pkt.Slice(36, 2));
            var th = BinaryPrimitives.ReadUInt16LittleEndian(pkt.Slice(38, 2));
            if (hh != 0xFFFF) hunterHdg = hh * 0.01;
            if (th != 0xFFFF) targetHdg = th * 0.01;
            rangeStatus = pkt[40];
        }

        double? hunterFwdVel = null;
        double? hunterLatVel = null;
        double? latRangeAcc = null;
        double? longRangeAcc = null;
        if (pkt.Length >= 58)
        {
            hunterFwdVel = BinaryPrimitives.ReadInt16LittleEndian(pkt.Slice(50, 2)) * 0.01;
            hunterLatVel = ReadInvalidableInt16(pkt, 52, unchecked((short)0x8000), 0.01);
            latRangeAcc = ReadInvalidableInt16(pkt, 54, unchecked((short)0x8000), 0.01);
            longRangeAcc = ReadInvalidableInt16(pkt, 56, unchecked((short)0x8000), 0.01);
        }

        result = new RcomExtendedRange
        {
            TimeIntoMinuteMs = timeIntoMinMs == 0xFFFF ? null : timeIntoMinMs,
            TargetNumber = targetNumber,
            TotalTargets = totalTargets,
            LateralRangeM = latRange,
            LongitudinalRangeM = longRange,
            LateralRangeRateMps = latRate,
            LongitudinalRangeRateMps = longRate,
            HunterHeadingDeg = hunterHdg,
            TargetHeadingDeg = targetHdg,
            RangeStatus = rangeStatus,
            HunterForwardVelocityMps = hunterFwdVel,
            HunterLateralVelocityMps = hunterLatVel,
            LateralRangeAccelMps2 = latRangeAcc,
            LongitudinalRangeAccelMps2 = longRangeAcc
        };
        return true;
    }

    private static double? ReadInvalidableInt32(ReadOnlySpan<byte> pkt, int offset, uint invalid, double scale)
    {
        if (pkt.Length < offset + 4)
            return null;
        var v = BinaryPrimitives.ReadInt32LittleEndian(pkt.Slice(offset, 4));
        if (unchecked((uint)v) == invalid)
            return null;
        return v * scale;
    }

    private static double? ReadInvalidableInt16(ReadOnlySpan<byte> pkt, int offset, short invalid, double scale)
    {
        if (pkt.Length < offset + 2)
            return null;
        var v = BinaryPrimitives.ReadInt16LittleEndian(pkt.Slice(offset, 2));
        if (v == invalid)
            return null;
        return v * scale;
    }
}

public sealed class RcomDecodeResult
{
    public RcomDecodeResult(
        IReadOnlyList<RcomExtendedRange> extendedRanges,
        IReadOnlyDictionary<byte, int> skippedTypeCounts,
        int totalPackets)
    {
        ExtendedRanges = extendedRanges;
        SkippedTypeCounts = skippedTypeCounts;
        TotalPackets = totalPackets;
    }

    public IReadOnlyList<RcomExtendedRange> ExtendedRanges { get; }
    public IReadOnlyDictionary<byte, int> SkippedTypeCounts { get; }
    public int TotalPackets { get; }
}

public sealed class RcomExtendedRange
{
    public ushort? TimeIntoMinuteMs { get; init; }
    public byte TargetNumber { get; init; }
    public byte TotalTargets { get; init; }
    public double? LateralRangeM { get; init; }
    public double? LongitudinalRangeM { get; init; }
    public double? LateralRangeRateMps { get; init; }
    public double? LongitudinalRangeRateMps { get; init; }
    public double? HunterHeadingDeg { get; init; }
    public double? TargetHeadingDeg { get; init; }
    public byte RangeStatus { get; init; }
    public double? HunterForwardVelocityMps { get; init; }
    public double? HunterLateralVelocityMps { get; init; }
    public double? LateralRangeAccelMps2 { get; init; }
    public double? LongitudinalRangeAccelMps2 { get; init; }
}
