using System.Globalization;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;

namespace ChassisMasterTestSuite.Tests;

public class VboTimestampSanitizerTests
{
    [Fact]
    public void IsolatedMidnightRow_IsDroppedFromTimeOfDayCluster()
    {
        var samples = new List<VehicleSample> { Sample(0, 0.02) };
        var start = TimeMs(8, 47, 23, 332);
        for (var i = 0; i < 200; i++)
            samples.Add(Sample(start + i * 10, 0.5 + i));

        var kept = VboTimestampSanitizer.DropIsolatedOutliers(samples);
        Assert.Equal(200, kept.Count);
        Assert.Equal(start, kept[0].Timestamp);
        Assert.DoesNotContain(kept, s => s.Timestamp == 0);
        Assert.True(kept[^1].Timestamp - kept[0].Timestamp < 60_000);
    }

    [Fact]
    public void MidnightWrap_IsKeptAsOneCluster()
    {
        var samples = new List<VehicleSample>();
        for (var i = 0; i < 50; i++)
            samples.Add(Sample(TimeMs(23, 59, 0, 0) + i * 100, 10));
        for (var i = 0; i < 50; i++)
            samples.Add(Sample(i * 100, 11));

        var kept = VboTimestampSanitizer.DropIsolatedOutliers(samples);
        Assert.Equal(100, kept.Count);
    }

    [Fact]
    public void TwoLargeSessionsHoursApart_AreBothKept()
    {
        var samples = new List<VehicleSample>();
        for (var i = 0; i < 80; i++)
            samples.Add(Sample(TimeMs(8, 0, 0, 0) + i * 100, 10));
        for (var i = 0; i < 80; i++)
            samples.Add(Sample(TimeMs(12, 0, 0, 0) + i * 100, 20));

        var kept = VboTimestampSanitizer.DropIsolatedOutliers(samples);
        Assert.Equal(160, kept.Count);
    }

    [Fact]
    public void ApplyFileCreatedDate_MapsUtcTimeOfDayOntoNearestCalendarDay()
    {
        var created = new DateTimeOffset(2026, 11, 19, 16, 47, 0, TimeSpan.FromHours(8));
        var sample = Sample(TimeMs(8, 47, 23, 332), 12);
        var unix = Assert.Single(VboTimestampSanitizer.ApplyFileCreatedDate([sample], created)).Timestamp;
        var expected = new DateTimeOffset(2026, 11, 19, 8, 47, 23, 332, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(expected, unix);
    }

    [Fact]
    public void ApplyFileCreatedDate_PicksPreviousUtcDayNearLocalMidnight()
    {
        // Local 20/11 00:30 = 19/11 16:30 UTC; time column 163000 is that UTC clock.
        var created = new DateTimeOffset(2026, 11, 20, 0, 30, 0, TimeSpan.FromHours(8));
        var sample = Sample(TimeMs(16, 30, 0, 0), 12);
        var unix = Assert.Single(VboTimestampSanitizer.ApplyFileCreatedDate([sample], created)).Timestamp;
        var expected = new DateTimeOffset(2026, 11, 19, 16, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(expected, unix);
    }

    [Fact]
    public void Reader_SkipsNanTimeAndDropsLeadingZeroTimeRow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cmts-vbo-gap-{Guid.NewGuid():N}.vbo");
        try
        {
            var lines = new List<string>
            {
                "File created on 19/11/2026 @ 16:47",
                "[column names]",
                "time velocity",
                "[data]",
                "000000.000 0.022",
                "NaN 0.000",
                "084723.332 1.000",
                "084723.342 1.100",
            };
            for (var i = 0; i < 30; i++)
                lines.Add($"084723.{352 + i:000} 1.{i:000}");
            File.WriteAllLines(path, lines);

            var reader = new VboReader(path);
            var rows = reader.ReadAll();
            Assert.True(reader.TreatTimestampsAsTimeOfDay);
            Assert.Equal(new DateTime(2026, 11, 19, 16, 47, 0), reader.FileCreatedLocal);
            Assert.DoesNotContain(rows, r => r.Timestamp == 0);
            Assert.True(rows.Count >= 30);
            Assert.Equal(TimeMs(8, 47, 23, 332), rows[0].Timestamp);
            Assert.True(rows[^1].Timestamp - rows[0].Timestamp < 2_000);
            Assert.True(reader.SkippedDataLines >= 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OfflineLoader_AppliesFileDateSoTimeAxisUsesUnixMilliseconds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cmts-vbo-date-{Guid.NewGuid():N}.vbo");
        try
        {
            var lines = new List<string>
            {
                "File created on 19/11/2026 @ 16:47",
                "[column names]",
                "time velocity",
                "[data]",
                "000000.000 0.022",
            };
            for (var i = 0; i < 40; i++)
                lines.Add($"084723.{332 + i:000} 1.{i:000}");
            File.WriteAllLines(path, lines);

            var track = Assert.Single(OfflineTrackLoader.Load(path).Tracks);
            var offset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 19, 16, 47, 0));
            var created = new DateTimeOffset(new DateTime(2026, 11, 19, 16, 47, 0), offset);
            var expectedFirst = VboTimestampSanitizer.ToUnixMilliseconds(
                TimeMs(8, 47, 23, 332), created.ToUnixTimeMilliseconds());
            Assert.Equal(expectedFirst, track.Samples[0].Timestamp);
            Assert.True(track.Samples[0].Timestamp > 1_000_000_000_000);
            Assert.True(track.Samples[^1].Timestamp - track.Samples[0].Timestamp < 2_000);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryParseFileCreatedOn_AcceptsAtAndAtWord()
    {
        Assert.True(VboReader.TryParseFileCreatedOn(
            "File created on 19/11/2026 @ 16:47", out var a));
        Assert.Equal(new DateTime(2026, 11, 19, 16, 47, 0), a);
        Assert.True(VboReader.TryParseFileCreatedOn(
            "File created on 01/02/2025 at 09:08:07", out var b));
        Assert.Equal(new DateTime(2025, 2, 1, 9, 8, 7), b);
    }

    private static long TimeMs(int hour, int minute, int second, int millisecond) =>
        ((hour * 3600L + minute * 60L + second) * 1000L) + millisecond;

    private static VehicleSample Sample(long timestamp, double speed) =>
        new()
        {
            Timestamp = timestamp,
            SpeedKph = speed,
            Channels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["time"] = timestamp / 1000.0,
                ["velocity"] = speed
            }
        };
}
