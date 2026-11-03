using Chassis_Master_Test_Suite.Controls;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class TrackProjectionOutlierPerfTests
{
    [Fact]
    public void FilterGpsOutliers_LargeTrack_KeepsRoute_DropsTeleportSpike()
    {
        // Compact ~100 m loop so the 5 km median radius keeps the route;
        // only the Beijing teleport should be dropped. Count is large to
        // exercise the subsampled-median path (>8192).
        const int count = 100_000;
        var samples = new List<VehicleSample>(count + 1);
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)count;
            samples.Add(new VehicleSample
            {
                Sequence = i,
                Latitude = 31.2304 + 0.0004 * Math.Sin(t * Math.PI * 2),
                Longitude = 121.4737 + 0.0004 * Math.Cos(t * Math.PI * 2),
                Altitude = 10
            });
        }

        samples.Add(new VehicleSample
        {
            Sequence = count,
            Latitude = 40.0,
            Longitude = 116.0,
            Altitude = 10
        });

        var filtered = TrackProjection.FilterGpsOutliers(samples);
        Assert.True(filtered.Count >= count - 1);
        Assert.DoesNotContain(filtered, s => Math.Abs(s.Latitude - 40.0) < 1e-9);
    }

    [Fact]
    public void FilterGpsOutliers_SmallTrack_StillDropsFarSpike()
    {
        var samples = new List<VehicleSample>
        {
            Gps(31.00, 121.00),
            Gps(31.01, 121.01),
            Gps(31.02, 121.02),
            Gps(40.00, 116.00),
            Gps(31.03, 121.03),
        };

        var filtered = TrackProjection.FilterGpsOutliers(samples);
        Assert.Equal(4, filtered.Count);
        Assert.DoesNotContain(filtered, s => Math.Abs(s.Latitude - 40.0) < 1e-9);
    }

    private static VehicleSample Gps(double lat, double lon) => new()
    {
        Latitude = lat,
        Longitude = lon,
        Altitude = 0
    };
}
