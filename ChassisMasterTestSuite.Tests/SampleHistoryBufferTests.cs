using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class SampleHistoryBufferTests
{
    [Fact]
    public void Add_BeyondCapacity_DropsOldest_KeepsCountAtCapacity()
    {
        var buffer = new SampleHistoryBuffer(capacity: 3);

        buffer.Add(Sample(1));
        buffer.Add(Sample(2));
        buffer.Add(Sample(3));
        buffer.Add(Sample(4));

        Assert.Equal(3, buffer.Count);
        Assert.Equal(3, buffer.Capacity);

        var snap = buffer.Snapshot();
        Assert.Equal(3, snap.Count);
        Assert.Equal(2, snap[0].Sequence);
        Assert.Equal(3, snap[1].Sequence);
        Assert.Equal(4, snap[2].Sequence);
    }

    [Fact]
    public void Snapshot_IsIsolated_FromLaterMutations()
    {
        var buffer = new SampleHistoryBuffer(capacity: 8);
        buffer.Add(Sample(10));
        buffer.Add(Sample(20));

        var snap = buffer.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Equal(10, snap[0].Sequence);

        buffer.Add(Sample(30));
        buffer.Clear();
        buffer.Add(Sample(99));

        Assert.Equal(2, snap.Count);
        Assert.Equal(10, snap[0].Sequence);
        Assert.Equal(20, snap[1].Sequence);
        Assert.Equal(1, buffer.Count);
        Assert.Equal(99, buffer.Snapshot()[0].Sequence);
    }

    [Fact]
    public void Snapshot_Empty_ReturnsEmpty()
    {
        var buffer = new SampleHistoryBuffer(capacity: 4);
        Assert.Empty(buffer.Snapshot());
        Assert.Equal(0, buffer.Count);
    }

    private static VehicleSample Sample(long sequence) =>
        new()
        {
            Sequence = sequence,
            Timestamp = sequence * 10,
            SpeedKph = sequence
        };
}
