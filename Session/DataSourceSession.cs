using Chassis_Master_Test_Suite.Communication;
using Chassis_Master_Test_Suite.Communication.GSpot;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Session;

/// <summary>
/// 当前 <see cref="IDataSource"/> 所有权与切换（UDP / GSpot）。
/// 不负责 ChannelRegistry、离线历史标志或 UI；那些仍由 MainWindow 处理。
/// </summary>
public sealed class DataSourceSession : IDisposable
{
    private readonly DataBus _dataBus;
    private IDataSource? _dataSource;
    private bool _disposed;

    public DataSourceSession(DataBus dataBus)
    {
        _dataBus = dataBus ?? throw new ArgumentNullException(nameof(dataBus));
    }

    public DataBus DataBus => _dataBus;

    public IDataSource? Current => _dataSource;

    public UdpReceiver CreateUdpReceiver() => new(_dataBus);

    public GSpotDataSource CreateGSpot(GSpotOptions options) =>
        new(_dataBus, options);

    /// <summary>
    /// 设置当前源引用（调用方负责 Start / Stop）。
    /// </summary>
    public void SetCurrent(IDataSource? source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dataSource = source;
    }

    /// <summary>
    /// 停掉旧源并切换到 <paramref name="next"/>，然后 <c>StartAsync</c>。
    /// 行为与原 MainWindow.SwitchDataSourceAsync 一致（不含 EnterLiveSourceMode）。
    /// </summary>
    public async Task SwitchAsync(IDataSource next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(next);

        var old = _dataSource;
        _dataSource = null;

        if (old is not null)
        {
            try
            {
                await old.StopAsync();
            }
            catch
            {
                try { old.Dispose(); } catch { /* ignore */ }
            }
        }

        _dataSource = next;
        await next.StartAsync();
    }

    /// <summary>
    /// 在 <paramref name="next"/> 已 Connected 后提交切换：
    /// 清空引用、停 <paramref name="previous"/>、挂上 next（不再 Start）。
    /// </summary>
    public async Task CommitConnectedAsync(
        IDataSource? previous,
        IDataSource next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(next);

        _dataSource = null;

        if (previous is not null)
        {
            try
            {
                await previous.StopAsync();
            }
            catch
            {
                try { previous.Dispose(); } catch { /* ignore */ }
            }
        }

        _dataSource = next;
    }

    /// <summary>
    /// 等待 Connected，或 Faulted / GSpot 首轮 Reconnecting+LastError 提前失败。
    /// </summary>
    public static async Task<bool> WaitForConnectedAsync(
        IDataSource source,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(source);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (source.State == DataSourceState.Connected)
                return true;

            if (source.State == DataSourceState.Faulted)
                return false;

            // Reconnecting 且已有 LastError：首轮已失败，不必空等满超时
            if (source is GSpotDataSource gspot &&
                source.State == DataSourceState.Reconnecting &&
                !string.IsNullOrWhiteSpace(gspot.LastError))
            {
                // 再给一次瞬间机会，避免刚写下 LastError 时误判
                await Task.Delay(400);
                if (source.State == DataSourceState.Connected)
                    return true;
                return false;
            }

            await Task.Delay(200);
        }

        return source.State == DataSourceState.Connected;
    }

    /// <summary>
    /// 关闭窗口：同步 Dispose 当前源（与原 Closed 路径一致）。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _dataSource?.Dispose();
        _dataSource = null;
    }
}
