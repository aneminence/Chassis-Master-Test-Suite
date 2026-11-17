using Chassis_Master_Test_Suite.Communication;
using Chassis_Master_Test_Suite.Communication.GSpot;
using Chassis_Master_Test_Suite.Communication.Remote;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Session;

/// <summary>
/// 当前 <see cref="IDataSource"/> 所有权与切换（UDP / GSpot / CMTS Remote）。
/// 可同时挂多个 Remote；UDP / GSpot 仍为独占源（切换时会停掉全部 Remote）。
/// 不负责 ChannelRegistry、离线历史标志或 UI；那些仍由 MainWindow 处理。
/// </summary>
public sealed class DataSourceSession : IDisposable
{
    private readonly DataBus _dataBus;
    private readonly List<IDataSource> _sources = new();
    private IDataSource? _dataSource;
    private bool _disposed;

    public DataSourceSession(DataBus dataBus)
    {
        _dataBus = dataBus ?? throw new ArgumentNullException(nameof(dataBus));
    }

    public DataBus DataBus => _dataBus;

    public IDataSource? Current => _dataSource;

    public IReadOnlyList<IDataSource> ActiveSources
    {
        get
        {
            if (_sources.Count == 0 && _dataSource is not null)
                return new[] { _dataSource };
            return _sources.ToArray();
        }
    }

    public IReadOnlyList<CMTSRemoteDataSource> Remotes =>
        ActiveSources.OfType<CMTSRemoteDataSource>().ToArray();

    public bool HasUsableSource =>
        ActiveSources.Any(s =>
            s.State is DataSourceState.Connected
                or DataSourceState.Connecting
                or DataSourceState.Reconnecting);

    public UdpReceiver CreateUdpReceiver() => new(_dataBus);

    public GSpotDataSource CreateGSpot(GSpotOptions options) =>
        new(_dataBus, options);

    public CMTSRemoteDataSource CreateRemote(CMTSRemoteOptions options) =>
        new(_dataBus, options);

    public bool IsRemoteEndpointAttached(CMTSRemoteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var url = options.BuildWebSocketUrl();
        foreach (var remote in Remotes)
        {
            if (string.Equals(remote.Endpoint, url, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrWhiteSpace(options.ExpectedDeviceId) &&
                string.Equals(remote.DeviceId, options.ExpectedDeviceId, StringComparison.Ordinal))
                return true;
            if (string.Equals(remote.Options.Host, options.Host, StringComparison.OrdinalIgnoreCase) &&
                remote.Options.Port == options.Port &&
                string.Equals(
                    NormalizePath(remote.Options.Path),
                    NormalizePath(options.Path),
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizePath(string? path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "/cmts/v1" : path.Trim();
        return value.StartsWith('/') ? value : "/" + value;
    }

    /// <summary>
    /// 设置当前源引用（调用方负责 Start / Stop）。
    /// 会替换集合，不停止或释放旧源。
    /// </summary>
    public void SetCurrent(IDataSource? source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _sources.Clear();
        if (source is not null)
            _sources.Add(source);
        _dataSource = source;
    }

    /// <summary>
    /// 停掉全部旧源并切换到 <paramref name="next"/>，然后 <c>StartAsync</c>。
    /// </summary>
    public async Task SwitchAsync(IDataSource next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(next);

        await StopAllAsync().ConfigureAwait(true);
        _sources.Add(next);
        _dataSource = next;
        await next.StartAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 在 <paramref name="next"/> 已 Connected 后提交独占切换：
    /// 停掉全部旧源（含其它 Remote），挂上 next（不再 Start）。
    /// </summary>
    public async Task CommitConnectedAsync(
        IDataSource? previous,
        IDataSource next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(next);

        await StopAllExceptAsync(next).ConfigureAwait(true);
        if (!_sources.Contains(next))
            _sources.Add(next);
        _dataSource = next;

        if (previous is not null &&
            !ReferenceEquals(previous, next) &&
            !_sources.Contains(previous))
        {
            try
            {
                await previous.StopAsync().ConfigureAwait(true);
            }
            catch
            {
                try { previous.Dispose(); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// 在 next 已 Connected 后接入，不停止已经在跑的其它 Remote。
    /// 若当前是 UDP / GSpot 等独占源，会先停掉它们。
    /// </summary>
    public async Task AttachConnectedRemoteAsync(CMTSRemoteDataSource next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(next);

        var exclusive = ActiveSources
            .Where(s => s is not CMTSRemoteDataSource && !ReferenceEquals(s, next))
            .ToList();
        foreach (var old in exclusive)
        {
            _sources.Remove(old);
            if (ReferenceEquals(_dataSource, old))
                _dataSource = null;
            try
            {
                await old.StopAsync().ConfigureAwait(true);
            }
            catch
            {
                try { old.Dispose(); } catch { /* ignore */ }
            }
        }

        if (!_sources.Contains(next))
            _sources.Add(next);
        _dataSource = next;
    }

    public async Task StopAllAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StopAllExceptAsync(null).ConfigureAwait(true);
        _dataSource = null;
    }

    /// <summary>
    /// Stop and drop one source, keeping the others. Current becomes the last remaining source.
    /// </summary>
    public async Task DetachAsync(IDataSource source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);

        var removed = _sources.Remove(source);
        if (!removed && !ReferenceEquals(_dataSource, source))
            return;

        if (ReferenceEquals(_dataSource, source))
            _dataSource = _sources.Count > 0 ? _sources[^1] : null;

        try
        {
            await source.StopAsync().ConfigureAwait(true);
        }
        catch
        {
            try { source.Dispose(); } catch { /* ignore */ }
        }
    }

    private async Task StopAllExceptAsync(IDataSource? keep)
    {
        var sources = ActiveSources
            .Where(s => !ReferenceEquals(s, keep))
            .ToList();
        _sources.Clear();
        if (keep is not null)
            _sources.Add(keep);
        _dataSource = keep;

        foreach (var old in sources)
        {
            try
            {
                await old.StopAsync().ConfigureAwait(true);
            }
            catch
            {
                try { old.Dispose(); } catch { /* ignore */ }
            }
        }
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
            string? lastError = source switch
            {
                GSpotDataSource gspot => gspot.LastError,
                CMTSRemoteDataSource remote => remote.LastError,
                _ => null
            };

            if (source.State == DataSourceState.Reconnecting &&
                !string.IsNullOrWhiteSpace(lastError))
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
    /// 关闭窗口：同步 Dispose 全部源（与原 Closed 路径一致）。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var source in ActiveSources)
        {
            try { source.Dispose(); } catch { /* ignore */ }
        }

        _sources.Clear();
        _dataSource = null;
    }
}
