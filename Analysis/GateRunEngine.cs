using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// Gate-to-gate runs: cross Start → End. Optional Pass conditions evaluate
/// channel value at the moment the path crosses each At-gate within the run.
/// </summary>
public static class GateRunEngine
{
    public static IReadOnlyList<TestRunResult> Compute(
        IReadOnlyList<VehicleSample> samples,
        GateDefinition startGate,
        GateDefinition endGate,
        double? minSpeedKph = null,
        double? maxSpeedKph = null) =>
        Compute(samples, startGate, endGate, passConditions: null, resolveGate: null, minSpeedKph, maxSpeedKph);

    public static IReadOnlyList<TestRunResult> Compute(
        IReadOnlyList<VehicleSample> samples,
        GateDefinition startGate,
        GateDefinition endGate,
        IReadOnlyList<GatePassCondition>? passConditions,
        Func<Guid, GateDefinition?>? resolveGate,
        double? minSpeedKph = null,
        double? maxSpeedKph = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(startGate);
        ArgumentNullException.ThrowIfNull(endGate);

        if (samples.Count < 2 || !startGate.IsValid || !endGate.IsValid)
            return Array.Empty<TestRunResult>();

        var results = new List<TestRunResult>();
        var state = 0; // 0 idle, 1 armed, 2 cooldown
        var startIndex = -1;
        var startTs = 0L;
        var startSpeed = 0.0;

        for (var i = 1; i < samples.Count; i++)
        {
            var prev = samples[i - 1];
            var curr = samples[i];

            if (state == 2)
                state = 0;

            if (state == 0)
            {
                if (!GateCrossing.TryCrossSamples(startGate, prev, curr, out var tStart))
                    continue;
                if (!SpeedOk(curr.SpeedKph, minSpeedKph, maxSpeedKph) &&
                    !SpeedOk(prev.SpeedKph, minSpeedKph, maxSpeedKph))
                    continue;

                startIndex = i;
                startTs = InterpolateTs(prev.Timestamp, curr.Timestamp, tStart);
                startSpeed = Lerp(prev.SpeedKph, curr.SpeedKph, tStart);
                state = 1;
                continue;
            }

            if (state == 1)
            {
                if (!GateCrossing.TryCrossSamples(endGate, prev, curr, out var tEnd))
                    continue;

                var endTs = InterpolateTs(prev.Timestamp, curr.Timestamp, tEnd);
                if (endTs <= startTs)
                    continue;

                var endIndex = i;
                var endSpeed = Lerp(prev.SpeedKph, curr.SpeedKph, tEnd);
                var durationS = (endTs - startTs) / 1000.0;
                var (dist, method) = EstimateDistance(samples, startIndex, endIndex);

                var (pass, failReason, measurements) = EvaluatePass(
                    samples, startIndex, endIndex, passConditions, resolveGate);

                results.Add(new TestRunResult
                {
                    RunNumber = results.Count + 1,
                    StartTimestampMs = startTs,
                    EndTimestampMs = endTs,
                    DurationSeconds = durationS,
                    StartSpeedKph = startSpeed,
                    EndSpeedKph = endSpeed,
                    DeltaSpeedKph = endSpeed - startSpeed,
                    DistanceMeters = dist,
                    DistanceMethod = method,
                    Pass = pass,
                    FailReason = failReason,
                    PassMeasurements = measurements,
                    StartSampleIndex = startIndex,
                    EndSampleIndex = endIndex
                });

                state = 2;
                startIndex = -1;
            }
        }

        return results;
    }

    private static (bool Pass, string FailReason, IReadOnlyList<PassConditionMeasurement> Measurements) EvaluatePass(
        IReadOnlyList<VehicleSample> samples,
        int startIndex,
        int endIndex,
        IReadOnlyList<GatePassCondition>? passConditions,
        Func<Guid, GateDefinition?>? resolveGate)
    {
        if (passConditions is null || passConditions.Count == 0)
            return (true, "", Array.Empty<PassConditionMeasurement>());

        resolveGate ??= _ => null;
        var fails = new List<string>();
        var measurements = new List<PassConditionMeasurement>(passConditions.Count);

        foreach (var cond in passConditions)
        {
            var gate = resolveGate(cond.AtGateId);
            var gateName = gate?.Name ?? "?";

            if (gate is null || !gate.IsValid)
            {
                fails.Add("Pass At-gate missing");
                measurements.Add(new PassConditionMeasurement
                {
                    ChannelId = cond.ChannelId,
                    GateName = gateName,
                    AtGateId = cond.AtGateId,
                    Value = null,
                    Min = cond.Min,
                    Max = cond.Max,
                    InRange = false
                });
                continue;
            }

            if (!TrySampleChannelAtGateCrossing(
                    samples, startIndex, endIndex, gate, cond.ChannelId,
                    out var value, out var crossed))
            {
                fails.Add(crossed
                    ? $"{cond.ChannelId}@{gate.Name}: invalid value"
                    : $"did not cross {gate.Name}");
                measurements.Add(new PassConditionMeasurement
                {
                    ChannelId = cond.ChannelId,
                    GateName = gate.Name,
                    AtGateId = cond.AtGateId,
                    Value = null,
                    Min = cond.Min,
                    Max = cond.Max,
                    InRange = false
                });
                continue;
            }

            var inRange = value >= cond.Min && value <= cond.Max;
            measurements.Add(new PassConditionMeasurement
            {
                ChannelId = cond.ChannelId,
                GateName = gate.Name,
                AtGateId = cond.AtGateId,
                Value = value,
                Min = cond.Min,
                Max = cond.Max,
                InRange = inRange
            });

            if (!inRange)
            {
                fails.Add(
                    $"{cond.ChannelId}@{gate.Name}={value:0.##} not in [{cond.Min:0.##},{cond.Max:0.##}]");
            }
        }

        return fails.Count == 0
            ? (true, "", measurements)
            : (false, string.Join("; ", fails), measurements);
    }

    /// <summary>
    /// Find first crossing of gate within [startIndex-1 .. endIndex] and lerp channel.
    /// </summary>
    public static bool TrySampleChannelAtGateCrossing(
        IReadOnlyList<VehicleSample> samples,
        int startIndex,
        int endIndex,
        GateDefinition gate,
        string channelId,
        out double value,
        out bool crossed)
    {
        value = 0;
        crossed = false;
        if (samples.Count < 2 || !gate.IsValid)
            return false;

        var i0 = Math.Max(1, startIndex);
        var i1 = Math.Min(samples.Count - 1, Math.Max(endIndex, startIndex));
        // Allow a small look-back so Start-gate itself can be an At-gate.
        var from = Math.Max(1, i0 - 1);

        for (var i = from; i <= i1; i++)
        {
            var prev = samples[i - 1];
            var curr = samples[i];
            if (!GateCrossing.TryCrossSamples(gate, prev, curr, out var t))
                continue;

            crossed = true;
            var a = prev.GetChannel(channelId);
            var b = curr.GetChannel(channelId);
            value = Lerp(a, b, t);
            if (double.IsNaN(value) || double.IsInfinity(value))
                return false;
            return true;
        }

        return false;
    }

    private static bool SpeedOk(double speed, double? min, double? max)
    {
        if (min is double mn && speed < mn)
            return false;
        if (max is double mx && speed > mx)
            return false;
        return true;
    }

    private static long InterpolateTs(long a, long b, double t) =>
        a + (long)Math.Round((b - a) * Math.Clamp(t, 0, 1));

    private static double Lerp(double a, double b, double t) =>
        a + (b - a) * Math.Clamp(t, 0, 1);

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
            integrated += (a.SpeedKph + b.SpeedKph) * 0.5 / 3.6 * dtS;
        }

        if (integrated > 0.01)
            return (integrated, "SpeedIntegral");

        var s = samples[startIndex];
        var e = samples[endIndex];
        var hav = SampleEnricher.HaversineMeters(
            s.Latitude, s.Longitude, e.Latitude, e.Longitude);
        return hav > 0.01 ? (hav, "HaversineEndpoints") : (0, "None");
    }
}
