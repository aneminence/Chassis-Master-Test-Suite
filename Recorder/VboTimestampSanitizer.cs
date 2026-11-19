using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Recorder;

/// <summary>
/// VBO time is HHMMSS with no date, so VehicleSample.Timestamp after a raw
/// read is milliseconds since UTC midnight. A single invalid/zero time row
/// (NaN mapped to 0, or a written 000000.000) sits ~hours away from the
/// real cluster and stretches the Time axis into a long flat line.
/// </summary>
public static class VboTimestampSanitizer
{
    public static readonly TimeSpan DefaultMaxGap = TimeSpan.FromHours(2);
    public const int DefaultMaxOutlierCount = 10;
    public const double DefaultMaxOutlierFraction = 0.01;

    /// <summary>
    /// Drop small isolated timestamp groups that are more than
    /// <paramref name="maxGap"/> from the main recording cluster.
    /// A midnight wrap (23:59 → 00:00) is kept as one cluster.
    /// Two large sessions far apart are both kept.
    /// </summary>
    public static IReadOnlyList<VehicleSample> DropIsolatedOutliers(
        IReadOnlyList<VehicleSample> samples,
        TimeSpan? maxGap = null,
        int maxOutlierCount = DefaultMaxOutlierCount,
        double maxOutlierFraction = DefaultMaxOutlierFraction)
    {
        if (samples.Count < 3)
            return samples;

        var maxGapMs = (long)(maxGap ?? DefaultMaxGap).TotalMilliseconds;
        if (maxGapMs <= 0)
            return samples;

        var times = new long[samples.Count];
        var min = long.MaxValue;
        var max = long.MinValue;
        for (var i = 0; i < samples.Count; i++)
        {
            var t = samples[i].Timestamp;
            times[i] = t;
            if (t < min) min = t;
            if (t > max) max = t;
        }

        if (max - min <= maxGapMs)
            return samples;

        var allowWrap = min >= 0 && max < 2 * 24L * 60 * 60 * 1000;
        var order = Enumerable.Range(0, times.Length).OrderBy(i => times[i]).ToArray();

        var groups = new List<List<int>>();
        var current = new List<int> { order[0] };
        for (var i = 1; i < order.Length; i++)
        {
            var prev = times[order[i - 1]];
            var cur = times[order[i]];
            if (cur - prev <= maxGapMs)
            {
                current.Add(order[i]);
            }
            else
            {
                groups.Add(current);
                current = new List<int> { order[i] };
            }
        }
        groups.Add(current);

        if (allowWrap && groups.Count >= 2)
        {
            var dayMs = 24L * 60 * 60 * 1000;
            var firstGroup = groups[0];
            var lastGroup = groups[^1];
            var wrapGap = dayMs - (times[lastGroup[^1]] % dayMs) + (times[firstGroup[0]] % dayMs);
            if (wrapGap <= maxGapMs)
            {
                lastGroup.AddRange(firstGroup);
                groups.RemoveAt(0);
            }
        }

        if (groups.Count <= 1)
            return samples;

        var largest = groups.Max(g => g.Count);
        var dropThreshold = Math.Max(1, Math.Min(
            maxOutlierCount,
            (int)Math.Floor(samples.Count * maxOutlierFraction)));

        var drop = new HashSet<int>();
        foreach (var group in groups)
        {
            if (group.Count < largest && group.Count <= dropThreshold)
            {
                foreach (var index in group)
                    drop.Add(index);
            }
        }

        if (drop.Count == 0)
            return samples;

        var kept = new List<VehicleSample>(samples.Count - drop.Count);
        for (var i = 0; i < samples.Count; i++)
        {
            if (!drop.Contains(i))
                kept.Add(samples[i]);
        }

        return kept.Count >= 2 ? kept : samples;
    }

    /// <summary>
    /// Turn time-of-day timestamps into Unix milliseconds using the VBO
    /// "File created on" local wall time. The time column is UTC; the date
    /// is chosen so each sample is as close as possible to the file-created
    /// instant (covers midnight and time-zone day boundaries).
    /// </summary>
    public static IReadOnlyList<VehicleSample> ApplyFileCreatedDate(
        IReadOnlyList<VehicleSample> samples,
        DateTimeOffset fileCreated)
    {
        if (samples.Count == 0)
            return samples;

        var createdUtcMs = fileCreated.ToUnixTimeMilliseconds();
        var result = new VehicleSample[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            result[i] = WithTimestamp(sample, ToUnixMilliseconds(sample.Timestamp, createdUtcMs));
        }

        return result;
    }

    public static long ToUnixMilliseconds(long timeOfDayMs, long fileCreatedUtcMs)
    {
        var dayMs = 24L * 60 * 60 * 1000;
        // Guard against a timestamp that already looks like Unix time.
        if (timeOfDayMs < 0 || timeOfDayMs >= 2 * dayMs)
            return timeOfDayMs;

        var createdDayMs = FloorDiv(fileCreatedUtcMs, dayMs) * dayMs;
        var best = createdDayMs + timeOfDayMs;
        var bestDist = Abs(best - fileCreatedUtcMs);
        for (var day = -1; day <= 1; day++)
        {
            if (day == 0)
                continue;
            var candidate = createdDayMs + day * dayMs + timeOfDayMs;
            var dist = Abs(candidate - fileCreatedUtcMs);
            if (dist < bestDist)
            {
                best = candidate;
                bestDist = dist;
            }
        }

        return best;
    }

    private static VehicleSample WithTimestamp(VehicleSample sample, long timestamp) =>
        timestamp == sample.Timestamp
            ? sample
            : new VehicleSample
            {
                Timestamp = timestamp,
                Sequence = sample.Sequence,
                SpeedKph = sample.SpeedKph,
                LongitudinalAcceleration = sample.LongitudinalAcceleration,
                LateralAcceleration = sample.LateralAcceleration,
                VerticalAcceleration = sample.VerticalAcceleration,
                YawRate = sample.YawRate,
                Latitude = sample.Latitude,
                Longitude = sample.Longitude,
                Altitude = sample.Altitude,
                Heading = sample.Heading,
                VehicleId = sample.VehicleId,
                VehicleLabel = sample.VehicleLabel,
                Channels = sample.Channels
            };

    private static long FloorDiv(long value, long divisor)
    {
        var q = value / divisor;
        if (value < 0 && value % divisor != 0)
            q--;
        return q;
    }

    private static long Abs(long value) => value < 0 ? -value : value;
}
