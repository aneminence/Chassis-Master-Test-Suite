using Chassis_Master_Test_Suite.Controls;
using Chassis_Master_Test_Suite.Recorder;

namespace ChassisMasterTestSuite.Tests;

public class GSpotVboReaderTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "gspot-g4-mini.vbo");

    [Fact]
    public void GSpot_Vbo_DecodesLatLon_NotRacelogicArcmin()
    {
        Assert.True(File.Exists(FixturePath), $"missing fixture: {FixturePath}");

        var reader = new VboReader(FixturePath);
        var samples = reader.ReadAll();

        Assert.Equal(3, samples.Count);

        // Raw lat 431.9 / 10 = 43.19°N; long 1094232194 / 1e7 = 109.4232194°E
        Assert.Equal(43.19, samples[0].Latitude, precision: 5);
        Assert.Equal(109.4232194, samples[0].Longitude, precision: 5);
        Assert.True(samples[0].Longitude > 0, "GSpot east longitude must stay positive");

        Assert.Equal(43.1905, samples[1].Latitude, precision: 5);
        Assert.Equal(43.191, samples[2].Latitude, precision: 5);
        Assert.Equal(109.42328, samples[2].Longitude, precision: 5);
    }

    [Fact]
    public void GSpot_Vbo_AllPointsSurviveIsValidGps_AndOutlierFilter()
    {
        var reader = new VboReader(FixturePath);
        var samples = reader.ReadAll();

        var valid = samples.Where(TrackProjection.IsValidGps).ToList();
        Assert.Equal(samples.Count, valid.Count);

        var filtered = TrackProjection.FilterGpsOutliers(samples);
        Assert.Equal(samples.Count, filtered.Count);
    }

    [Fact]
    public void Racelogic_RoundTrip_StillUsesArcmin()
    {
        // Sanity: GSpot path must not break CMTS/VBOX arcmin files.
        var path = Path.Combine(
            Path.GetTempPath(),
            "cmts-vbo-racelogic-" + Guid.NewGuid().ToString("N") + ".vbo");

        try
        {
            const double latDeg = 31.2304;
            const double lonDeg = 121.4737;

            var sample = new Chassis_Master_Test_Suite.Core.VehicleSample
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Sequence = 0,
                SpeedKph = 60,
                Latitude = latDeg,
                Longitude = lonDeg,
                Altitude = 5
            };

            using (var recorder = new VboRecorder(path))
            {
                Assert.True(recorder.TryWrite(sample));
            }

            var restored = new VboReader(path).ReadAll().Single();
            Assert.Equal(latDeg, restored.Latitude, precision: 5);
            Assert.Equal(lonDeg, restored.Longitude, precision: 5);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}