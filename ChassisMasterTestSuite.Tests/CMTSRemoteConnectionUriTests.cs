using Chassis_Master_Test_Suite.Communication.Remote;

namespace ChassisMasterTestSuite.Tests;

public class CMTSRemoteConnectionUriTests
{
    [Fact]
    public void TryParse_FullCmtsUri_FillsAllFields()
    {
        var ok = CMTSRemoteConnectionUri.TryParse(
            "cmts://10.11.6.214:8787/cmts/v1?token=72e3a38515d0",
            out var parsed);

        Assert.True(ok);
        Assert.Equal(CMTSRemoteConnectionUri.ConnectionPasteKind.FullUri, parsed.Kind);
        Assert.Equal("10.11.6.214", parsed.Host);
        Assert.Equal(8787, parsed.Port);
        Assert.Equal("/cmts/v1", parsed.Path);
        Assert.Equal("72e3a38515d0", parsed.Token);
    }

    [Fact]
    public void TryParse_FullCmtsUri_CustomPortAndPath()
    {
        var ok = CMTSRemoteConnectionUri.TryParse(
            "cmts://192.168.1.10:9000/custom/path?token=abcDEF12",
            out var parsed);

        Assert.True(ok);
        Assert.Equal("192.168.1.10", parsed.Host);
        Assert.Equal(9000, parsed.Port);
        Assert.Equal("/custom/path", parsed.Path);
        Assert.Equal("abcDEF12", parsed.Token);
    }

    [Fact]
    public void TryParse_CmtsUri_MissingPort_UsesDefault()
    {
        var ok = CMTSRemoteConnectionUri.TryParse(
            "cmts://10.0.0.5/cmts/v1?token=tok12345",
            out var parsed);

        Assert.True(ok);
        Assert.Equal("10.0.0.5", parsed.Host);
        Assert.Equal(CMTSRemoteConnectionUri.DefaultPort, parsed.Port);
        Assert.Equal("/cmts/v1", parsed.Path);
        Assert.Equal("tok12345", parsed.Token);
    }

    [Fact]
    public void TryParse_IpOnly()
    {
        var ok = CMTSRemoteConnectionUri.TryParse("10.11.6.214", out var parsed);
        Assert.True(ok);
        Assert.Equal(CMTSRemoteConnectionUri.ConnectionPasteKind.HostOnly, parsed.Kind);
        Assert.Equal("10.11.6.214", parsed.Host);
        Assert.Null(parsed.Port);
        Assert.Null(parsed.Path);
        Assert.Null(parsed.Token);
    }

    [Fact]
    public void TryParse_TokenOnly()
    {
        var ok = CMTSRemoteConnectionUri.TryParse("72e3a38515d0", out var parsed);
        Assert.True(ok);
        Assert.Equal(CMTSRemoteConnectionUri.ConnectionPasteKind.TokenOnly, parsed.Kind);
        Assert.Null(parsed.Host);
        Assert.Equal("72e3a38515d0", parsed.Token);
    }

    [Fact]
    public void TryParse_TokenEqualsForm()
    {
        var ok = CMTSRemoteConnectionUri.TryParse("token=72e3a38515d0", out var parsed);
        Assert.True(ok);
        Assert.Equal(CMTSRemoteConnectionUri.ConnectionPasteKind.TokenOnly, parsed.Kind);
        Assert.Equal("72e3a38515d0", parsed.Token);
    }

    [Fact]
    public void TryParse_WhitespaceAndQuotes_Stripped()
    {
        var ok = CMTSRemoteConnectionUri.TryParse(
            "  \"cmts://10.11.6.214:8787/cmts/v1?token=72e3a38515d0\" \n",
            out var parsed);
        Assert.True(ok);
        Assert.Equal("10.11.6.214", parsed.Host);
        Assert.Equal("72e3a38515d0", parsed.Token);
    }

    [Fact]
    public void TryParse_Empty_ReturnsFalse()
    {
        Assert.False(CMTSRemoteConnectionUri.TryParse("   ", out var parsed));
        Assert.Equal(CMTSRemoteConnectionUri.ConnectionPasteKind.Empty, parsed.Kind);
    }

    [Fact]
    public void TryParse_InvalidScheme_ReturnsFalse()
    {
        Assert.False(CMTSRemoteConnectionUri.TryParse("http://10.0.0.1/x", out var parsed));
        Assert.Equal(CMTSRemoteConnectionUri.ConnectionPasteKind.Invalid, parsed.Kind);
    }

    [Fact]
    public void ApplyTo_OnlyOverwritesPresentParts()
    {
        var host = "old-host";
        var port = "1111";
        var path = "/old";
        var token = "old-token";

        Assert.True(CMTSRemoteConnectionUri.TryParse("10.11.6.214", out var ipOnly));
        CMTSRemoteConnectionUri.ApplyTo(ipOnly, ref host, ref port, ref path, ref token);
        Assert.Equal("10.11.6.214", host);
        Assert.Equal("1111", port);
        Assert.Equal("/old", path);
        Assert.Equal("old-token", token);

        Assert.True(CMTSRemoteConnectionUri.TryParse("72e3a38515d0", out var tokOnly));
        CMTSRemoteConnectionUri.ApplyTo(tokOnly, ref host, ref port, ref path, ref token);
        Assert.Equal("10.11.6.214", host);
        Assert.Equal("72e3a38515d0", token);

        Assert.True(CMTSRemoteConnectionUri.TryParse(
            "cmts://1.2.3.4:8787/cmts/v1?token=newtok", out var full));
        CMTSRemoteConnectionUri.ApplyTo(full, ref host, ref port, ref path, ref token);
        Assert.Equal("1.2.3.4", host);
        Assert.Equal("8787", port);
        Assert.Equal("/cmts/v1", path);
        Assert.Equal("newtok", token);
    }
}
