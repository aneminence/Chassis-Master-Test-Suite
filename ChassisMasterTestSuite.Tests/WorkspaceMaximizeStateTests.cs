using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class WorkspaceMaximizeStateTests
{
    private static LayoutVisibilityFlags AllOn => new(true, true, true, true);
    private static LayoutVisibilityFlags OnlyChart => new(false, false, false, true);

    [Fact]
    public void Toggle_MaximizesOnePanel_AndRestoresSnapshot()
    {
        var state = new WorkspaceMaximizeState();
        var before = new LayoutVisibilityFlags(true, false, true, true);

        var max = state.Toggle(WorkspacePanelKind.Map, before);
        Assert.True(state.IsMaximized);
        Assert.Equal(WorkspacePanelKind.Map, state.MaximizedPanel);
        Assert.Equal(new LayoutVisibilityFlags(false, false, true, false), max);

        var restored = state.Toggle(WorkspacePanelKind.Map, max);
        Assert.False(state.IsMaximized);
        Assert.Null(state.MaximizedPanel);
        Assert.Equal(before, restored);
    }

    [Fact]
    public void Toggle_SwitchPanel_KeepsOriginalSnapshot()
    {
        var state = new WorkspaceMaximizeState();
        var before = new LayoutVisibilityFlags(true, true, false, true);

        state.Toggle(WorkspacePanelKind.Dashboard, before);
        var chart = state.Toggle(WorkspacePanelKind.Chart, WorkspaceMaximizeState.Only(WorkspacePanelKind.Dashboard));
        Assert.Equal(OnlyChart, chart);
        Assert.Equal(WorkspacePanelKind.Chart, state.MaximizedPanel);

        var restored = state.Exit();
        Assert.Equal(before, restored);
        Assert.False(state.IsMaximized);
    }

    [Fact]
    public void Clear_DropsMaximize_WithoutChangingCallerFlags()
    {
        var state = new WorkspaceMaximizeState();
        state.Toggle(WorkspacePanelKind.TestResults, AllOn);
        Assert.True(state.IsMaximized);

        state.Clear();
        Assert.False(state.IsMaximized);
        Assert.Null(state.MaximizedPanel);
    }

    [Fact]
    public void Only_ReturnsSinglePanelFlags()
    {
        Assert.Equal(new LayoutVisibilityFlags(true, false, false, false),
            WorkspaceMaximizeState.Only(WorkspacePanelKind.Dashboard));
        Assert.Equal(new LayoutVisibilityFlags(false, true, false, false),
            WorkspaceMaximizeState.Only(WorkspacePanelKind.TestResults));
        Assert.Equal(new LayoutVisibilityFlags(false, false, true, false),
            WorkspaceMaximizeState.Only(WorkspacePanelKind.Map));
        Assert.Equal(OnlyChart, WorkspaceMaximizeState.Only(WorkspacePanelKind.Chart));
    }
}
