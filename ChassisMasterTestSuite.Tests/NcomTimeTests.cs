using Chassis_Master_Test_Suite.Recorder.OxTS;

namespace ChassisMasterTestSuite.Tests;

public class NcomTimeTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "oxts", name);

    [Fact]
    public void Standalone_Ncom_Timestamps_Span_Full_Duration_Not_All_Zero()
    {
        var ncomPath = Fixture("mobile-015.ncom");
        Assert.True(File.Exists(ncomPath));

        var track = XcomImporter.ImportNcomFile(ncomPath);
        Assert.True(track.Samples.Count >= 100);

        var t0 = track.Samples[0].Timestamp;
        var t1 = track.Samples[^1].Timestamp;

        Assert.True(t0 > NcomTime.GpsEpochUnixMs, "expected absolute GPS-based Unix ms");
        Assert.True(t1 > t0, "timestamps must increase");

        var spanMs = t1 - t0;
        // Fixture is ~21 s of data at 100 Hz.
        Assert.InRange(spanMs, 15_000, 30_000);

        // Not the old bug: every sample Timestamp == 0 → Curves Time hairline at 00:00.
        Assert.DoesNotContain(track.Samples, s => s.Timestamp == 0);
    }

    [Fact]
    public void AssignUnixTimestamps_Uses_StatusChannel0_Minutes_Plus_BatchA_Ms()
    {
        var bytes = File.ReadAllBytes(Fixture("mobile-015.ncom"));
        var all = NcomDecoder.DecodeAll(bytes);
        Assert.True(all.Count >= 5);

        var stamps = NcomTime.AssignUnixTimestamps(all);
        Assert.Equal(all.Count, stamps.Length);

        var firstWithCh0 = all.Select((m, i) => (m, i)).First(x => NcomTime.TryReadGpsMinutes(x.m, out _));
        Assert.True(NcomTime.TryReadGpsMinutes(firstWithCh0.m, out var minutes));
        var expected = NcomTime.GpsMinutesAndMsToUnixMs(minutes, firstWithCh0.m.TimeMsIntoMinute);
        Assert.Equal(expected, stamps[firstWithCh0.i]);
    }

    [Fact]
    public void GpsNsToUnixMs_Adds_GpsEpoch()
    {
        // Doc sample GPS seconds ~1472383616.400 → 2026-09-02 UTC (no leap adjust).
        var gpsNs = 1_472_383_616_400_000_000L;
        var unixMs = NcomTime.GpsNsToUnixMs(gpsNs);
        var dto = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        Assert.Equal(2026, dto.UtcDateTime.Year);
        Assert.Equal(9, dto.UtcDateTime.Month);
        Assert.Equal(2, dto.UtcDateTime.Day);
    }
}
