using System.Text.Json;
using Chassis_Master_Test_Suite.Communication.Remote;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

/// <summary>
/// Protocol v0.1 parse/mapping — no network / no Android required.
/// </summary>
public class CMTSRemoteParserTests
{
    [Fact]
    public void TryParseEnvelope_ReadsCommonHeader()
    {
        const string json = """
            {
              "v": 1,
              "type": "stream.sample",
              "id": "e1",
              "ts": 1730966400123,
              "deviceId": "dev-1",
              "payload": { "seq": 1 }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        Assert.NotNull(env);
        Assert.Equal(1, env!.V);
        Assert.Equal("stream.sample", env.Type);
        Assert.Equal("e1", env.Id);
        Assert.Equal(1730966400123, env.Ts);
        Assert.Equal("dev-1", env.DeviceId);
    }

    [Fact]
    public void TryMapSample_MapsCoreFields_AndUsesMeasureTime()
    {
        const string json = """
            {
              "v": 1,
              "type": "stream.sample",
              "deviceId": "uuid-abc",
              "payload": {
                "seq": 1042,
                "measureTimeMs": 1730966400100,
                "receiveTimeMs": 1730966400112,
                "vehicleLabel": "Phone-01",
                "speedKph": 15.8,
                "ax": -0.12,
                "ay": 0.05,
                "az": 9.81,
                "yawRateDps": 1.2,
                "lat": 39.9,
                "lon": 116.4,
                "altM": 45.2,
                "headingDeg": 182.0,
                "valid": {
                  "gnss": true,
                  "speed": true,
                  "imu": true,
                  "heading": true
                }
              }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        var result = CMTSRemoteParser.TryMapSample(env!);
        Assert.Null(result.SkipReason);
        Assert.NotNull(result.Sample);
        Assert.Equal(1730966400100, result.Sample!.Timestamp);
        Assert.Equal(1042, result.Sample.Sequence);
        Assert.Equal(15.8, result.Sample.SpeedKph);
        Assert.Equal(39.9, result.Sample.Latitude);
        Assert.Equal(116.4, result.Sample.Longitude);
        Assert.Equal("uuid-abc", result.Sample.VehicleId);
        Assert.Equal("Phone-01", result.Sample.VehicleLabel);
        Assert.Equal(1730966400112, result.ReceiveTimeMs);
        Assert.True(result.Sample.Channels.ContainsKey(ChannelIds.Velocity));
    }

    [Fact]
    public void TryMapSample_SkipsWhenMeasureTimeMissing()
    {
        const string json = """
            {
              "type": "stream.sample",
              "deviceId": "d1",
              "payload": {
                "seq": 1,
                "receiveTimeMs": 99,
                "speedKph": 10.0,
                "valid": { "speed": true }
              }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        var result = CMTSRemoteParser.TryMapSample(env!);
        Assert.Null(result.Sample);
        Assert.Equal("missing_measureTimeMs", result.SkipReason);
    }

    [Fact]
    public void TryMapSample_InvalidFieldsAreNaN_NotSilentZero()
    {
        const string json = """
            {
              "type": "stream.sample",
              "deviceId": "d1",
              "payload": {
                "seq": 7,
                "measureTimeMs": 1000,
                "speedKph": null,
                "lat": 31.2,
                "lon": 121.5,
                "ax": 0.0,
                "valid": {
                  "gnss": true,
                  "speed": false,
                  "imu": false,
                  "heading": false
                }
              }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        var result = CMTSRemoteParser.TryMapSample(env!);
        Assert.NotNull(result.Sample);
        Assert.True(double.IsNaN(result.Sample!.SpeedKph));
        Assert.True(double.IsNaN(result.Sample.LongitudinalAcceleration));
        Assert.Equal(31.2, result.Sample.Latitude);
        Assert.False(result.Sample.Channels.ContainsKey(ChannelIds.Velocity));
        Assert.True(result.Sample.Channels.ContainsKey(ChannelIds.Latitude));
        // ax=0.0 is a legal physical value but valid.imu=false → still invalid
        Assert.False(result.Sample.Channels.ContainsKey(ChannelIds.Longacc));
    }

    [Fact]
    public void ParseHelloAck_AuthFailed()
    {
        const string json = """
            {
              "type": "device.hello",
              "id": "h1",
              "payload": { "ok": false, "errorCode": "auth_failed" }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        var ack = CMTSRemoteParser.ParseHelloAck(env!);
        Assert.False(ack.Ok);
        Assert.Equal("auth_failed", ack.ErrorCode);
    }

    [Fact]
    public void ParseRecordStatus_ReadsState()
    {
        const string json = """
            {
              "type": "record.status",
              "id": "r1",
              "payload": {
                "state": "RECORDING",
                "fileId": "f-9",
                "startedAtMs": 123,
                "ok": true
              }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        var st = CMTSRemoteParser.ParseRecordStatus(env!.Payload);
        Assert.Equal("RECORDING", st.State);
        Assert.Equal("f-9", st.FileId);
        Assert.Equal(123, st.StartedAtMs);
        Assert.True(st.Ok);
    }

    [Fact]
    public void BuildEnvelopeJson_RoundTripsTypeAndId()
    {
        var json = CMTSRemoteParser.BuildEnvelopeJson(
            "stream.subscribe",
            "sub-1",
            new Dictionary<string, object?>
            {
                ["channels"] = new[] { "core" },
                ["maxHz"] = 100
            });

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        Assert.Equal("stream.subscribe", env!.Type);
        Assert.Equal("sub-1", env.Id);
        Assert.Equal(1, env.V);
    }

    [Fact]
    public void Options_BuildWebSocketUrl_DefaultPath()
    {
        var opt = new CMTSRemoteOptions { Host = "192.168.1.10" };
        Assert.Equal("ws://192.168.1.10:8787/cmts/v1", opt.BuildWebSocketUrl());
        Assert.Equal(100, opt.MaxHz);
    }

    [Fact]
    public void TryMapSample_PassThroughAndroidExtras_IntoChannels()
    {
        const string json = """
            {
              "type": "stream.sample",
              "deviceId": "d1",
              "payload": {
                "seq": 3,
                "measureTimeMs": 2000,
                "speedKph": 10.0,
                "ax": 0.1,
                "ay": 0.2,
                "az": 9.8,
                "phone.pressure": 1013.25,
                "vehicle.accel": [1.0, 2.0, 3.0],
                "phone.attitude": {
                  "pitch": 4.5,
                  "roll": -1.2,
                  "azimuth": 90.0,
                  "headingAcc": 2.0
                },
                "valid": { "speed": true, "imu": true }
              }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        var result = CMTSRemoteParser.TryMapSample(env!);
        Assert.NotNull(result.Sample);
        Assert.Equal(1013.25, result.Sample!.Channels["phone.pressure"]);
        Assert.Equal(1.0, result.Sample.Channels["vehicle.accel.x"]);
        Assert.Equal(2.0, result.Sample.Channels["vehicle.accel.y"]);
        Assert.Equal(3.0, result.Sample.Channels["vehicle.accel.z"]);
        Assert.Equal(4.5, result.Sample.Channels["phone.attitude.pitch"]);
        Assert.Equal(-1.2, result.Sample.Channels["phone.attitude.roll"]);
        Assert.Equal(90.0, result.Sample.Channels["phone.attitude.azimuth"]);
        Assert.Equal(2.0, result.Sample.Channels["phone.attitude.headingAcc"]);
        // Core still under ChannelIds, not raw ax
        Assert.True(result.Sample.Channels.ContainsKey(ChannelIds.Longacc));
        Assert.False(result.Sample.Channels.ContainsKey("ax"));
    }

    [Fact]
    public void ParseDeviceStatus_MapsSatellitesToAndroidChannelIds()
    {
        const string json = """
            {
              "type": "device.status",
              "deviceId": "d1",
              "payload": {
                "recordState": "IDLE",
                "gnssFix": true,
                "satellitesVisible": 22,
                "satellitesUsed": 14,
                "serviceAlive": true,
                "running": true
              }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        var st = CMTSRemoteParser.ParseDeviceStatus(env!.Payload);
        Assert.Equal("IDLE", st.RecordState);
        Assert.True(st.GnssFix);
        Assert.Equal(22, st.SatellitesVisible);
        Assert.Equal(14, st.SatellitesUsed);
        Assert.Equal(22, st.Channels[CMTSRemoteParser.ChannelSatellitesVisible]);
        Assert.Equal(14, st.Channels[CMTSRemoteParser.ChannelSatellitesUsed]);
        Assert.Equal(14, st.Channels["sats"]);

        var channels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [ChannelIds.Velocity] = 5
        };
        CMTSRemoteParser.MergeStatusChannels(channels, st);
        Assert.Equal(14, channels["sats"]);
        Assert.Equal(5, channels[ChannelIds.Velocity]);
    }


    [Fact]
    public void TryMapSample_FullAndroidKeyTable_FlattensAllVectors()
    {
        // Mirrors Android Live/CSV sources CaptureService emits (array component order).
        const string json = """
            {
              "type": "stream.sample",
              "deviceId": "d1",
              "payload": {
                "seq": 9,
                "measureTimeMs": 5000,
                "speedKph": 36.0,
                "ax": 0.1,
                "ay": 0.2,
                "az": 9.8,
                "yawRateDps": 3.0,
                "lat": 31.2,
                "lon": 121.5,
                "altM": 12.0,
                "headingDeg": 90.0,
                "phone.pressure": 1008.5,
                "phone.accel": [0.11, 0.22, 9.77],
                "vehicle.accel": [1.1, 2.2, 3.3],
                "phone.gyro": [0.01, 0.02, 0.03],
                "vehicle.gyro": [0.04, 0.05, 0.06],
                "vehicle.attitude": [1.0, 2.0, 3.0],
                "phone.attitude": [4.0, 5.0, 6.0, 1.5],
                "phone.rotation_vector": [0.1, 0.2, 0.3, 0.9, 0.05],
                "phone.gps": [31.2, 121.5, 12.0, 10.0, 90.0, 3.5, 4.5, 0.8],
                "phone.satellites": [22.0, 14.0],
                "valid": {
                  "gnss": true,
                  "speed": true,
                  "imu": true,
                  "heading": true
                }
              }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        var result = CMTSRemoteParser.TryMapSample(env!);
        Assert.NotNull(result.Sample);
        var ch = result.Sample!.Channels;

        Assert.Equal(1008.5, ch["phone.pressure"]);
        Assert.Equal(0.11, ch["phone.accel.x"]);
        Assert.Equal(0.22, ch["phone.accel.y"]);
        Assert.Equal(9.77, ch["phone.accel.z"]);
        Assert.Equal(1.1, ch["vehicle.accel.x"]);
        Assert.Equal(2.2, ch["vehicle.accel.y"]);
        Assert.Equal(3.3, ch["vehicle.accel.z"]);
        Assert.Equal(0.01, ch["phone.gyro.x"]);
        Assert.Equal(0.02, ch["phone.gyro.y"]);
        Assert.Equal(0.03, ch["phone.gyro.z"]);
        Assert.Equal(0.04, ch["vehicle.gyro.x"]);
        Assert.Equal(0.05, ch["vehicle.gyro.y"]);
        Assert.Equal(0.06, ch["vehicle.gyro.z"]);
        Assert.Equal(1.0, ch["vehicle.attitude.roll"]);
        Assert.Equal(2.0, ch["vehicle.attitude.pitch"]);
        Assert.Equal(3.0, ch["vehicle.attitude.yaw"]);
        Assert.Equal(4.0, ch["phone.attitude.pitch"]);
        Assert.Equal(5.0, ch["phone.attitude.roll"]);
        Assert.Equal(6.0, ch["phone.attitude.azimuth"]);
        Assert.Equal(1.5, ch["phone.attitude.headingAcc"]);
        Assert.Equal(0.1, ch["phone.rotation_vector.x"]);
        Assert.Equal(0.2, ch["phone.rotation_vector.y"]);
        Assert.Equal(0.3, ch["phone.rotation_vector.z"]);
        Assert.Equal(0.9, ch["phone.rotation_vector.w"]);
        Assert.Equal(0.05, ch["phone.rotation_vector.headingAcc"]);
        Assert.Equal(31.2, ch["phone.gps.lat"]);
        Assert.Equal(121.5, ch["phone.gps.lon"]);
        Assert.Equal(12.0, ch["phone.gps.alt"]);
        Assert.Equal(10.0, ch["phone.gps.speed"]);
        Assert.Equal(90.0, ch["phone.gps.bearing"]);
        Assert.Equal(3.5, ch["phone.gps.horizAcc"]);
        Assert.Equal(4.5, ch["phone.gps.vertAcc"]);
        Assert.Equal(0.8, ch["phone.gps.speedAcc"]);
        Assert.Equal(22.0, ch["phone.satellites.visible"]);
        Assert.Equal(14.0, ch["phone.satellites.used"]);

        // Core typed ChannelIds still present; raw ax not duplicated.
        Assert.True(ch.ContainsKey(ChannelIds.Velocity));
        Assert.True(ch.ContainsKey(ChannelIds.Longacc));
        Assert.False(ch.ContainsKey("ax"));
    }

    [Fact]
    public void TryMapSample_PhoneGpsArray_SkipsNullComponents()
    {
        const string json = """
            {
              "type": "stream.sample",
              "deviceId": "d1",
              "payload": {
                "seq": 2,
                "measureTimeMs": 100,
                "phone.gps": [31.2, 121.5, null, 5.0, null, 2.0],
                "valid": { "gnss": true, "speed": true }
              }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        var result = CMTSRemoteParser.TryMapSample(env!);
        Assert.NotNull(result.Sample);
        var ch = result.Sample!.Channels;
        Assert.Equal(31.2, ch["phone.gps.lat"]);
        Assert.Equal(121.5, ch["phone.gps.lon"]);
        Assert.False(ch.ContainsKey("phone.gps.alt"));
        Assert.Equal(5.0, ch["phone.gps.speed"]);
        Assert.False(ch.ContainsKey("phone.gps.bearing"));
        Assert.Equal(2.0, ch["phone.gps.horizAcc"]);
    }

    [Fact]
    public void TryMapSample_DoesNotInventMissingAndroidKeys()
    {
        const string json = """
            {
              "type": "stream.sample",
              "deviceId": "d1",
              "payload": {
                "seq": 1,
                "measureTimeMs": 100,
                "speedKph": 1.0,
                "valid": { "speed": true }
              }
            }
            """;

        Assert.True(CMTSRemoteParser.TryParseEnvelope(json, out var env));
        var result = CMTSRemoteParser.TryMapSample(env!);
        Assert.NotNull(result.Sample);
        Assert.False(result.Sample!.Channels.ContainsKey("phone.pressure"));
        Assert.False(result.Sample.Channels.ContainsKey("vehicle.accel.x"));
        Assert.False(result.Sample.Channels.ContainsKey("phone.satellites.used"));
    }

    [Fact]
    public void ParseStreamReset_ReadsMountZeroPayload()
    {
        using var doc = JsonDocument.Parse("""
            {
              "reason": "mount_zero",
              "mountZeroEnabled": true,
              "zeroedAtElapsedNanos": 123456789,
              "nextSeq": 42
            }
            """);
        var info = CMTSRemoteParser.ParseStreamReset(doc.RootElement);
        Assert.Equal(CMTSRemoteParser.StreamResetReasonMountZero, info.Reason);
        Assert.True(info.MountZeroEnabled);
        Assert.Equal(123456789L, info.ZeroedAtElapsedNanos);
        Assert.Equal(42ul, info.NextSeq);
    }

    [Fact]
    public void ParseStreamReset_ReadsClearPayload()
    {
        using var doc = JsonDocument.Parse("""
            {
              "reason": "mount_zero_clear",
              "mountZeroEnabled": false,
              "nextSeq": 100
            }
            """);
        var info = CMTSRemoteParser.ParseStreamReset(doc.RootElement);
        Assert.Equal(CMTSRemoteParser.StreamResetReasonMountZeroClear, info.Reason);
        Assert.False(info.MountZeroEnabled);
        Assert.Null(info.ZeroedAtElapsedNanos);
        Assert.Equal(100ul, info.NextSeq);
    }
}
