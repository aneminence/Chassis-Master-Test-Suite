using System.Net;
using System.Net.Sockets;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Communication;

/// <summary>
/// CMTS V0.1 UDP 数据接收器。
///
/// 当前监听本机 50000 端口。
/// 同时负责统计 UDP 接收质量。
/// </summary>
public sealed class UdpReceiver
{
    private readonly DataBus _dataBus;
    private readonly UdpClient _udpClient;

    private long _receivedPackets;
    private long _validPackets;
    private long _lostPackets;
    private long _outOfOrderPackets;

    private long _lastSequence = -1;

    public UdpReceiver(
        DataBus dataBus,
        int port = 50000)
    {
        _dataBus = dataBus;
        _udpClient = new UdpClient(port);
    }

    /// <summary>
    /// 接收到的 UDP 数据包总数。
    /// </summary>
    public long ReceivedPackets => _receivedPackets;

    /// <summary>
    /// 成功解析的数据包数量。
    /// </summary>
    public long ValidPackets => _validPackets;

    /// <summary>
    /// 检测到的丢包数量。
    /// </summary>
    public long LostPackets => _lostPackets;

    /// <summary>
    /// 检测到的乱序数据包数量。
    /// </summary>
    public long OutOfOrderPackets => _outOfOrderPackets;

    /// <summary>
    /// 开始接收 UDP 数据。
    ///
    /// 关闭方式：
    /// 调用 Dispose() 关闭底层 socket，
    /// 本循环通过 ObjectDisposedException 退出。
    ///
    /// 这里刻意不使用 CancellationToken：
    /// ReceiveAsync(token) 在取消时会抛 OperationCanceledException，
    /// 而关闭是正常流程，不应该依赖异常来做控制流。
    /// </summary>
    public async Task RunAsync()
    {
        while (true)
        {
            UdpReceiveResult result;

            try
            {
                result = await _udpClient.ReceiveAsync();
            }
            catch (ObjectDisposedException)
            {
                // socket 已被 Dispose() 关闭，正常退出。
                break;
            }
            catch (SocketException)
            {
                // socket 被关闭或网络中断，正常退出。
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

    /// <summary>
    /// 关闭 UDP 接收器。
    /// </summary>
    public void Dispose()
    {
        _udpClient.Dispose();
    }
}