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

        var minutesHold = new long?[n];
        long? lastMinutes = null;
        for (var i = 0; i < n; i++)
        {
            var m = measurements[i];
            if (TryReadGpsMinutes(m, out var mins))
                lastMinutes = mins;
            minutesHold[i] = lastMinutes;
        }

        // Backfill leading packets before the first channel-0 minute update.
        long? first = null;
        for (var i = 0; i < n; i++)
        {
            if (minutesHold[i] is long v)
            {
                first = v;
                break;
            }
        }

        if (first is long seed)
        {
            for (var i = 0; i < n && minutesHold[i] is null; i++)
                minutesHold[i] = seed;

            // Walk backward adjusting for minute wraps in TimeMsIntoMinute.
            var firstIdx = 0;
            while (firstIdx < n && minutesHold[firstIdx] is null)
                firstIdx++;
            for (var i = firstIdx - 1; i >= 0; i--)
            {
                var newerMs = measurements[i + 1].TimeMsIntoMinute;
                var olderMs = measurements[i].TimeMsIntoMinute;
                var mins = minutesHold[i + 1]!.Value;
                // Going backward: if older ms > newer ms by a large gap, we crossed a minute boundary forward in time,
                // so the older packet is in the previous GPS minute.
                if (olderMs > newerMs && olderMs - newerMs > 30_000)
                    mins -= 1;
                minutesHold[i] = mins;
            }
        }

        ushort? prevMs = null;
        long? liveMinutes = null;
        for (var i = 0; i < n; i++)
        {
            var m = measurements[i];

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

                if (TryReadGpsMinutes(m, out var minsFromPrefixPkt))
                    liveMinutes = minsFromPrefixPkt;
                else if (minutesHold[i] is long held)
                    liveMinutes = held;

                prevMs = m.TimeMsIntoMinute <= 59_999 ? m.TimeMsIntoMinute : prevMs;
                continue;
            }

            if (TryReadGpsMinutes(m, out var minsNow))
                liveMinutes = minsNow;
            else if (minutesHold[i] is long heldMins)
                liveMinutes = heldMins;

            var ms = m.TimeMsIntoMinute;
            if (ms > 59_999)
            {
                // Invalid time field — keep previous stamp or fall back to sequence estimate.
                result[i] = i > 0 ? result[i - 1] : 0;
                continue;
            }

            if (liveMinutes is long mins)
            {
                if (prevMs is ushort p && ms < p && (p - ms) > 30_000)
                {
                    mins += 1;
                    liveMinutes = mins;
                }

                result[i] = GpsMinutesAndMsToUnixMs(mins, ms);
                prevMs = ms;
                continue;
            }

            // No GPS minutes and no XCOM prefix: 100 Hz synthetic relative timeline.
            result[i] = i * 10L;
            prevMs = ms;
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
