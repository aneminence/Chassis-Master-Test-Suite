using Chassis_Master_Test_Suite.Communication;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Simulator;

/// <summary>Owns the simulation loop and publishes samples without a network transport.</summary>
public sealed class SimulatorDataSource : IDataSource
{
    private readonly DataBus _bus;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private long _received;
    private long _valid;
    private bool _disposed;
    public VehicleSimulator Simulator { get; }
    public string Name => "Simulator";
    public DataSourceState State { get; private set; }
    public DataSourceStats Stats => new(Interlocked.Read(ref _received), Interlocked.Read(ref _valid), 0, 0);
    public event EventHandler<DataSourceState>? StateChanged;

    public SimulatorDataSource(DataBus bus)
    {
        _bus = bus;
        Simulator = new VehicleSimulator(sample =>
        {
            Interlocked.Increment(ref _received);
            if (_bus.TryPublish(sample)) Interlocked.Increment(ref _valid);
        });
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_task is { IsCompleted: false }) return Task.CompletedTask;
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        SetState(DataSourceState.Connected);
        _task = Task.Run(async () =>
        {
            try { await Simulator.RunAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch { SetState(DataSourceState.Faulted); return; }
            SetState(DataSourceState.Disconnected);
        });
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_task is not null) await _task.ConfigureAwait(false);
        Simulator.ReleaseControls();
        SetState(DataSourceState.Disconnected);
    }

    private void SetState(DataSourceState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
        _cts = null;
    }
}
