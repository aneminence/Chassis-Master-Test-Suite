using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class DashboardLayoutStoreTests
{
    [Fact]
    public void GaugeBinding_RoundTrips_StorageKeys()
    {
        Assert.Equal("live:velocity", GaugeBinding.Live(ChannelIds.Velocity).ToStorageKey());
        Assert.Equal("test:duration", GaugeBinding.TestField(TestResultFieldIds.Duration).ToStorageKey());
        Assert.Equal("pass:velocity@Gate 2", GaugeBinding.Pass("velocity@Gate 2").ToStorageKey());

        var live = GaugeBinding.Parse("live:Longacc");
        Assert.Equal(GaugeBindingKind.LiveChannel, live.Kind);
        Assert.Equal("Longacc", live.Key);

        var test = GaugeBinding.Parse("test:delta_v");
        Assert.Equal(GaugeBindingKind.TestResultField, test.Kind);
        Assert.Equal(TestResultFieldIds.DeltaSpeed, test.Key);

        var pass = GaugeBinding.Parse("pass:velocity@Gate 2");
        Assert.Equal(GaugeBindingKind.PassMeasurement, pass.Kind);
        Assert.Equal("velocity@Gate 2", pass.Key);
    }

    [Fact]
    public void CreateDefaults_HasFiveGauges()
    {
        var defaults = DashboardLayoutStore.CreateDefaults();
        Assert.Equal(5, defaults.Count);
        Assert.Contains(defaults, g => g.BindingKey.Contains("velocity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Store_AddUpdateRemove_Works()
    {
        var store = new DashboardLayoutStore();
        store.ResetToDefaults();
        var before = store.Gauges.Count;

        var g = store.Add(new DashboardGaugeLayout
        {
            Title = "Distance",
            Unit = "m",
            BindingKey = "test:distance",
            X = 10, Y = 10, Width = 120, Height = 80
        });
        Assert.Equal(before + 1, store.Gauges.Count);

        g.Title = "Dist";
        store.Update(g);
        Assert.Equal("Dist", store.Gauges.First(x => x.Id == g.Id).Title);

        Assert.True(store.Remove(g.Id));
        Assert.Equal(before, store.Gauges.Count);
    }

    [Fact]
    public void Layout_BindingAssignment_UpdatesBindingKey_AndTitleIndependently()
    {
        var layout = new DashboardGaugeLayout
        {
            Title = "Speed",
            Unit = "km/h",
            BindingKey = "live:velocity"
        };

        // Simulate picker OK: new binding + title/unit from selected source.
        layout.Binding = GaugeBinding.Live(ChannelIds.Latacc);
        layout.Title = "Lateral Accel";
        layout.Unit = "m/s2";

        Assert.Equal("live:Latacc", layout.BindingKey);
        Assert.Equal(GaugeBindingKind.LiveChannel, layout.Binding.Kind);
        Assert.Equal(ChannelIds.Latacc, layout.Binding.Key);
        Assert.Equal("Lateral Accel", layout.Title);

        layout.Binding = GaugeBinding.TestField(TestResultFieldIds.Duration);
        layout.Title = "Duration";
        layout.Unit = "s";
        Assert.Equal("test:duration", layout.BindingKey);
        Assert.Equal("Duration", layout.Title);
    }

    [Fact]
    public void Store_Update_PersistsBindingKeyChange()
    {
        var store = new DashboardLayoutStore();
        store.ResetToDefaults();
        var g = store.Gauges[0];
        Assert.Contains("velocity", g.BindingKey, StringComparison.OrdinalIgnoreCase);

        g.Binding = GaugeBinding.Live(ChannelIds.Heading);
        g.Title = "Heading";
        g.Unit = "deg";
        store.Update(g);

        var again = store.Gauges.First(x => x.Id == g.Id);
        Assert.Equal("live:heading", again.BindingKey);
        Assert.Equal("Heading", again.Title);
    }
}