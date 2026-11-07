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
                ["maxHz"] = 20
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
    }
}
