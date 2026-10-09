using System.Net.Sockets;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Communication;

/// <summary>
/// CMTS V0.1 UDP 数据接收器，实现 <see cref="IDataSource"/>。
///
/// 默认监听本机 50000 端口，解析后写入 DataBus，
/// 并统计 Received / Valid / Lost / OutOfOrder。
/// </summary>
public sealed class UdpReceiver : IDataSource
{
    private readonly DataBus _dataBus;
    private readonly int _port;
    private UdpClient? _udpClient;

    private long _receivedPackets;
    private long _validPackets;
    private long _lostPackets;
    private long _outOfOrderPackets;

    private long _lastSequence = -1;

    private DataSourceState _state = DataSourceState.Disconnected;
    private Task? _runTask;
    private int _started; // 0 = not started, 1 = started
    private int _disposed;

    /// <summary>最近一次启动/运行失败原因（绑定失败等）。</summary>
    public string? LastError { get; private set; }

    public UdpReceiver(
        DataBus dataBus,
        int port = 50000)
    {
        _dataBus = dataBus;
        _port = port;
    }

    public string Name => "UDP";

    public DataSourceState State => _state;

    public DataSourceStats Stats => new(
        Interlocked.Read(ref _receivedPackets),
        Interlocked.Read(ref _validPackets),
        Interlocked.Read(ref _lostPackets),
        Interlocked.Read(ref _outOfOrderPackets));

    public event EventHandler<DataSourceState>? StateChanged;

    /// <summary>接收到的 UDP 数据包总数。</summary>
    public long ReceivedPackets =>
        Interlocked.Read(ref _receivedPackets);

    /// <summary>成功解析的数据包数量。</summary>
    public long ValidPackets =>
        Interlocked.Read(ref _validPackets);

    /// <summary>检测到的丢包数量。</summary>
    public long LostPackets =>
        Interlocked.Read(ref _lostPackets);

    /// <summary>检测到的乱序数据包数量。</summary>
    public long OutOfOrderPackets =>
        Interlocked.Read(ref _outOfOrderPackets);

    /// <summary>
    /// 绑定端口并启动接收循环。重复调用是安全的（幂等）。
    /// </summary>
    public Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _disposed != 0,
            this);

        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(
                ref _started, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        SetState(DataSourceState.Connecting);
        LastError = null;

        try
        {
            _udpClient = new UdpClient(_port);
        }
        catch (SocketException ex)
        {
            Interlocked.Exchange(ref _started, 0);
            LastError =
                $"无法绑定 UDP 端口 {_port}：{ex.Message}" +
                "（常见原因：上一次 CMTS 未退出，端口仍被占用）。";
            SetState(DataSourceState.Faulted);
            throw new InvalidOperationException(LastError, ex);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _started, 0);
            LastError = $"UDP 启动失败：{ex.Message}";
            SetState(DataSourceState.Faulted);
            throw new InvalidOperationException(LastError, ex);
        }

        SetState(DataSourceState.Connected);

        // 接收循环自己跑；StartAsync 只表示“已开始监听”。
        _runTask = Task.Run(
            () => RunAsync(),
            CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 关闭 socket，等待接收循环退出，状态回到 Disconnected。
    /// </summary>
    public async Task StopAsync()
    {
        Dispose();

        var runTask = _runTask;
        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch
            {
                // 关闭过程中的异常已在 RunAsync 内处理。
            }
        }
    }

    /// <summary>
    /// 开始接收 UDP 数据。
    ///
    /// 关闭方式：
    /// 调用 Dispose() / StopAsync() 关闭底层 socket，
    /// 本循环通过 ObjectDisposedException 退出。
    ///
    /// 这里刻意不使用 CancellationToken：
    /// ReceiveAsync(token) 在取消时会抛 OperationCanceledException，
    /// 而关闭是正常流程，不应该依赖异常来做控制流。
    /// </summary>
    private async Task RunAsync()
    {
        var udpClient = _udpClient;
        if (udpClient is null)
            return;

        while (true)
        {
            UdpReceiveResult result;

            try
            {
                result = await udpClient.ReceiveAsync();
            }
            catch (ObjectDisposedException)
            {
                // socket 已被 Dispose() 关闭，正常退出。
                break;
            }
            catch (SocketException)
            {
                // socket 被关闭或网络中断。
                if (_disposed != 0)
                    break;

                SetState(DataSourceState.Faulted);
                break;
            }

            Interlocked.Increment(
                ref _receivedPackets);

            if (!UdpPacketSerializer.TryDeserialize(
                    result.Buffer,
                    out var packet))
            {
                continue;
            }

            if (packet is null)
            {
                continue;
            }

            Interlocked.Increment(
                ref _validPackets);

            CheckSequence(packet.Sequence);

            var sample = new VehicleSample
            {
                Timestamp = packet.Timestamp,
                Sequence = packet.Sequence,

                SpeedKph = packet.SpeedKph,

                LongitudinalAcceleration =
                    packet.LongitudinalAcceleration,

                LateralAcceleration =
                    packet.LateralAcceleration,

                VerticalAcceleration =
                    packet.VerticalAcceleration,

                YawRate =
                    packet.YawRate,

                Latitude = packet.Latitude,
                Longitude = packet.Longitude,
                Altitude = packet.Altitude,

                Heading = packet.Heading
            };

            _dataBus.TryPublish(sample);
        }

        if (_disposed != 0)
        {
            SetState(DataSourceState.Disconnected);
        }
    }

    /// <summary>
    /// 检查数据包序号连续性。
    /// </summary>
    private void CheckSequence(long sequence)
    {
        if (_lastSequence >= 0)
        {
            if (sequence > _lastSequence + 1)
            {
                var lost = sequence - _lastSequence - 1;

                Interlocked.Add(
                    ref _lostPackets,
                    lost);
            }
            else if (sequence <= _lastSequence)
            {
                Interlocked.Increment(
                    ref _outOfOrderPackets);
            }
        }

        _lastSequence = sequence;
    }

    private void SetState(DataSourceState state)
    {
        if (_state == state)
            return;

        _state = state;
        StateChanged?.Invoke(this, state);
    }

    /// <summary>
    /// 关闭 UDP 接收器。可安全重复调用。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            _udpClient?.Dispose();
        }
        catch
        {
            // ignore dispose races
        }

        // 若接收循环尚未把状态改回 Disconnected，这里先标上。
        // RunAsync 退出时会再设一次（同值则忽略）。
        if (_state is DataSourceState.Connected
            or DataSourceState.Connecting
            or DataSourceState.Reconnecting)
        {
            SetState(DataSourceState.Disconnected);
        }
    }
}
