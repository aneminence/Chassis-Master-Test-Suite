using Chassis_Master_Test_Suite.Communication.GSpot;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class GSpotVehicleFilterTests
{
    [Fact]
    public void AcceptAll_PassesEveryVehicle_WithoutLocking()
    {
        string? lockedUser = null;
        string? lockedCNum = null;

        Assert.True(GSpotVehicleFilter.ShouldAccept(
            null, null, acceptAllVehicles: true,
            ref lockedUser, ref lockedCNum, "u1", "1"));
        Assert.True(GSpotVehicleFilter.ShouldAccept(
            null, null, acceptAllVehicles: true,
            ref lockedUser, ref lockedCNum, "u2", "2"));
        Assert.Null(lockedUser);
        Assert.Null(lockedCNum);
    }

    [Fact]
    public void LegacyLockFirst_DropsOtherVehicles()
    {
        string? lockedUser = null;
        string? lockedCNum = null;

        Assert.True(GSpotVehicleFilter.ShouldAccept(
            null, null, acceptAllVehicles: false,
            ref lockedUser, ref lockedCNum, "u1", "1"));
        Assert.Equal("u1", lockedUser);
        Assert.False(GSpotVehicleFilter.ShouldAccept(
            null, null, acceptAllVehicles: false,
            ref lockedUser, ref lockedCNum, "u2", "2"));
        Assert.True(GSpotVehicleFilter.ShouldAccept(
            null, null, acceptAllVehicles: false,
            ref lockedUser, ref lockedCNum, "u1", "1"));
    }

    [Fact]
    public void FilterCNum_OnlyMatchingPasses()
    {
        string? lockedUser = null;
        string? lockedCNum = null;

        Assert.False(GSpotVehicleFilter.ShouldAccept(
            null, "7", acceptAllVehicles: true,
            ref lockedUser, ref lockedCNum, "u1", "1"));
        Assert.True(GSpotVehicleFilter.ShouldAccept(
            null, "7", acceptAllVehicles: true,
            ref lockedUser, ref lockedCNum, "u7", "7"));
        Assert.Equal("7", lockedCNum);
    }
}

public class LiveVehicleRosterTests
{
    private static VehicleSample Sample(string id, string label, double lat, double lon, long ts) =>
        new()
        {
            VehicleId = id,
            VehicleLabel = label,
            Latitude = lat,
            Longitude = lon,
            Timestamp = ts,
            SpeedKph = 50
        };

    [Fact]
    public void Upsert_TracksMultipleVehicles_AndFocus()
    {
        var roster = new LiveVehicleRoster();
        roster.Upsert(Sample("a", "CarA", 31.1, 121.1, 1));
        roster.Upsert(Sample("b", "CarB", 31.2, 121.2, 2));

        Assert.Equal(2, roster.Count);
        Assert.Equal("a", roster.FocusId);

        Assert.True(roster.TrySetFocusByLabel("CarB"));
        Assert.Equal("b", roster.FocusId);
        Assert.Equal("CarB", roster.GetFocusLatest()!.VehicleLabel);

        var snap = roster.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Contains(snap, v => v.IsFocus && v.Id == "b");
        Assert.Contains(snap, v => !v.IsFocus && v.Id == "a");
        Assert.NotEqual(snap[0].ColorHex, snap[1].ColorHex);
    }

    [Fact]
    public void Upsert_KeepsShortTrail()
    {
        var roster = new LiveVehicleRoster();
        for (var i = 0; i < 5; i++)
            roster.Upsert(Sample("a", "A", 31, 121, i), trailCapacity: 3);

        var trail = roster.Snapshot().Single().Trail;
        Assert.Equal(3, trail.Count);
        Assert.Equal(2, trail[0].Timestamp);
        Assert.Equal(4, trail[^1].Timestamp);
    }
}
