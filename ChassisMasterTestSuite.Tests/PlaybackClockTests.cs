using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class PlaybackClockTests
{
    [Fact]
    public void Empty_CannotPlay_AndPlayIsNoOp()
    {
        var clock = new PlaybackClock();
        Assert.False(clock.CanPlay);
        clock.Play(1_000);
        Assert.False(clock.IsPlaying);
        Assert.False(clock.Tick(2_000));
        Assert.Null(clock.CurrentSample);
    }

    [Fact]
    public void Tick_AdvancesInRealTime_AndPicksSampleAtOrBefore()
    {
        var clock = new PlaybackClock();
        clock.SetSamples(Samples(startMs: 1_000, count: 11, stepMs: 100));
        Assert.True(clock.CanPlay);
        Assert.Equal(1_000, clock.StartTimestampMs);
        Assert.Equal(2_000, clock.EndTimestampMs);
        Assert.Equal(0, clock.CurrentIndex);

        clock.Play(wallMs: 10_000);
        Assert.True(clock.IsPlaying);

        Assert.True(clock.Tick(10_250));
        Assert.Equal(1_250, clock.CurrentTimestampMs);
        Assert.Equal(2, clock.CurrentIndex);
        Assert.Equal(1_200, clock.CurrentSample!.Timestamp);
    }

    [Fact]
    public void Tick_HonorsSpeedAndRebasing()
    {
        var clock = new PlaybackClock();
        clock.SetSamples(Samples(startMs: 0, count: 11, stepMs: 100));
        clock.Play(0);
        clock.Tick(100);
        Assert.Equal(100, clock.CurrentTimestampMs);

        clock.SetSpeed(2.0, wallMs: 100);
        clock.Tick(200);
        Assert.Equal(300, clock.CurrentTimestampMs);
        Assert.Equal(3, clock.CurrentIndex);
    }

    [Fact]
    public void Tick_StopsAtEnd()
    {
        var clock = new PlaybackClock();
        clock.SetSamples(Samples(startMs: 0, count: 5, stepMs: 100));
        clock.Play(0);
        Assert.True(clock.Tick(1_000));
        Assert.False(clock.IsPlaying);
        Assert.Equal(400, clock.CurrentTimestampMs);
        Assert.Equal(4, clock.CurrentIndex);
    }

    [Fact]
    public void Play_WhenAtEnd_RestartsFromStart()
    {
        var clock = new PlaybackClock();
        clock.SetSamples(Samples(startMs: 0, count: 5, stepMs: 100));
        clock.SeekToTimestamp(400, 0);
        clock.Play(50);
        Assert.True(clock.IsPlaying);
        Assert.Equal(0, clock.CurrentTimestampMs);
        clock.Tick(150);
        Assert.Equal(100, clock.CurrentTimestampMs);
    }

    [Fact]
    public void SeekToProgress_AndStop_ResetPosition()
    {
        var clock = new PlaybackClock();
        clock.SetSamples(Samples(startMs: 1_000, count: 11, stepMs: 100));
        clock.Play(0);
        clock.SeekToProgress(0.5, 20);
        Assert.Equal(1_500, clock.CurrentTimestampMs);
        Assert.Equal(0.5, clock.Progress, 3);
        Assert.True(clock.IsPlaying);

        clock.Stop();
        Assert.False(clock.IsPlaying);
        Assert.Equal(1_000, clock.CurrentTimestampMs);
        Assert.Equal(0, clock.CurrentIndex);
    }

    [Fact]
    public void FormatElapsed_UsesTenths()
    {
        Assert.Equal("0:00.0", PlaybackClock.FormatElapsed(0));
        Assert.Equal("0:01.5", PlaybackClock.FormatElapsed(1_500));
        Assert.Equal("1:02.3", PlaybackClock.FormatElapsed(62_349));
        Assert.Equal("1:01:01.0", PlaybackClock.FormatElapsed(3_661_000));
    }

    [Fact]
    public void FormatPositionLabel_ShowsElapsedOverDuration()
    {
        var clock = new PlaybackClock();
        clock.SetSamples(Samples(0, 11, 100));
        clock.SeekToTimestamp(500, 0);
        Assert.Equal("0:00.5 / 0:01.0", clock.FormatPositionLabel());
    }

    private static List<VehicleSample> Samples(long startMs, int count, long stepMs)
    {
        var list = new List<VehicleSample>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new VehicleSample
            {
                Timestamp = startMs + i * stepMs,
                Sequence = i,
                SpeedKph = i
            });
        }
        return list;
    }
}
