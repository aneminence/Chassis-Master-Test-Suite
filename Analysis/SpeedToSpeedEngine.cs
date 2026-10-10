using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// P0 速度起止切段引擎：按阈值穿越切出 Accel / Decel / Custom run。
///
/// 穿越约定（避免站立起步 start=0 漏检）：
/// - 起点 Rising：离开阈值向上（a ≤ thr 且 b &gt; thr）
/// - 终点 Rising：到达阈值向上（a &lt; thr 且 b ≥ thr）
/// - 起点 Falling：离开阈值向下（a ≥ thr 且 b &lt; thr）
/// - 终点 Falling：到达阈值向下（a &gt; thr 且 b ≤ thr）
///
/// 距离估算优先级（写在结果 DistanceMethod）：
/// 1) DistanceTraveled 通道差分（SampleEnricher Haversine 累计）
/// 2) 速度梯形积分（km/h → m/s × Δt）
/// 3) 起终点 Haversine（仅当 GPS 非零）
/// </summary>
public static class SpeedToSpeedEngine
{
    public static IReadOnlyList<TestRunResult> Compute(
        IReadOnlyList<VehicleSample> samples,
        TestDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(definition);

        if (samples.Count < 2)
            return Array.Empty<TestRunResult>();

        var results = new List<TestRunResult>();
        var startCond = definition.Start;
        var endCond = definition.End;
        var hyst = Math.Max(0, definition.Hysteresis);

        // 0 = idle (找起点), 1 = armed (找终点), 2 = cooldown
        var state = 0;
        var startIndex = -1;
        var startTs = 0L;
        var startValue = 0.0;

        for (var i = 1; i < samples.Count; i++)
        {
            var prev = samples[i - 1];
            var curr = samples[i];

            if (state == 2)
            {
                // 回到起点带即可再布防。
                // 若 start 足够大，要求回到 start-hyst 以下以抑制抖动；start≈0 时不要求负速度。
                var v = curr.GetChannel(startCond.ChannelId);
                var quiet = startCond.Direction == CrossingDirection.Rising
                    ? startCond.Threshold - Math.Min(hyst, Math.Max(0, startCond.Threshold))
                    : startCond.Threshold + Math.Min(hyst, Math.Abs(startCond.Threshold) + hyst);
                var ready = startCond.Direction == CrossingDirection.Rising
                    ? v <= quiet
                    : v >= quiet;

                if (ready)
                    state = 0;

                continue;
            }

            if (state == 0)
            {
                if (!TryCross(prev, curr, startCond, isStart: true, out var crossTs, out var crossVal))
                    continue;

                startIndex = i;
                startTs = crossTs;
                startValue = crossVal;
                state = 1;
                continue;
            }

            if (!TryCross(prev, curr, endCond, isStart: false, out var endTs, out var endVal))
                continue;

            if (endTs <= startTs)
                continue;

            var endIndex = i;
            var durationS = (endTs - startTs) / 1000.0;
            var (distanceM, method) = EstimateDistance(samples, startIndex, endIndex);

            var pass = true;
            var failReason = "";
            if (definition.MaxDurationSeconds is double max && durationS > max)
            {
                pass = false;
                failReason = $"Duration {durationS:0.###}s > max {max:0.###}s";
            }

            results.Add(new TestRunResult
            {
                RunNumber = results.Count + 1,
                StartTimestampMs = startTs,
                EndTimestampMs = endTs,
                DurationSeconds = durationS,
                StartSpeedKph = startValue,
                EndSpeedKph = endVal,
                DeltaSpeedKph = endVal - startValue,
                DistanceMeters = distanceM,
                DistanceMethod = method,
                Pass = pass,
                FailReason = failReason,
                StartSampleIndex = startIndex,
                EndSampleIndex = endIndex
            });

            state = 2;
            startIndex = -1;
        }

        return results;
    }

    private static bool TryCross(
        VehicleSample prev,
        VehicleSample curr,
        ThresholdCondition condition,
        bool isStart,
        out long crossTimestampMs,
        out double crossValue)
    {
        var a = prev.GetChannel(condition.ChannelId);
        var b = curr.GetChannel(condition.ChannelId);
        var thr = condition.Threshold;

        bool crossed = condition.Direction switch
        {
            CrossingDirection.Rising when isStart => a <= thr && b > thr,
            CrossingDirection.Rising => a < thr && b >= thr,
            CrossingDirection.Falling when isStart => a >= thr && b < thr,
            CrossingDirection.Falling => a > thr && b <= thr,
            _ => false
        };

        if (!crossed)
        {
            crossTimestampMs = 0;
            crossValue = 0;
            return false;
        }

        var denom = b - a;
        var t = Math.Abs(denom) < 1e-12 ? 1.0 : (thr - a) / denom;
        t = Math.Clamp(t, 0, 1);

        var dt = curr.Timestamp - prev.Timestamp;
        crossTimestampMs = prev.Timestamp + (long)Math.Round(dt * t);
        // 起点“离开”阈值时通道值略高于 thr；仍报告定义为 thr，便于 speed-to-speed 对照
        crossValue = thr;
        return true;
    }

    private static (double Meters, string Method) EstimateDistance(
        IReadOnlyList<VehicleSample> samples,
        int startIndex,
        int endIndex)
    {
        var d0 = samples[startIndex].GetChannel(ChannelIds.DistanceTraveled);
        var d1 = samples[endIndex].GetChannel(ChannelIds.DistanceTraveled);
        if (d1 > d0 && d1 - d0 < 1_000_000)
            return (d1 - d0, "DistanceTraveled");

        double integrated = 0;
        for (var i = startIndex; i < endIndex; i++)
        {
            var a = samples[i];
            var b = samples[i + 1];
            var dtS = (b.Timestamp - a.Timestamp) / 1000.0;
            if (dtS <= 0 || dtS > 5)
                continue;

            var vMps = (a.SpeedKph + b.SpeedKph) * 0.5 / 3.6;
            integrated += vMps * dtS;
        }

        if (integrated > 0.01)
            return (integrated, "SpeedIntegral");

        var s = samples[startIndex];
        var e = samples[endIndex];
        var hav = SampleEnricher.HaversineMeters(
            s.Latitude, s.Longitude, e.Latitude, e.Longitude);
        if (hav > 0.01)
            return (hav, "HaversineEndpoints");

        return (0, "None");
    }
}