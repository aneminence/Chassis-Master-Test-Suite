using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Session;

namespace ChassisMasterTestSuite.Tests;

public class OfflineFileVisibilityTests
{
    [Fact]
    public void NewFiles_DefaultToVisible_AndKeepOpenOrder()
    {
        var files = new OfflineFileSet();
        var first = files.Add("first.vbo", Samples(1), new[] { "velocity" });
        var second = files.Add("second.vbo", Samples(2), new[] { "height" });

        Assert.True(first.IsVisible);
        Assert.True(second.IsVisible);
        Assert.Equal(2, files.VisibleCount);
        Assert.Equal(new[] { first, second }, files.VisibleFiles);
    }

    [Fact]
    public void HideSelectedFile_KeepsSelectionSamplesAndCalculationColumns()
    {
        var files = new OfflineFileSet();
        var first = files.Add("first.vbo", Samples(1), new[] { "velocity" });
        var selectedSamples = Samples(2);
        var selected = files.Add("selected.vbo", selectedSamples, new[] { "height" });
        var live = new SampleHistoryBuffer(2);

        Assert.True(files.SetVisible(selected.Id, false));

        Assert.Equal(selected.Id, files.SelectedId);
        Assert.Same(selected, files.Selected);
        Assert.Same(selected, files.Primary);
        Assert.Same(selectedSamples, selected.Samples);
        Assert.Same(selectedSamples, ActiveSampleHistory.GetSnapshot(true, files, live));
        Assert.Equal(2, files.Count);
        Assert.Equal(new[] { "velocity", "height" }, files.UnionColumns());
        Assert.Equal(new[] { first }, files.VisibleFiles);

        files.Select(first.Id);
        files.Select(selected.Id);
        Assert.Same(selected, files.Selected);
        Assert.False(selected.IsVisible);
    }

    [Fact]
    public void AllHiddenFiles_CanBeShownAgain_WithoutReopeningOrChangingSelection()
    {
        var files = new OfflineFileSet();
        var first = files.Add("first.vbo", Samples(1), Array.Empty<string>(), isVisible: false);
        var second = files.Add("second.vbo", Samples(2), Array.Empty<string>(), isVisible: false);

        Assert.Equal(0, files.VisibleCount);
        Assert.Empty(files.VisibleFiles);
        Assert.Same(second, files.Primary);
        Assert.Equal(2, files.Count);

        Assert.True(files.ToggleVisibility(first.Id));
        Assert.Equal(new[] { first }, files.VisibleFiles);
        Assert.Same(second, files.Selected);
        Assert.True(files.ToggleVisibility(first.Id));
        Assert.Empty(files.VisibleFiles);

        Assert.True(files.SetVisible(second.Id, true));
        Assert.True(files.SetVisible(second.Id, true));
        Assert.Equal(1, files.VisibleCount);
        Assert.Same(second, Assert.Single(files.VisibleFiles));
    }

    [Fact]
    public void HiddenFile_DataAndIdentityUpdates_PreserveVisibility()
    {
        var files = new OfflineFileSet();
        var file = files.Add("original.vbo", Samples(1), new[] { "velocity" }, isVisible: false);
        var replacement = Samples(2);

        Assert.True(files.UpdateData(file.Id, replacement, new[] { "height" }));
        Assert.True(files.UpdateIdentity(file.Id, "replacement.vbo"));
        Assert.True(files.SetColor(file.Id, "#123456"));

        Assert.False(file.IsVisible);
        Assert.Same(replacement, files.Primary!.Samples);
        Assert.Equal("replacement.vbo", file.FilePath);
        Assert.Empty(files.VisibleFiles);
    }

    [Fact]
    public void UnknownOrRemovedFile_VisibilityOperationsDoNotAffectRemainingFiles()
    {
        var files = new OfflineFileSet();
        var first = files.Add("first.vbo", Samples(1), Array.Empty<string>(), isVisible: false);
        var second = files.Add("second.vbo", Samples(2), Array.Empty<string>());
        Assert.True(files.Remove(second.Id));

        Assert.False(files.SetVisible(second.Id, true));
        Assert.False(files.ToggleVisibility(Guid.NewGuid()));
        Assert.Same(first, files.Selected);
        Assert.False(first.IsVisible);

        files.Clear();
        Assert.Empty(files.VisibleFiles);
        Assert.Equal(0, files.VisibleCount);
        Assert.Null(files.Primary);
        Assert.False(files.ToggleVisibility(first.Id));
        Assert.True(files.Add("reopened.vbo", Samples(3), Array.Empty<string>()).IsVisible);
    }

    private static VehicleSample[] Samples(int sequence) =>
        new[] { new VehicleSample { Sequence = sequence, Timestamp = sequence * 10L } };
}
