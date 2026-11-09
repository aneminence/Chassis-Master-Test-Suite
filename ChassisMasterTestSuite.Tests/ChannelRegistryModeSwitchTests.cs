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
    public void MergeRemoteKeys_AddsVehicle_HidesPhoneAndSats()
    {
        var registry = ChannelRegistry.Instance;
        registry.SetLiveCore();
        Assert.True(registry.IsAvailable(ChannelIds.Velocity));
        Assert.False(registry.IsAvailable("phone.pressure"));
        Assert.False(registry.IsAvailable("vehicle.accel.x"));

        registry.MergeRemoteKeys(new[]
        {
            "phone.pressure",
            "vehicle.accel.x",
            "vehicle.gyro.z",
            CMTSRemoteParser.ChannelSatellitesUsed,
            "sats"
        });

        Assert.True(registry.IsAvailable(ChannelIds.Velocity));
        Assert.True(registry.IsAvailable("vehicle.accel.x"));
        Assert.True(registry.IsAvailable("vehicle.gyro.z"));
        Assert.False(registry.IsAvailable("phone.pressure"));
        Assert.False(registry.IsAvailable(CMTSRemoteParser.ChannelSatellitesUsed));
        Assert.False(registry.IsAvailable("sats"));
    }

    [Fact]
    public void SetFromRemoteKeys_KeepsLiveCore_AddsVehicle_HidesPhone()
    {
        var registry = ChannelRegistry.Instance;
        registry.SetFromRemoteKeys(new[] { "phone.pressure", "vehicle.accel.x" });
        Assert.True(registry.IsAvailable(ChannelIds.Longacc));
        Assert.True(registry.IsAvailable("vehicle.accel.x"));
        Assert.False(registry.IsAvailable("phone.pressure"));
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
            "vehicle.accel.x",
            "vehicle.attitude.yaw",
            "phone.pressure",
            "sats"
        });
        Assert.True(registry.IsAvailable("vehicle.accel.x"));
        Assert.True(registry.IsAvailable("vehicle.attitude.yaw"));
        Assert.False(registry.IsAvailable("phone.pressure"));
        Assert.False(registry.IsAvailable("sats"));

        registry.SetLiveCore();
        Assert.False(registry.IsAvailable("vehicle.accel.x"));
        Assert.True(registry.IsAvailable(ChannelIds.Velocity));

        registry.MergeRemoteKeys(new[]
        {
            "vehicle.accel.x",
            "vehicle.attitude.yaw",
            "phone.pressure",
            "sats"
        });
        Assert.True(registry.IsAvailable("vehicle.accel.x"));
        Assert.True(registry.IsAvailable("vehicle.attitude.yaw"));
        Assert.False(registry.IsAvailable("phone.pressure"));
        Assert.False(registry.IsAvailable("sats"));
    }

    [Fact]
    public void MergeRemoteKeys_VehicleKeys_UseCatalogUnits_PhoneStayHidden()
    {
        var registry = ChannelRegistry.Instance;
        registry.SetLiveCore();

        var vehicleKeys = new[]
        {
            "vehicle.accel.x", "vehicle.accel.y", "vehicle.accel.z",
            "vehicle.gyro.x", "vehicle.gyro.y", "vehicle.gyro.z",
            "vehicle.attitude.roll", "vehicle.attitude.pitch", "vehicle.attitude.yaw",
        };
        var phoneKeys = new[]
        {
            "phone.pressure",
            "phone.accel.x", "phone.accel.y", "phone.accel.z",
            "phone.gyro.x",
            "phone.attitude.pitch",
            "phone.rotation_vector.x", "phone.rotation_vector.headingAcc",
            "phone.gps.lat", "phone.gps.speed", "phone.gps.horizAcc",
            "phone.satellites.visible", "phone.satellites.used", "sats"
        };

        registry.MergeRemoteKeys(vehicleKeys.Concat(phoneKeys));

        Assert.True(registry.IsAvailable(ChannelIds.Velocity));
        foreach (var key in vehicleKeys)
            Assert.True(registry.IsAvailable(key), key);
        foreach (var key in phoneKeys)
            Assert.False(registry.IsAvailable(key), key);

        Assert.True(registry.TryGet("vehicle.accel.x", out var vac));
        Assert.Equal("m/s2", vac.Unit);
        Assert.True(registry.TryGet("vehicle.attitude.yaw", out var yaw));
        Assert.Equal("deg", yaw.Unit);
    }

    [Fact]
    public void IsCurvesAvailableKey_HidesPhoneAndSats()
    {
        Assert.True(CMTSRemoteParser.IsCurvesAvailableKey("vehicle.accel.x"));
        Assert.True(CMTSRemoteParser.IsCurvesAvailableKey("velocity"));
        Assert.False(CMTSRemoteParser.IsCurvesAvailableKey("phone.pressure"));
        Assert.False(CMTSRemoteParser.IsCurvesAvailableKey("phone.satellites.used"));
        Assert.False(CMTSRemoteParser.IsCurvesAvailableKey("sats"));
        Assert.False(CMTSRemoteParser.IsCurvesAvailableKey(null));
        Assert.False(CMTSRemoteParser.IsCurvesAvailableKey(""));
    }
}
