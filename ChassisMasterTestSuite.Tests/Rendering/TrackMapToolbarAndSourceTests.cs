using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chassis_Master_Test_Suite.Controls;
using Chassis_Master_Test_Suite.Core;
using Xunit;

namespace ChassisMasterTestSuite.RenderingTests;

/// <summary>
/// Uses the production XAML and controller logic without creating a Window,
/// initializing WebView2, touching map preferences/cache, or requesting tiles.
/// Layout structure protects against HWND airspace; actual browser hit testing
/// remains an application-level check.
/// </summary>
public class TrackMapToolbarAndSourceTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Type Controller = typeof(TrackMapPanel).Assembly.GetType(
        "Chassis_Master_Test_Suite.Controls.TrackMap3DController", throwOnError: true)!;

    [Fact]
    public async Task ReturningToReadyMap_ClearsLoadingOverlay()
    {
        var controller = RuntimeHelpers.GetUninitializedObject(Controller);
        SetField(controller, "_pageReady", true);
        string? status = "Loading 3D map...";
        Controller.GetEvent("StatusChanged")!.AddEventHandler(controller,
            new Action<string>(message => status = message));
        await (Task)Controller.GetMethod("EnsureInitializedAsync")!.Invoke(controller, null)!;
        Assert.Equal(string.Empty, status);
    }

    [Fact]
    public void ReenteringWhileLoading_ReusesPendingInitialization()
    {
        var controller = RuntimeHelpers.GetUninitializedObject(Controller);
        var pending = new TaskCompletionSource();
        SetField(controller, "_initializationTask", pending.Task);
        Assert.Same(pending.Task, Controller.GetMethod("EnsureInitializedAsync")!.Invoke(controller, null));
    }

    [Fact]
    public void SharedGateToolbar_FloatsInsideMapAboveLegend()
    {
        var document = ReadProductionXaml();
        var toolbar = Named(document, "GateToolbarPanel");
        var surface = Named(document, "MapSurface");
        var browser = Named(document, "Map3DWebView");
        Assert.Contains(surface, toolbar.Ancestors());
        Assert.Contains(surface, browser.Ancestors());
        Assert.Same(toolbar.Parent, Named(document, "GateLegendPanel").Parent);
        Assert.Equal("WebView2CompositionControl", browser.Name.LocalName);
        Assert.Equal("Top", (string?)toolbar.Parent!.Attribute("VerticalAlignment"));

        foreach (var name in new[]
        {
            "AddGateButton", "GateCombo", "GateWidthBox", "RenameGateButton",
            "DeleteGateButton", "DeleteAllGatesButton", "ImportGatesButton", "ExportGatesButton"
        })
        {
            var control = Named(document, name);
            Assert.Contains(toolbar, control.Ancestors());
            AssertOutsideModeSpecificHosts(control);
            Assert.NotEqual("False", (string?)control.Attribute("IsEnabled"));
        }
    }

    [Fact]
    public void FitButton_IsSharedByBothMapModes_AndHasItsClickHandler()
    {
        var document = ReadProductionXaml();
        var fit = Named(document, "FitButton");
        Assert.Equal(Wpf + "Button", fit.Name);
        Assert.Equal("FitButton_Click", (string?)fit.Attribute("Click"));
        AssertOutsideModeSpecificHosts(fit);
        Assert.DoesNotContain(Named(document, "MapSurface"), fit.Ancestors());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SceneTrack_PreservesHiddenGeometryAndOriginalSampleIndices(bool visible)
    {
        var samples = new[]
        {
            Sample(0, double.NaN), Sample(1, 31.0), Sample(2, 31.001),
            Sample(3, double.NaN), Sample(4, 31.002)
        };
        var track = new TrackMapPanel.TrackLayer(
            "selected", "#42A5F5", samples, IsVisible: visible, SourceIndex: 7);
        var build = Controller.GetMethod("BuildTrack", BindingFlags.NonPublic | BindingFlags.Static)!;

        var dto = build.Invoke(null, new object[] { track, 2 })!;

        // Scene IDs are local transport IDs. They must not be confused with the
        // original file SourceIndex used by the host's selection callback.
        Assert.Equal(2, Property<int>(dto, "Id"));
        Assert.Equal(visible, Property<bool>(dto, "IsVisible"));
        var points = Property<List<double[]>>(dto, "Points");
        Assert.Equal(new double[] { 1, 2, 4 }, points.Select(point => point[3]));
        Assert.Equal(samples[1].Longitude, points[0][0]);
        Assert.Equal(samples[4].Latitude, points[^1][1]);
        Assert.Equal(7, track.SourceIndex);
        Assert.Equal(visible, track.IsVisible);
    }

    [Fact]
    public void SelectedSceneTrack_UsesOriginalSourceIndex_EvenWhenHiddenOrReordered()
    {
        // This getter depends only on fields. Avoid constructing its WebView or
        // TrackMapPanel (which subscribe to process-wide UI events and caches).
        var controller = RuntimeHelpers.GetUninitializedObject(Controller);
        var hidden = new TrackMapPanel.TrackLayer(
            "hidden selected", "#42A5F5", new[] { Sample(1, 31.0), Sample(2, 31.001) },
            IsVisible: false, SourceIndex: 7);
        var other = new TrackMapPanel.TrackLayer(
            "other", "#E05252", new[] { Sample(3, 32.0), Sample(4, 32.001) }, SourceIndex: 3);
        SetField(controller, "_tracks", new[] { other, hidden });
        SetField(controller, "_selectedTrackSourceIndex", (int?)7);

        Assert.Equal(1, SelectedSceneId(controller));
        Assert.False(hidden.IsVisible);

        SetField(controller, "_tracks", new[] { hidden, other });
        Assert.Equal(0, SelectedSceneId(controller));

        // A removed/empty filtered source must not fit an unrelated first file.
        SetField(controller, "_tracks", new[] { other });
        Assert.Equal(-1, SelectedSceneId(controller));
    }

    [Fact]
    public void HiddenTrackCursor_IsSuppressed_WhileVisibleCursorKeepsSceneAndSampleIndices()
    {
        var controller = RuntimeHelpers.GetUninitializedObject(Controller);
        var hiddenSamples = new[] { Sample(8, 31.0), Sample(9, 31.001) };
        var visibleSamples = new[] { Sample(10, 32.0), Sample(11, 32.001) };
        SetField(controller, "_tracks", new[]
        {
            new TrackMapPanel.TrackLayer("hidden", "#42A5F5", hiddenSamples, IsVisible: false, SourceIndex: 7),
            new TrackMapPanel.TrackLayer("visible", "#E05252", visibleSamples, SourceIndex: 9)
        });
        var cacheField = Controller.GetField("_sampleLocations", BindingFlags.NonPublic | BindingFlags.Instance)!;
        cacheField.SetValue(controller, Activator.CreateInstance(cacheField.FieldType));
        var toCursor = Controller.GetMethod("ToCursorPoint", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.Null(toCursor.Invoke(controller, new object?[] { hiddenSamples[1], null, null }));
        var visibleCursor = toCursor.Invoke(controller, new object?[] { visibleSamples[1], null, null });
        Assert.NotNull(visibleCursor);
        Assert.Equal(1, Property<int>(visibleCursor, "trackId"));
        Assert.Equal(1, Property<int>(visibleCursor, "sampleIndex"));
    }

    private static int SelectedSceneId(object controller) =>
        (int)Controller.GetProperty("SelectedTrackId", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

    private static void SetField(object instance, string name, object value) =>
        Controller.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, value);

    private static T Property<T>(object instance, string name) =>
        (T)instance.GetType().GetProperty(name)!.GetValue(instance)!;

    private static VehicleSample Sample(int index, double latitude) => new()
    {
        Sequence = index, Timestamp = index * 10L,
        Latitude = latitude, Longitude = 121.0, Altitude = 20 + index
    };

    private static XDocument ReadProductionXaml()
    {
        using var stream = typeof(TrackMapToolbarAndSourceTests).Assembly
            .GetManifestResourceStream("TrackMapPanel.Source.xaml");
        Assert.NotNull(stream);
        return XDocument.Load(stream);
    }

    private static XElement Named(XDocument document, string name) =>
        Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == name);

    private static int GridRow(XElement element) => (int?)element.Attribute("Grid.Row") ?? 0;

    private static void AssertOutsideModeSpecificHosts(XElement element)
    {
        foreach (var parent in element.AncestorsAndSelf())
        {
            var name = (string?)parent.Attribute(Xaml + "Name");
            Assert.DoesNotContain(name, new[] { "Map2DHost", "Map3DHost", "Map2DToolbar", "Map3DToolbar" });
            Assert.NotEqual("Collapsed", (string?)parent.Attribute("Visibility"));
            Assert.NotEqual("Hidden", (string?)parent.Attribute("Visibility"));
        }
    }
}
