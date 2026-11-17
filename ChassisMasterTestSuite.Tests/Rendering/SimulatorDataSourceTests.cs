using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Simulator;
using Chassis_Master_Test_Suite.Communication;
using Xunit;

public class SimulatorDataSourceTests
{
    [Fact]
    public async Task PublishesDirectlyStopsAndRestarts()
    {
        var bus = new DataBus();
        using var source = new SimulatorDataSource(bus);
        await source.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sample = await bus.Reader.ReadAsync(timeout.Token);
        Assert.Equal("simulator", sample.VehicleId);
        Assert.InRange(sample.Latitude, 31, 32);
        Assert.InRange(sample.Longitude, 121, 122);
        Assert.True(sample.Timestamp > 0);
        await source.StopAsync();
        var count = source.Stats.Received;
        await Task.Delay(40);
        Assert.Equal(count, source.Stats.Received);
        Assert.Equal(DataSourceState.Disconnected, source.State);
        while (bus.Reader.TryRead(out _)) { }
        await source.StartAsync();
        var next = await bus.Reader.ReadAsync(timeout.Token);
        Assert.True(next.Sequence > sample.Sequence);
        await source.StopAsync();
        await source.StopAsync();
    }
}
