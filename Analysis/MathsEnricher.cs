using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// 将数学通道写入样本 Channels（不可变：返回新样本列表）。
/// </summary>
public static class MathsEnricher
{
    public static IReadOnlyList<VehicleSample> Apply(
        IReadOnlyList<VehicleSample> samples,
        IReadOnlyList<MathsChannelDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(definitions);

        if (definitions.Count == 0 || samples.Count == 0)
            return samples;

        var result = new List<VehicleSample>(samples.Count);
        foreach (var sample in samples)
            result.Add(ApplyOne(sample, definitions));
        return result;
    }

    public static VehicleSample ApplyOne(
        VehicleSample sample,
        IReadOnlyList<MathsChannelDefinition> definitions)
    {
        if (definitions.Count == 0)
            return sample;

        var channels = new Dictionary<string, double>(
            sample.Channels,
            StringComparer.OrdinalIgnoreCase);

        foreach (var def in definitions)
        {
            double Resolve(string name)
            {
                if (channels.TryGetValue(name, out var v))
                    return v;
                return sample.GetChannel(name);
            }

            if (MathsExpression.TryEvaluate(def.Expression, Resolve, out var value, out _))
                channels[def.Id] = value;
            else
                channels[def.Id] = 0;
        }

        return new VehicleSample
        {
            Timestamp = sample.Timestamp,
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
            Channels = channels
        };
    }
}
