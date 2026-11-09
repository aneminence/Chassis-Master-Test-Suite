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

    [Fact]
    public void SetLiveCore_AfterMergeRemoteKeys_WipesExtras_UntilRemerged()
    {
        // Mirrors Remote connect race: samples MergeRemoteKeys, then UI EnterLiveSourceMode
        // calls SetLiveCore and must re-merge or Curves picker stays LiveCore-only.
        var registry = ChannelRegistry.Instance;
        registry.SetLiveCore();
        registry.MergeRemoteKeys(new[]
        {
            "phone.pressure",
            CMTSRemoteParser.ChannelSatellitesUsed,
            "sats"
        });
        Assert.True(registry.IsAvailable("phone.pressure"));
        Assert.True(registry.IsAvailable("sats"));

        registry.SetLiveCore();
        Assert.False(registry.IsAvailable("phone.pressure"));
        Assert.False(registry.IsAvailable("sats"));
        Assert.True(registry.IsAvailable(ChannelIds.Velocity));

        registry.MergeRemoteKeys(new[]
        {
            "phone.pressure",
            CMTSRemoteParser.ChannelSatellitesUsed,
            "sats"
        });
        Assert.True(registry.IsAvailable("phone.pressure"));
        Assert.True(registry.IsAvailable(CMTSRemoteParser.ChannelSatellitesUsed));
        Assert.True(registry.IsAvailable("sats"));
    }

    [Fact]
    public void MergeRemoteKeys_FullAndroidTable_UsesCatalogUnits()
    {
        var registry = ChannelRegistry.Instance;
        registry.SetLiveCore();

        var keys = new[]
        {
            "phone.pressure",
            "phone.accel.x", "phone.accel.y", "phone.accel.z",
            "vehicle.accel.x", "vehicle.accel.y", "vehicle.accel.z",
            "phone.gyro.x", "vehicle.gyro.z",
            "vehicle.attitude.roll", "vehicle.attitude.pitch", "vehicle.attitude.yaw",
            "phone.attitude.pitch", "phone.attitude.roll", "phone.attitude.azimuth",
            "phone.rotation_vector.x", "phone.rotation_vector.w", "phone.rotation_vector.headingAcc",
            "phone.gps.lat", "phone.gps.lon", "phone.gps.alt", "phone.gps.speed",
            "phone.gps.bearing", "phone.gps.horizAcc", "phone.gps.vertAcc", "phone.gps.speedAcc",
            "phone.satellites.visible", "phone.satellites.used", "sats"
        };

        registry.MergeRemoteKeys(keys);

        Assert.True(registry.IsAvailable(ChannelIds.Velocity));
        foreach (var key in keys)
            Assert.True(registry.IsAvailable(key), key);

        Assert.True(registry.TryGet("phone.gps.speed", out var gpsSpeed));
        Assert.Equal("m/s", gpsSpeed.Unit);
        Assert.True(registry.TryGet("phone.gps.horizAcc", out var horiz));
        Assert.Equal("m", horiz.Unit);
        Assert.True(registry.TryGet("phone.rotation_vector.headingAcc", out var rvAcc));
        Assert.Equal("rad", rvAcc.Unit);
        Assert.True(registry.TryGet("vehicle.accel.x", out var vac));
        Assert.Equal("m/s2", vac.Unit);
        Assert.True(registry.TryGet("phone.pressure", out var prs));
        Assert.Equal("mbar", prs.Unit);
    }
}
