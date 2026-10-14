namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// After <see cref="SampleEnricher"/> has written <see cref="ChannelIds.DistanceTraveled"/>,
/// compute a distance-windowed grade channel <see cref="ChannelIds.SlopePct"/> (%).
/// grade% = 100 * Δh / Δs over ~<see cref="DefaultWindowMeters"/> of horizontal distance.
/// <para>
/// Note: <c>KmlTrackImporter.ToVehicleSamples</c> currently coerces missing altitude to 0.0
/// (not null). This enricher therefore cannot distinguish "missing alt" from true sea-level;
/// it still computes. Callers that need skip-on-missing should preserve null upstream later.
/// </para>
/// </summary>
public static class SlopeEnricher
{
    public const double DefaultWindowMeters = 10.0;

    /// <summary>Minimum Δs (m) to accept a grade sample; below this → hold last good / NaN.</summary>
    public const double MinDeltaMeters = 0.05;

    /// <summary>If distance window cannot be met, look back this many samples.</summary>
    public const int FallbackSampleCount = 5;

    public static List<VehicleSample> EnrichAll(
        IReadOnlyList<VehicleSample> samples,
        double windowMeters = DefaultWindowMeters)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
            return new List<VehicleSample>();

        var distances = new double[samples.Count];
        var heights = new double[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            distances[i] = GetDistanceMeters(samples[i]);
            heights[i] = samples[i].Altitude;
        }

        var result = new List<VehicleSample>(samples.Count);
        double? lastGood = null;

        for (var i = 0; i < samples.Count; i++)
        {
            var computed = TryComputeGrade(
                distances,
                heights,
                i,
                windowMeters,
                out var grade);

            if (computed)
                lastGood = grade;
            else if (lastGood is double held)
                grade = held;
            else
                grade = double.NaN;

            result.Add(WithSlope(samples[i], grade));
        }

        return result;
    }

    /// <summary>
    /// Compute grade% at <paramref name="index"/>; returns false when Δs is unusable.
    /// </summary>
    internal static bool TryComputeGrade(
        IReadOnlyList<double> distances,
        IReadOnlyList<double> heights,
        int index,
        double windowMeters,
        out double gradePct)
    {
        gradePct = double.NaN;
        if (index <= 0 || index >= distances.Count)
            return false;

        var dI = distances[index];
        var hI = heights[index];

        // Prefer a look-back of ~windowMeters along cumulative distance.
        var j = index;
        for (var k = index - 1; k >= 0; k--)
        {
            if (dI - distances[k] >= windowMeters)
            {
                j = k;
                break;
            }

            j = k; // keep earliest as fallback span
        }

        var deltaS = dI - distances[j];
        if (deltaS < MinDeltaMeters)
        {
            // Sample-count fallback (e.g. dense GPS with tiny Haversine steps).
            var back = Math.Min(FallbackSampleCount, index);
            j = index - back;
            deltaS = dI - distances[j];
        }

        if (deltaS < MinDeltaMeters)
            return false;

        gradePct = 100.0 * (hI - heights[j]) / deltaS;
        return true;
    }

    private static double GetDistanceMeters(VehicleSample sample)
    {
        if (sample.Channels.TryGetValue(ChannelIds.DistanceTraveled, out var d))
            return d;
        return sample.GetChannel(ChannelIds.DistanceTraveled);
    }

    private static VehicleSample WithSlope(VehicleSample sample, double slopePct)
    {
        var channels = new Dictionary<string, double>(
            sample.Channels,
            StringComparer.OrdinalIgnoreCase)
        {
            [ChannelIds.SlopePct] = slopePct
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
}
