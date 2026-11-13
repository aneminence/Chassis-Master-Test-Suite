using System.Buffers.Binary;

namespace Chassis_Master_Test_Suite.Recorder.OxTS;

/// <summary>
/// Builds Unix-ms timestamps for NCOM packets.
/// Prefer XCOM packet time prefix (GPS ns); else Batch A ms-into-minute + status channel 0 GPS minutes
/// (NCOM Manual / NCOMdecoder). GPS epoch 1980-01-06, no leap-second adjustment (matches XCOM manual).
/// </summary>
public static class NcomTime
{
    /// <summary>1980-01-06 00:00:00 UTC as Unix milliseconds.</summary>
    public static readonly long GpsEpochUnixMs =
        new DateTimeOffset(1980, 1, 6, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>Channel 0 minutes invalid when below this (NCOM Manual).</summary>
    public const uint MinValidGpsMinutes = 1000;

    public static long GpsNsToUnixMs(long gpsTimeNs) =>
        GpsEpochUnixMs + (gpsTimeNs / 1_000_000L);

    public static long GpsMinutesAndMsToUnixMs(long gpsMinutes, int msIntoMinute) =>
        GpsEpochUnixMs + (gpsMinutes * 60_000L) + msIntoMinute;

    /// <summary>
    /// Assigns one Unix-ms timestamp per measurement (same order/length as input).
    /// </summary>
    public static long[] AssignUnixTimestamps(IReadOnlyList<NcomMeasurement> measurements)
    {
        var n = measurements.Count;
        var result = new long[n];
        if (n == 0)
            return result;

        // Find the first absolute minute anchor before backfilling the leading
        // packets. Keeping its index is essential when the file begins just
        // before a minute rollover.
        long? liveMinutes = null;
        var firstAnchorIndex = -1;
        for (var i = 0; i < n; i++)
        {
            if (TryReadGpsMinutes(measurements[i], out var minutes))
            {
                liveMinutes = minutes;
                firstAnchorIndex = i;
                break;
            }
        }

        if (firstAnchorIndex >= 0)
        {
            ushort? newerMs = null;
            for (var i = firstAnchorIndex; i >= 0; i--)
            {
                var olderMs = measurements[i].TimeMsIntoMinute;
                if (olderMs > 59_999) continue;
                if (newerMs is ushort next && olderMs - next > 30_000)
                    liveMinutes -= 1;
                newerMs = olderMs;
            }
        }

        ushort? prevMs = null;
        for (var i = 0; i < n; i++)
        {
            var m = measurements[i];
            var ms = m.TimeMsIntoMinute;
            if (TryReadGpsMinutes(m, out var minsNow))
            {
                // Channel 0 already specifies this packet's minute, including
                // when its update is carried by the first packet after rollover.
                liveMinutes = minsNow;
            }
            else if (ms <= 59_999 && prevMs is ushort previous && previous - ms > 30_000
                     && liveMinutes.HasValue)
            {
                // Carry the inferred minute until the next explicit update;
                // reapplying the last channel-0 value here moved time back 60 s.
                liveMinutes += 1;
            }

            // Prefixes own their output timestamps, but their valid Batch A time
            // still advances the fallback minute state for unprefixed packets.
            if (ms <= 59_999) prevMs = ms;

            if (m.TimePrefixNs is long prefixNs && prefixNs > 0)
            {
                if (prefixNs <= XcomContainerReader.GpsKnownGranuleThresholdNs)
                {
                    // Free-running / unknown GPS — relative ns from 0.
                    result[i] = prefixNs / 1_000_000L;
                }
                else
                {
                    result[i] = GpsNsToUnixMs(prefixNs);
                }

                continue;
            }

            if (ms > 59_999)
            {
                // Invalid time field — keep previous stamp or fall back to sequence estimate.
                result[i] = i > 0 ? result[i - 1] : 0;
                continue;
            }

            if (liveMinutes is long mins)
            {
                result[i] = GpsMinutesAndMsToUnixMs(mins, ms);
                continue;
            }

            // No GPS minutes and no XCOM prefix: 100 Hz synthetic relative timeline.
            result[i] = i * 10L;
        }

        return result;
    }

    public static bool TryReadGpsMinutes(NcomMeasurement m, out uint minutes)
    {
        minutes = 0;
        if (m.StatusChannel != 0 || m.StatusData.Length < 4)
            return false;

        minutes = BinaryPrimitives.ReadUInt32LittleEndian(m.StatusData.AsSpan(0, 4));
        return minutes >= MinValidGpsMinutes;
    }
}
