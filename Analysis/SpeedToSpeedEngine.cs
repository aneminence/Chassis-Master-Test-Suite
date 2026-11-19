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
/// Falling（Decel / 下降 Custom）额外对齐 VBOX Test Suite：
/// - 提交型起点：向下穿越 Start 后，若在到达终点前速度再次升回 ≥ Start，则放弃该起点，等待下一次向下穿越。
/// - 零速地板：配置的终点 &lt; 0.8 km/h 时，按 0.8 km/h 切段（VBOX 界面写 80→0，实际停在约 0.8）。
///   结果仍报告配置阈值（如 80 和 0），Duration / Distance 取有效窗口。
///
/// Rising（Accel / 上升 Custom）额外对齐 VBOX Test Suite：
/// - 零速地板：配置的起点 &lt; 0.5 km/h 时，按 0.5 km/h 起算（VBOX 界面写 0→50，实际从约 0.5 离开且不再掉回）。
/// - 提交型起点：向上离开有效起点后，若在到达终点前速度再次落到 ≤ 有效起点，则放弃该起点，等待下一次向上离开。
///   结果仍报告配置阈值（如 0 和 50），Duration / Distance 取有效窗口。跑完后回到有效起点以下再布防。
///
/// 距离估算优先级（写在结果 DistanceMethod）：
/// 1) 插值梯形速度积分（精确起终点时刻 + 中间整样本，km/h → m/s × Δt）
/// 2) DistanceTraveled 通道差分（SampleEnricher Haversine 累计）
/// 3) 起终点 Haversine（仅当 GPS 非零）
/// </summary>
public static class SpeedToSpeedEngine
{
    /// <summary>
    /// VBOX 在界面终点为 0 时实际采用的结束速度（km/h）。
    /// </summary>
    public const double VboxZeroEndFloorKph = 0.8;

    /// <summary>
    /// VBOX 在界面起点为 0 时实际采用的起步速度（km/h）。
    /// </summary>
    public const double VboxZeroStartFloorKph = 0.5;

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
        var isFalling = startCond.Direction == CrossingDirection.Falling
                        && endCond.Direction == CrossingDirection.Falling;
        var isRising = startCond.Direction == CrossingDirection.Rising
                       && endCond.Direction == CrossingDirection.Rising;

        var effectiveStartCond = startCond;
        if (isRising && startCond.Threshold < VboxZeroStartFloorKph)
        {
            effectiveStartCond = new ThresholdCondition
            {
                ChannelId = startCond.ChannelId,
                Threshold = VboxZeroStartFloorKph,
                Direction = startCond.Direction
            };
        }

        var effectiveEndCond = endCond;
        if (isFalling && endCond.Threshold < VboxZeroEndFloorKph)
        {
            effectiveEndCond = new ThresholdCondition
            {
                ChannelId = endCond.ChannelId,
                Threshold = VboxZeroEndFloorKph,
                Direction = endCond.Direction
            };
        }

        // 0 = idle (找起点), 1 = armed (找终点), 2 = cooldown
        var state = 0;
        var startIndex = -1;
        var startTs = 0L;

        for (var i = 1; i < samples.Count; i++)
        {
            var prev = samples[i - 1];
            var curr = samples[i];

            if (state == 2)
            {
                // 回到起点带即可再布防。
                // 若 start 足够大，要求回到 start-hyst 以下以抑制抖动；start≈0 时不要求负速度。
                var v = curr.GetChannel(startCond.ChannelId);
                // Rising 再布防看有效起点（0→50 时为 0.5，而不是精确 0）。
                // 迟滞只按配置起点扣减，避免 0.5 地板被扣回 0。
                var quiet = startCond.Direction == CrossingDirection.Rising
                    ? effectiveStartCond.Threshold - Math.Min(hyst, Math.Max(0, startCond.Threshold))
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
                if (!TryCross(prev, curr, effectiveStartCond, isStart: true, out var crossTs, out _))
                    continue;

                startIndex = i;
                startTs = crossTs;
                state = 1;
                continue;
            }

            // Falling：到达终点前若速度再次升回起点阈值，放弃本次起点。
            // Rising：到达终点前若速度再次落到有效起点及以下，放弃本次起点。
            if (isFalling || isRising)
            {
                var startThr = effectiveStartCond.Threshold;
                var a = prev.GetChannel(startCond.ChannelId);
                var b = curr.GetChannel(startCond.ChannelId);
                var recrossed = isFalling
                    ? a < startThr && b >= startThr
                    : a > startThr && b <= startThr;
                if (recrossed)
                {
                    state = 0;
                    startIndex = -1;
                    startTs = 0;
                    continue;
                }
            }

            if (!TryCross(prev, curr, effectiveEndCond, isStart: false, out var endTs, out _))
                continue;

            if (endTs <= startTs)
                continue;

            var endIndex = i;
            var durationS = (endTs - startTs) / 1000.0;
            var (distanceM, method) = EstimateDistance(
                samples,
                startIndex,
                endIndex,
                startTs,
                endTs,
                effectiveStartCond.Threshold,
                effectiveEndCond.Threshold,
                startCond.ChannelId);

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
                StartSpeedKph = startCond.Threshold,
                EndSpeedKph = endCond.Threshold,
                DeltaSpeedKph = endCond.Threshold - startCond.Threshold,
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
        int endIndex,
        long startTs,
        long endTs,
        double startSpeedKph,
        double endSpeedKph,
        string speedChannelId)
    {
        var integrated = IntegrateSegment(
            startSpeedKph,
            samples[startIndex].GetChannel(speedChannelId),
            startTs,
            samples[startIndex].Timestamp);

        for (var i = startIndex; i < endIndex - 1; i++)
        {
            var a = samples[i];
            var b = samples[i + 1];
            integrated += IntegrateSegment(
                a.GetChannel(speedChannelId),
                b.GetChannel(speedChannelId),
                a.Timestamp,
                b.Timestamp);
        }

        integrated += IntegrateSegment(
            samples[endIndex - 1].GetChannel(speedChannelId),
            endSpeedKph,
            samples[endIndex - 1].Timestamp,
            endTs);

        if (integrated > 0.01)
            return (integrated, "SpeedIntegral");

        var d0 = samples[startIndex].GetChannel(ChannelIds.DistanceTraveled);
        var d1 = samples[endIndex].GetChannel(ChannelIds.DistanceTraveled);
        if (d1 > d0 && d1 - d0 < 1_000_000)
            return (d1 - d0, "DistanceTraveled");

        var s = samples[startIndex];
        var e = samples[endIndex];
        var hav = SampleEnricher.HaversineMeters(
            s.Latitude, s.Longitude, e.Latitude, e.Longitude);
        if (hav > 0.01)
            return (hav, "HaversineEndpoints");

        return (0, "None");
    }

    private static double IntegrateSegment(double speed0Kph, double speed1Kph, long t0Ms, long t1Ms)
    {
        var dtS = (t1Ms - t0Ms) / 1000.0;
        if (dtS <= 0 || dtS > 5)
            return 0;

        return (speed0Kph + speed1Kph) * 0.5 / 3.6 * dtS;
    }
}
