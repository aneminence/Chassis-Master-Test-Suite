using Chassis_Master_Test_Suite.Communication;

namespace ChassisMasterTestSuite.Tests;

public class UdpPacketSerializerTests
{
    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsFields()
    {
        var original = new UdpPacket
        {
            Version = 1,
            Sequence = 42,
            Timestamp = 1_700_000_000_123,
            SpeedKph = 123.456,
            LongitudinalAcceleration = 1.1,
            LateralAcceleration = -2.2,
            VerticalAcceleration = 0.5,
            YawRate = 12.34,
            Latitude = 31.2304,
            Longitude = 121.4737,
            Altitude = 12.5,
            Heading = 89.1
        };

        var bytes = UdpPacketSerializer.Serialize(original);
        Assert.Equal(89, bytes.Length);

        Assert.True(UdpPacketSerializer.TryDeserialize(bytes, out var restored));
        Assert.NotNull(restored);
        Assert.Equal(original.Version, restored!.Version);
        Assert.Equal(original.Sequence, restored.Sequence);
        Assert.Equal(original.Timestamp, restored.Timestamp);
        Assert.Equal(original.SpeedKph, restored.SpeedKph);
        Assert.Equal(original.LongitudinalAcceleration, restored.LongitudinalAcceleration);
        Assert.Equal(original.LateralAcceleration, restored.LateralAcceleration);
        Assert.Equal(original.VerticalAcceleration, restored.VerticalAcceleration);
        Assert.Equal(original.YawRate, restored.YawRate);
        Assert.Equal(original.Latitude, restored.Latitude);
        Assert.Equal(original.Longitude, restored.Longitude);
        Assert.Equal(original.Altitude, restored.Altitude);
        Assert.Equal(original.Heading, restored.Heading);
    }

    [Fact]
    public void TryDeserialize_WrongLength_ReturnsFalse()
    {
        Assert.False(UdpPacketSerializer.TryDeserialize(new byte[10], out var packet));
        Assert.Null(packet);
    }
}
