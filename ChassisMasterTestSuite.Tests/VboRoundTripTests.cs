using System.Globalization;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;

namespace ChassisMasterTestSuite.Tests;

public class VboRoundTripTests
{
    [Fact]
    public void Recorder_ThenReader_PreservesEastPositiveLongitude()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "cmts-vbo-roundtrip-" + Guid.NewGuid().ToString("N") + ".vbo");

        try
        {
            // Shanghai-ish: east longitude must stay positive after round-trip.
            const double latDeg = 31.2304;
            const double lonDeg = 121.4737;

            var sample = new VehicleSample
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Sequence = 0,
                SpeedKph = 80.5,
                LongitudinalAcceleration = 1.0,
                LateralAcceleration = -0.5,
                VerticalAcceleration = 0.1,
                YawRate = 3.2,
                Heading = 45.0,
                Latitude = latDeg,
                Longitude = lonDeg,
                Altitude = 10.0
            };

            using (var recorder = new VboRecorder(path))
            {
                Assert.True(recorder.TryWrite(sample));
            }

            // File itself uses Racelogic sign: east = negative arcmin.
            var text = File.ReadAllText(path);
            Assert.Contains("[data]", text);
            var dataLine = text
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .SkipWhile(l => !l.Trim().Equals("[data]", StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .First(l => !string.IsNullOrWhiteSpace(l));

            var tokens = dataLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // columns: time Elapsed velocity Longacc Latacc Yaw heading lat long height Z
            Assert.True(tokens.Length >= 10);
            var longArcmin = double.Parse(tokens[8], CultureInfo.InvariantCulture);
            Assert.True(longArcmin < 0, "VBO file long column should be negative for east longitude.");
            Assert.Equal(-lonDeg * 60.0, longArcmin, precision: 4);

            var reader = new VboReader(path);
            var samples = reader.ReadAll();
            Assert.Single(samples);

            var restored = samples[0];
            Assert.Equal(latDeg, restored.Latitude, precision: 5);
            Assert.Equal(lonDeg, restored.Longitude, precision: 5);
            Assert.True(restored.Longitude > 0);
            Assert.Equal(80.5, restored.SpeedKph, precision: 2);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
