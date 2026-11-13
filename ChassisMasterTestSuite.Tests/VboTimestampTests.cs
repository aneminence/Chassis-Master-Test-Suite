using System.Globalization;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;

namespace ChassisMasterTestSuite.Tests;

public class VboTimestampTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(12, 34, 56)]
    [InlineData(22, 13, 20)]
    [InlineData(23, 59, 59)]
    public void RecorderReader_EveryMillisecondInSecond_RoundTripsExactly(int hour, int minute, int second)
    {
        var path = TempPath();
        try
        {
            var origin = new DateTimeOffset(2026, 11, 11, hour, minute, second, TimeSpan.Zero);
            using (var recorder = new VboRecorder(path))
            {
                for (var ms = 0; ms < 1000; ms++)
                    Assert.True(recorder.TryWrite(new VehicleSample
                    {
                        Timestamp = origin.ToUnixTimeMilliseconds() + ms,
                        SpeedKph = 50,
                        Latitude = 31,
                        Longitude = 121
                    }));
            }

            var rows = new VboReader(path).ReadAll();
            Assert.Equal(1000, rows.Count);
            var startOfSecond = (hour * 3600L + minute * 60L + second) * 1000;
            for (var ms = 0; ms < 1000; ms++)
                Assert.Equal(startOfSecond + ms, rows[ms].Timestamp);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TimeOnlyBoundaries_KeepMinuteHourAndMidnightSemantics()
    {
        var path = TempPath();
        try
        {
            var times = new[] { "000059.999", "000100.000", "005959.999", "010000.000",
                "235959.999", "000000.000", "000000.001" };
            File.WriteAllLines(path, new[] { "[column names]", "time velocity", "[data]" }
                .Concat(times.Select(time => $"{time} 50")));
            var rows = new VboReader(path).ReadAll();

            // VBO carries no date: midnight remains time-of-day zero; this fix
            // must not invent a date/day offset or change the elapsed channel.
            Assert.Equal(new long[] { 59_999, 60_000, 3_599_999, 3_600_000,
                86_399_999, 0, 1 }, rows.Select(row => row.Timestamp));
            Assert.All(rows, row => Assert.Equal(50, row.SpeedKph));
            for (var i = 0; i < rows.Count; i++)
                Assert.Equal(double.Parse(times[i], CultureInfo.InvariantCulture), rows[i].Channels["time"]);
        }
        finally { File.Delete(path); }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"cmts-vbo-time-{Guid.NewGuid():N}.vbo");
}
