using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class DataBusTests
{
    [Fact]
    public void DropOldest_FillBeyondCapacity_ReaderSeesNewest()
    {
        var bus = new DataBus(capacity: 3);

        Assert.True(bus.TryPublish(Sample(1)));
        Assert.True(bus.TryPublish(Sample(2)));
        Assert.True(bus.TryPublish(Sample(3)));
        Assert.True(bus.TryPublish(Sample(4)));
        Assert.True(bus.TryPublish(Sample(5)));

        // DropOldest: TryWrite typically always succeeds → DroppedPublishCount stays 0.
        Assert.Equal(0, bus.DroppedPublishCount);

        var received = new List<long>();
        while (bus.Reader.TryRead(out var sample))
            received.Add(sample.Sequence);

        Assert.Equal(3, received.Count);
        Assert.Equal(new long[] { 3, 4, 5 }, received.ToArray());

        bus.Complete();
    }

    [Fact]
    public void TryPublish_ThenComplete_DrainsRemaining()
    {
        var bus = new DataBus(capacity: 10);
        Assert.True(bus.TryPublish(Sample(7)));
        bus.Complete();

        Assert.True(bus.Reader.TryRead(out var sample));
        Assert.Equal(7, sample.Sequence);
        Assert.False(bus.Reader.TryRead(out _));
    }

    private static VehicleSample Sample(long sequence) =>
        new() { Sequence = sequence, Timestamp = sequence };
}
