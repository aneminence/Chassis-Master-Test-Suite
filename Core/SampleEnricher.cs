namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// 为样本补齐合成通道：测试经过时间（秒）、行驶距离（米）。
/// </summary>
public static class SampleEnricher
{
    private const double EarthRadiusMeters = 6_371_000.0;

    /// <summary>
    /// 就地按时间戳顺序写入 elapsed / distance 到 Channels（返回新样本列表）。
    /// </summary>
    public static List<VehicleSample> EnrichAll(
        IReadOnlyList<VehicleSample> samples)
    {
        var result = new List<VehicleSample>(samples.Count);
        if (samples.Count == 0)
            return result;

        var originTs = samples[0].Timestamp;
        double distanceM = 0;
        VehicleSample? prev = null;

        foreach (var sample in samples)
        {
            if (prev is not null)
            {
                distanceM += HaversineMeters(
                    prev.Latitude,
                    prev.Longitude,
                    sample.Latitude,
                    sample.Longitude);
            }

            var elapsedS = Math.Max(0, (sample.Timestamp - originTs) / 1000.0);
            result.Add(WithSynthetic(sample, elapsedS, distanceM));
            prev = sample;
        }

        return result;
    }

    /// <summary>
    /// 实时流：基于会话起点与累计距离补齐一帧。
    /// </summary>
    public static VehicleSample EnrichLive(
        VehicleSample sample,
        long sessionOriginTimestampMs,
        ref double cumulativeDistanceM,
        VehicleSample? previous)
    {
        if (previous is not null)
        {
            cumulativeDistanceM += HaversineMeters(
                previous.Latitude,
                previous.Longitude,
                sample.Latitude,
                sample.Longitude);
        }

        var elapsedS = Math.Max(
            0,
            (sample.Timestamp - sessionOriginTimestampMs) / 1000.0);

        return WithSynthetic(sample, elapsedS, cumulativeDistanceM);
    }

    public static VehicleSample WithSynthetic(
        VehicleSample sample,
        double elapsedSeconds,
        double distanceMeters)
    {
        var channels = new Dictionary<string, double>(
            sample.Channels,
            StringComparer.OrdinalIgnoreCase)
        {
            [ChannelIds.ElapsedTest] = elapsedSeconds,
            [ChannelIds.DistanceTraveled] = distanceMeters
        };

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

    public static double HaversineMeters(
        double lat1Deg,
        double lon1Deg,
        double lat2Deg,
        double lon2Deg)
    {
        if (lat1Deg == 0 && lon1Deg == 0 && lat2Deg == 0 && lon2Deg == 0)
            return 0;

        var lat1 = lat1Deg * Math.PI / 180.0;
        var lat2 = lat2Deg * Math.PI / 180.0;
        var dLat = (lat2Deg - lat1Deg) * Math.PI / 180.0;
        var dLon = (lon2Deg - lon1Deg) * Math.PI / 180.0;

        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(lat1) * Math.Cos(lat2) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        var meters = EarthRadiusMeters * c;

        // 异常跳点（GPS 毛刺）忽略，避免距离暴涨
        return meters > 500 ? 0 : meters;
    }
}
