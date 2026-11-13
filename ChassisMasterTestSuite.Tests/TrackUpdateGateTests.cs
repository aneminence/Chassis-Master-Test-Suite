using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class TrackUpdateGateTests
{
    [Fact]
    public void NewSnapshotContainerDoesNotResendUnchangedTrack()
    {
        var gate = new TrackUpdateGate();
        var stamp = new TrackUpdateStamp(new object(), new object(), new object(), 100, "live", "red", true);
        Assert.True(gate.TryAccept(new[] { stamp }, 0));
        Assert.False(gate.TryAccept(new[] { stamp }, 2000));
    }

    [Fact]
    public void LiveAppendAndFullBufferSlidingAreBatchedToOneSecond()
    {
        var gate = new TrackUpdateGate();
        var stamp = new TrackUpdateStamp(new object(), new object(), new object(), 100, "live", "red", true);
        Assert.True(gate.TryAccept(new[] { stamp }, 0));
        var appended = stamp with { Last = new object(), Count = 110 };
        Assert.False(gate.TryAccept(new[] { appended }, 300));
        Assert.True(gate.TryAccept(new[] { appended }, 1000));
        var sliding = appended with { First = new object(), Last = new object() };
        Assert.False(gate.TryAccept(new[] { sliding }, 1300));
        Assert.True(gate.TryAccept(new[] { sliding }, 2000));
    }

    [Fact]
    public void ReplacementStyleChangesAndResetAreImmediate()
    {
        var gate = new TrackUpdateGate();
        var stamp = new TrackUpdateStamp(new object(), new object(), new object(), 100, "live", "red", true);
        Assert.True(gate.TryAccept(new[] { stamp }, 0));
        var replacement = stamp with { Source = new object() };
        Assert.True(gate.TryAccept(new[] { replacement }, 100));
        var recolored = replacement with { Color = "gold" };
        Assert.True(gate.TryAccept(new[] { recolored }, 200));
        gate.Reset();
        Assert.True(gate.TryAccept(new[] { recolored }, 201));
    }
}
