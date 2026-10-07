using System.Net.Sockets;

namespace Chassis_Master_Test_Suite.Communication;

/// <summary>
/// CMTS V0.1 UDP 数据发送器。
/// 当前用于 Simulator 向本机 UDP 端口发送模拟车辆数据。
/// </summary>
public sealed class UdpSender : IDisposable
{
    private readonly UdpClient _udpClient;
    private readonly string _host;
    private readonly int _port;

    public UdpSender(
        string host = "127.0.0.1",
        int port = 50000)
    {
        _host = host;
        _port = port;

        _udpClient = new UdpClient();
    }

    /// <summary>
    /// 发送一个 UDP 数据包。
    /// </summary>
    public async Task SendAsync(
        UdpPacket packet,
        CancellationToken cancellationToken)
    {
        var data = UdpPacketSerializer.Serialize(packet);

        await _udpClient.SendAsync(
            data,
            data.Length,
            _host,
            _port);
    }

    /// <summary>
    /// 关闭 UDP 发送器。
    /// </summary>
    public void Dispose()
    {
        _udpClient.Dispose();
    }
}