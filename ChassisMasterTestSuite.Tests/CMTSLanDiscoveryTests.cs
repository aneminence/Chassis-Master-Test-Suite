using System.Text.Json;
using Chassis_Master_Test_Suite.Communication.Remote;
using Xunit;
namespace ChassisMasterTestSuite.Tests;
public class CMTSLanDiscoveryTests
{
    private static string Response(string id = "request", string type = "cmts-android", bool auth = true) => JsonSerializer.Serialize(new {
        v = 1, type = "device.discover", id, deviceId = "stable-id",
        payload = new { discoveryVersion = 1, deviceType = type, deviceLabel = "Phone", protoMin = 1, protoMax = 1, port = 8787, path = "/cmts/v1", authRequired = auth }
    });
    [Fact] public void AcceptsCorrelatedCompatibleIdentity()
    {
        var result = CMTSLanDiscovery.ParseResponse(Response(), "request", "192.168.1.2", 8787, "/cmts/v1");
        Assert.NotNull(result); Assert.Equal("stable-id", result.DeviceId); Assert.Equal("192.168.1.2", result.Host);
    }
    [Theory] [InlineData("other", "cmts-android", true)] [InlineData("request", "other-service", true)]
    public void RejectsUntrustedResponses(string id, string type, bool auth) => Assert.Null(CMTSLanDiscovery.ParseResponse(Response(id,type,auth), "request", "192.168.1.2", 8787, "/cmts/v1"));
    [Theory] [InlineData("{}")] [InlineData("null")] [InlineData("not-json")]
    public void RejectsMalformed(string json) => Assert.Null(CMTSLanDiscovery.ParseResponse(json,"request","host",8787,"/cmts/v1"));
    [Fact] public void DoesNotAcceptRedirectedEndpoint() => Assert.Null(CMTSLanDiscovery.ParseResponse(Response(),"request","host",1234,"/cmts/v1"));
    [Fact] public void OptionalModelIsShownAlongsideLabel()
    {
        var json = Response().Replace("\"deviceLabel\":\"Phone\"", "\"deviceLabel\":\"Phone\",\"deviceModel\":\"Samsung SM-N9860\"");
        var result = CMTSLanDiscovery.ParseResponse(json,"request","host",8787,"/cmts/v1");
        Assert.NotNull(result);
        Assert.Equal("Samsung SM-N9860", result.DisplayName);
        Assert.Contains("Samsung SM-N9860", result.ToString());
    }
    [Fact] public void OlderDevicesKeepTheirLabel()
    {
        var result = CMTSLanDiscovery.ParseResponse(Response(),"request","host",8787,"/cmts/v1");
        Assert.NotNull(result);
        Assert.Null(result.DeviceModel);
        Assert.Equal("Android 手机", result.DisplayName);
    }
    [Fact] public void OpenPersonalDeviceDoesNotRequireCredential()
    {
        var result = CMTSLanDiscovery.ParseResponse(Response(auth:false),"request","host",8787,"/cmts/v1");
        Assert.NotNull(result);
        Assert.False(result.AuthRequired);
    }
}
