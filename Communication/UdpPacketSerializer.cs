using System.Buffers.Binary;

namespace Chassis_Master_Test_Suite.Communication;

/// <summary>
/// CMTS V0.1 UDP 数据包序列化器。
///
/// 当前采用固定长度的小端二进制格式，
/// 用于 Simulator 与 CMTS 之间的内部测试通信。
/// </summary>
public static class UdpPacketSerializer
{
    private const int PacketSize = 89;

    /// <summary>
    /// 将 UdpPacket 序列化为二进制数据。
    /// </summary>
    public static byte[] Serialize(UdpPacket packet)
    {
        var buffer = new byte[PacketSize];

        var offset = 0;

        // Version
        buffer[offset++] = packet.Version;

        // Sequence
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(offset, 8),
            packet.Sequence);
        offset += 8;

        // Timestamp
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(offset, 8),
            packet.Timestamp);
        offset += 8;

        // Speed
        WriteDouble(buffer, ref offset, packet.SpeedKph);

        // Longitudinal Acceleration
        WriteDouble(buffer, ref offset, packet.LongitudinalAcceleration);

        // Lateral Acceleration
        WriteDouble(buffer, ref offset, packet.LateralAcceleration);

        // Vertical Acceleration
        WriteDouble(buffer, ref offset, packet.VerticalAcceleration);

        // Yaw Rate
        WriteDouble(buffer, ref offset, packet.YawRate);

        // Latitude
        WriteDouble(buffer, ref offset, packet.Latitude);

        // Longitude
        WriteDouble(buffer, ref offset, packet.Longitude);

        // Altitude
        WriteDouble(buffer, ref offset, packet.Altitude);

        // Heading
        WriteDouble(buffer, ref offset, packet.Heading);

        return buffer;
    }

    /// <summary>
    /// 将二进制数据解析为 UdpPacket。
    /// </summary>
    public static bool TryDeserialize(
        ReadOnlySpan<byte> data,
        out UdpPacket? packet)
    {
        packet = null;

        if (data.Length != PacketSize)
        {
            return false;
        }

        var offset = 0;

        var version = data[offset++];

        var sequence = BinaryPrimitives.ReadInt64LittleEndian(
            data.Slice(offset, 8));
        offset += 8;

        var timestamp = BinaryPrimitives.ReadInt64LittleEndian(
            data.Slice(offset, 8));
        offset += 8;

        var speed = ReadDouble(data, ref offset);
        var longitudinalAcceleration = ReadDouble(data, ref offset);
        var lateralAcceleration = ReadDouble(data, ref offset);
        var verticalAcceleration = ReadDouble(data, ref offset);
        var yawRate = ReadDouble(data, ref offset);
        var latitude = ReadDouble(data, ref offset);
        var longitude = ReadDouble(data, ref offset);
        var altitude = ReadDouble(data, ref offset);
        var heading = ReadDouble(data, ref offset);

        packet = new UdpPacket
        {
            Version = version,
            Sequence = sequence,
            Timestamp = timestamp,
            SpeedKph = speed,
            LongitudinalAcceleration = longitudinalAcceleration,
            LateralAcceleration = lateralAcceleration,
            VerticalAcceleration = verticalAcceleration,
            YawRate = yawRate,
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            Heading = heading
        };

        return true;
    }

    private static void WriteDouble(
        byte[] buffer,
        ref int offset,
        double value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(offset, 8),
            BitConverter.DoubleToInt64Bits(value));

        offset += 8;
    }

    private static double ReadDouble(
        ReadOnlySpan<byte> data,
        ref int offset)
    {
        var bits = BinaryPrimitives.ReadInt64LittleEndian(
            data.Slice(offset, 8));

        offset += 8;

        return BitConverter.Int64BitsToDouble(bits);
    }
}