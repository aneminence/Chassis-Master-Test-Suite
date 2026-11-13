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

    [Fact]
    public void Snapshot_UnchangedContent_ReusesReadOnlySnapshotAndVersion()
    {
        var buffer = new SampleHistoryBuffer(3);
        buffer.AddRange(new[] { Sample(1), Sample(2), Sample(3) });
        var first = buffer.Snapshot();
        var version = buffer.Version;
        Assert.Same(first, buffer.Snapshot());
        Assert.Equal(version, buffer.Version);
        Assert.Throws<NotSupportedException>(() => ((IList<VehicleSample>)first)[0] = Sample(99));

        buffer.Add(Sample(4));
        Assert.True(buffer.Version > version);
        Assert.NotSame(first, buffer.Snapshot());
        Assert.Equal(new long[] { 2, 3, 4 }, buffer.Snapshot().Select(s => s.Sequence));
        Assert.Equal(new long[] { 1, 2, 3 }, first.Select(s => s.Sequence));
    }

    [Fact]
    public async Task Snapshot_ConcurrentWrapping_RemainsOrderedAndStable()
    {
        var buffer = new SampleHistoryBuffer(127);
        using var start = new ManualResetEventSlim();
        var producer = Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 20_000; i++)
                buffer.Add(Sample(i));
        });
        start.Set();
        for (var i = 0; i < 500; i++)
        {
            var snapshot = buffer.Snapshot();
            var original = snapshot.Select(s => s.Sequence).ToArray();
            for (var j = 1; j < snapshot.Count; j++)
                Assert.Equal(snapshot[j - 1].Sequence + 1, snapshot[j].Sequence);
            Assert.Equal(original, snapshot.Select(s => s.Sequence));
        }
        await producer;
        Assert.Equal(19_999, buffer.Snapshot()[^1].Sequence);
    }
}
