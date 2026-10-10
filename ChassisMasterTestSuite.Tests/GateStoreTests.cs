using Chassis_Master_Test_Suite.Analysis;

namespace ChassisMasterTestSuite.Tests;

public class GateStoreTests
{
    [Fact]
    public void Add_SelectsAndAutoAssignsAnalysisStartEnd()
    {
        var store = new GateStore(); // use Instance carefully — better test Instance with Clear
        GateStore.Instance.Clear();

        var a = GateStore.Instance.Add(new GateDefinition
        {
            Name = "开始",
            Latitude = 31,
            Longitude = 121,
            WidthMeters = 20,
            HeadingDeg = 0
        });
        var b = GateStore.Instance.Add(new GateDefinition
        {
            Name = "麋鹿测试点",
            Latitude = 31.001,
            Longitude = 121,
            WidthMeters = 25,
            HeadingDeg = 0
        });

        Assert.Equal(2, GateStore.Instance.Gates.Count);
        Assert.Equal(a.Id, GateStore.Instance.AnalysisStartId);
        Assert.Equal(b.Id, GateStore.Instance.AnalysisEndId);
        Assert.Equal(b.Id, GateStore.Instance.SelectedId);
        Assert.False(string.Equals(a.ColorHex, b.ColorHex, StringComparison.OrdinalIgnoreCase));

        Assert.True(GateStore.Instance.Rename(b.Id, "麋鹿"));
        Assert.Equal("麋鹿", GateStore.Instance.Find(b.Id)!.Name);

        Assert.True(GateStore.Instance.SetWidth(a.Id, 30));
        Assert.Equal(30, GateStore.Instance.Find(a.Id)!.WidthMeters);

        GateStore.Instance.Remove(a.Id);
        Assert.Null(GateStore.Instance.AnalysisStartId);
        Assert.Single(GateStore.Instance.Gates);

        GateStore.Instance.Clear();
        Assert.Empty(GateStore.Instance.Gates);
    }
}
