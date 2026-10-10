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
}
