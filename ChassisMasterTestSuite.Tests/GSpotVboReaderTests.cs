using Chassis_Master_Test_Suite.Controls;
using Chassis_Master_Test_Suite.Core;
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
    public void GSpot_Vbo_MapsSpeedHeadingTimeHeight_FromShiftedColumns()
    {
        var samples = new VboReader(FixturePath).ReadAll();

        // sats column -> speed km/h (NOT the mislabeled "velocity" elapsed-ms column)
        Assert.Equal(0.05, samples[0].SpeedKph, precision: 3);
        Assert.Equal(50.2, samples[1].SpeedKph, precision: 2);
        Assert.Equal(100.0, samples[2].SpeedKph, precision: 2);
        Assert.True(samples[1].SpeedKph < 400, "speed must not be elapsed-ms garbage");

        // time column -> heading deg
        Assert.Equal(180.0, samples[1].Heading, precision: 2);
        Assert.Equal(90.0, samples[2].Heading, precision: 2);

        // velocity column -> elapsed timestamp ms
        Assert.Equal(13964, samples[0].Timestamp);
        Assert.Equal(728490, samples[1].Timestamp);
        Assert.Equal(1028890, samples[2].Timestamp);

        // height centimetres -> metres
        Assert.Equal(0.0, samples[0].Altitude, precision: 3);
        Assert.Equal(60.76, samples[1].Altitude, precision: 2);
        Assert.Equal(59.56, samples[2].Altitude, precision: 2);

        Assert.Equal(50.2, samples[1].Channels[ChannelIds.Velocity], precision: 2);
        Assert.Equal(180.0, samples[1].Channels[ChannelIds.Heading], precision: 2);
    }

    [Fact]
    public void GSpot_Vbo_AllPointsSurviveIsValidGps_AndOutlierFilter()
    {
        var samples = new VboReader(FixturePath).ReadAll();

        var valid = samples.Where(TrackProjection.IsValidGps).ToList();
        Assert.Equal(samples.Count, valid.Count);

        var filtered = TrackProjection.FilterGpsOutliers(samples);
        Assert.Equal(samples.Count, filtered.Count);
    }

    [Fact]
    public void GSpot_Vbo_EnrichedDistance_UsesHaversineForFineSteps()
    {
        var raw = new VboReader(FixturePath).ReadAll();
        var enriched = SampleEnricher.EnrichAll(raw);

        // Fixture lat steps are 0.0005° (~55 m) — under the 500 m jump cap.
        var dist = enriched[^1].Channels[ChannelIds.DistanceTraveled];
        Assert.InRange(dist, 50, 250);
        Assert.True(enriched[^1].SpeedKph < 400);
    }

    [Fact]
    public void StepDistance_FallsBackToSpeedWhenGpsJumpDiscarded()
    {
        var a = new VehicleSample
        {
            Timestamp = 0,
            SpeedKph = 72, // 20 m/s
            Latitude = 43.0,
            Longitude = 109.4
        };
        var b = new VehicleSample
        {
            Timestamp = 1000, // 1 s later
            SpeedKph = 72,
            Latitude = 43.02, // ~2.2 km jump — HaversineMeters returns 0
            Longitude = 109.4
        };

        var step = SampleEnricher.StepDistanceMeters(a, b);
        Assert.Equal(20.0, step, precision: 2);
    }

    [Fact]
    public void Racelogic_RoundTrip_StillUsesArcmin()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "cmts-vbo-racelogic-" + Guid.NewGuid().ToString("N") + ".vbo");

        try
        {
            const double latDeg = 31.2304;
            const double lonDeg = 121.4737;

            var sample = new VehicleSample
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
            Assert.Equal(60, restored.SpeedKph, precision: 2);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}