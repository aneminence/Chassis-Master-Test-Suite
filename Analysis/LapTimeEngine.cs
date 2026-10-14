using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>Detects complete laps and ordered sectors from gate crossings.</summary>
public static class LapTimeEngine
{
    private const double MinimumLapSeconds = 5.0;

    public static IReadOnlyList<TestRunResult> Compute(
        IReadOnlyList<VehicleSample> samples,
        GateDefinition startFinish,
        IReadOnlyList<GateDefinition> sectorGates)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(startFinish);
        ArgumentNullException.ThrowIfNull(sectorGates);
        if (samples.Count < 2 || !startFinish.IsValid || sectorGates.Any(g => !g.IsValid))
            return Array.Empty<TestRunResult>();

        var finishCrossings = FindCrossings(samples, startFinish);
        var results = new List<TestRunResult>();
        for (var lapIndex = 1; lapIndex < finishCrossings.Count; lapIndex++)
        {
            var start = finishCrossings[lapIndex - 1];
            var end = finishCrossings[lapIndex];
            var lapSeconds = (end.TimestampMs - start.TimestampMs) / 1000.0;
            if (lapSeconds < MinimumLapSeconds)
                continue;

            var sectors = new List<SectorTimeResult>(sectorGates.Count + 1);
            var previousTs = start.TimestampMs;
            var searchIndex = Math.Max(1, start.SampleIndex);
            var complete = true;
            foreach (var gate in sectorGates)
            {
                if (!TryFindNextCrossing(samples, gate, searchIndex, end.SampleIndex,
                        previousTs, out var crossing))
                {
                    complete = false;
                    break;
                }

                sectors.Add(new SectorTimeResult
                {
                    Name = gate.Name,
                    EndGateId = gate.Id,
                    DurationSeconds = (crossing.TimestampMs - previousTs) / 1000.0
                });
                previousTs = crossing.TimestampMs;
                searchIndex = crossing.SampleIndex + 1;
            }
            if (!complete)
                continue;

            sectors.Add(new SectorTimeResult
            {
                Name = startFinish.Name,
                EndGateId = startFinish.Id,
                DurationSeconds = (end.TimestampMs - previousTs) / 1000.0
            });

            var i0 = Math.Clamp(start.SampleIndex, 0, samples.Count - 1);
            var i1 = Math.Clamp(end.SampleIndex, i0, samples.Count - 1);
            var startSpeed = Interpolate(samples[i0 - 1], samples[i0], start.Fraction, s => s.SpeedKph);
            var endSpeed = Interpolate(samples[i1 - 1], samples[i1], end.Fraction, s => s.SpeedKph);
            var d0 = samples[i0].GetChannel(ChannelIds.DistanceTraveled);
            var d1 = samples[i1].GetChannel(ChannelIds.DistanceTraveled);

            results.Add(new TestRunResult
            {
                RunNumber = results.Count + 1,
                StartTimestampMs = start.TimestampMs,
                EndTimestampMs = end.TimestampMs,
                DurationSeconds = lapSeconds,
                StartSpeedKph = startSpeed,
                EndSpeedKph = endSpeed,
                DeltaSpeedKph = endSpeed - startSpeed,
                DistanceMeters = d1 >= d0 ? d1 - d0 : 0,
                DistanceMethod = "DistanceTraveled",
                Pass = true,
                SectorTimes = sectors,
                StartSampleIndex = i0,
                EndSampleIndex = i1
            });
        }
        return results;
    }

    private static List<Crossing> FindCrossings(IReadOnlyList<VehicleSample> samples, GateDefinition gate)
    {
        var result = new List<Crossing>();
        for (var i = 1; i < samples.Count; i++)
        {
            if (!GateCrossing.TryCrossSamples(gate, samples[i - 1], samples[i], out var fraction))
                continue;
            var timestamp = InterpolateTimestamp(samples[i - 1].Timestamp, samples[i].Timestamp, fraction);
            if (result.Count == 0 || timestamp - result[^1].TimestampMs >= 1000)
                result.Add(new Crossing(i, timestamp, fraction));
        }
        return result;
    }

    private static bool TryFindNextCrossing(
        IReadOnlyList<VehicleSample> samples, GateDefinition gate, int fromIndex, int toIndex,
        long afterTimestamp, out Crossing crossing)
    {
        for (var i = Math.Max(1, fromIndex); i <= Math.Min(toIndex, samples.Count - 1); i++)
        {
            if (!GateCrossing.TryCrossSamples(gate, samples[i - 1], samples[i], out var fraction))
                continue;
            var timestamp = InterpolateTimestamp(samples[i - 1].Timestamp, samples[i].Timestamp, fraction);
            if (timestamp <= afterTimestamp)
                continue;
            crossing = new Crossing(i, timestamp, fraction);
            return true;
        }
        crossing = default;
        return false;
    }

    private static long InterpolateTimestamp(long a, long b, double fraction) =>
        a + (long)Math.Round((b - a) * Math.Clamp(fraction, 0, 1));

    private static double Interpolate(
        VehicleSample a, VehicleSample b, double fraction, Func<VehicleSample, double> selector) =>
        selector(a) + (selector(b) - selector(a)) * Math.Clamp(fraction, 0, 1);

    private readonly record struct Crossing(int SampleIndex, long TimestampMs, double Fraction);
}
