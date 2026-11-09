using Chassis_Master_Test_Suite.Communication.Remote;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class ChannelRegistryModeSwitchTests
{
    [Fact]
    public void SetLiveCore_AfterVboColumns_KeepsCatalogSoOrphanPicksCanResolve()
    {
        var reg = ChannelRegistry.Instance;

        reg.SetFromVboColumns(new[] { "velocity", "X_Accel", "Y_Accel", "latacc" });
        Assert.True(reg.IsAvailable("X_Accel"));

        // Online mode narrows Available to live core.
        reg.SetLiveCore();
        Assert.False(reg.IsAvailable("X_Accel"));
        Assert.True(reg.IsAvailable(ChannelIds.Velocity));

        // Catalog must still resolve the VBO channel (CreateChannelControl injects orphan).
        Assert.True(reg.TryGet("X_Accel", out var info));
        Assert.Equal("X_Accel", info.Id, ignoreCase: true);

        // Offline restore brings it back into Available.
        reg.SetFromVboColumns(new[] { "velocity", "X_Accel", "Y_Accel", "latacc" });
        Assert.True(reg.IsAvailable("X_Accel"));
    }

    [Fact]
    public void MergeRemoteKeys_AddsWithoutRemovingLiveCore()
    {
        var registry = ChannelRegistry.Instance;
        registry.SetLiveCore();
        Assert.True(registry.IsAvailable(ChannelIds.Velocity));
        Assert.False(registry.IsAvailable("phone.pressure"));

        registry.MergeRemoteKeys(new[]
        {
            "phone.pressure",
            CMTSRemoteParser.ChannelSatellitesUsed,
            "sats"
        });

        Assert.True(registry.IsAvailable(ChannelIds.Velocity));
        Assert.True(registry.IsAvailable("phone.pressure"));
        Assert.True(registry.IsAvailable(CMTSRemoteParser.ChannelSatellitesUsed));
        Assert.True(registry.IsAvailable("sats"));
    }

    [Fact]
    public void SetFromRemoteKeys_KeepsLiveCore_AndAddsExtras()
    {
        var registry = ChannelRegistry.Instance;
        registry.SetFromRemoteKeys(new[] { "phone.pressure", "vehicle.accel.x" });
        Assert.True(registry.IsAvailable(ChannelIds.Longacc));
        Assert.True(registry.IsAvailable("phone.pressure"));
        Assert.True(registry.IsAvailable("vehicle.accel.x"));
    }
}
