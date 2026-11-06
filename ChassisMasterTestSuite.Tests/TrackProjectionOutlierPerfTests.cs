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

    [Fact]
    public void FilterGpsOutliers_LongContinuousRoute_KeepsStartBeyond5km()
    {
        // ~12 km straight drive: median sits mid-route so the first ~2 km are
        // more than 5 km from the median. That is real motion, not a teleport.
        // Old filter kept >50% of points and never widened — red Track Map line
        // lost the opening minutes (CMTS phone KML / Tianjin commute case).
        const int count = 1201;
        var samples = new List<VehicleSample>(count + 1);
        const double startLat = 39.110;
        const double startLon = 117.236;
        // ~0.0001 deg lat ≈ 11 m; 1200 steps ≈ 13 km north→south-east-ish.
        for (var i = 0; i < count; i++)
        {
            samples.Add(new VehicleSample
            {
                Sequence = i,
                Latitude = startLat - i * 0.000035,
                Longitude = startLon + i * 0.000090,
                Altitude = 0
            });
        }

        // One true teleport far outside the route envelope.
        samples.Add(new VehicleSample
        {
            Sequence = count,
            Latitude = 40.0,
            Longitude = 116.0,
            Altitude = 0
        });

        var filtered = TrackProjection.FilterGpsOutliers(samples);
        Assert.True(filtered.Count >= count - 20, $"kept {filtered.Count}/{count}");
        Assert.Contains(filtered, s => s.Sequence == 0);
        Assert.Contains(filtered, s => s.Sequence == count - 1);
        Assert.DoesNotContain(filtered, s => Math.Abs(s.Latitude - 40.0) < 1e-9);
    }

    private static VehicleSample Gps(double lat, double lon) => new()
    {
        Latitude = lat,
        Longitude = lon,
        Altitude = 0
    };
}
