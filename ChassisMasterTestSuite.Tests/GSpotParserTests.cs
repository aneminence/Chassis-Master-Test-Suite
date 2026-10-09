using System.Text.Json;
using Chassis_Master_Test_Suite.Communication.GSpot;

namespace ChassisMasterTestSuite.Tests;

/// <summary>
/// Pure parser/index mapping — no network.
/// </summary>
public class GSpotParserTests
{
    [Fact]
    public void PropertyIndex_FromPropertiesBody_UsesPosNotArrayOrder()
    {
        // Deliberately unordered array; pos is the authority.
        const string json = """
            [
              { "name": "longitude", "pos": 2 },
              { "name": "serverTime", "pos": 0 },
              { "name": "speed", "pos": 1 },
              { "name": "latitude", "pos": 3 }
            ]
            """;

        using var doc = JsonDocument.Parse(json);
        var index = GSpotPropertyIndex.FromPropertiesBody(doc.RootElement);

        Assert.True(index.TryGetPos("serverTime", out var st));
        Assert.Equal(0, st);
        Assert.True(index.TryGetPos("speed", out var sp));
        Assert.Equal(1, sp);
        Assert.Equal("serverTime,speed,longitude,latitude", index.BuildSubscribePropertiesString());
    }

    [Fact]
    public void TryMap_ReadsFieldsByPos()
    {
        const string propsJson = """
            [
              { "name": "serverTime", "pos": 0 },
              { "name": "speed", "pos": 1 },
              { "name": "latitude", "pos": 2 },
              { "name": "longitude", "pos": 3 },
              { "name": "heading", "pos": 4 },
              { "name": "GUserId", "pos": 5 }
            ]
            """;

        const string bodyJson = """
            [1700000000123, 88.5, 31.23, 121.47, 90.0, "user-1"]
            """;

        using var propsDoc = JsonDocument.Parse(propsJson);
        using var bodyDoc = JsonDocument.Parse(bodyJson);
        var index = GSpotPropertyIndex.FromPropertiesBody(propsDoc.RootElement);

        Assert.True(GSpotParser.TryMap(
            bodyDoc.RootElement,
            index,
            sequence: 7,
            out var sample,
            out var gUserId,
            out _));

        Assert.NotNull(sample);
        Assert.Equal(7, sample!.Sequence);
        Assert.Equal(1700000000123, sample.Timestamp);
        Assert.Equal(88.5, sample.SpeedKph);
        Assert.Equal(31.23, sample.Latitude);
        Assert.Equal(121.47, sample.Longitude);
        Assert.Equal(90.0, sample.Heading);
        Assert.Equal("user-1", gUserId);
    }
}
