namespace Chassis_Master_Test_Suite.Core;

/// <summary>Header button glyphs for workspace maximize / restore (45° double arrows).</summary>
public static class WorkspaceMaximizeChrome
{
    /// <summary>Outward diagonal arrows — expand panel to fill workspace.</summary>
    public const string ExpandGlyph = "↗↙";

    /// <summary>Inward diagonal arrows — restore previous multi-panel layout.</summary>
    public const string RestoreGlyph = "↙↗";
}

/// <summary>One of the four main workspace regions that can fill the layout alone.</summary>
public enum WorkspacePanelKind
{
    Dashboard,
    TestResults,
    Map,
    Chart
}

/// <summary>Visibility flags matching MainWindow layout show toggles.</summary>
public readonly record struct LayoutVisibilityFlags(
    bool Dashboard,
    bool TestResults,
    bool Map,
    bool Chart);

/// <summary>
/// Maximize one workspace panel by hiding the other three, then restore the
/// previous visibility snapshot. Pure state — no WPF dependency.
/// </summary>
public sealed class WorkspaceMaximizeState
{
    private LayoutVisibilityFlags _saved = new(true, true, true, true);

    public WorkspacePanelKind? MaximizedPanel { get; private set; }

    public bool IsMaximized => MaximizedPanel is not null;

    /// <summary>
    /// Toggle maximize for <paramref name="panel"/>. First maximize snapshots
    /// <paramref name="current"/>; maximize again restores that snapshot.
    /// Switching from another maximized panel keeps the original snapshot.
    /// </summary>
    public LayoutVisibilityFlags Toggle(WorkspacePanelKind panel, LayoutVisibilityFlags current)
    {
        if (MaximizedPanel == panel)
            return Exit();

        if (MaximizedPanel is null)
            _saved = current;

        MaximizedPanel = panel;
        return Only(panel);
    }

    /// <summary>Leave maximize mode and return the saved visibility flags.</summary>
    public LayoutVisibilityFlags Exit()
    {
        MaximizedPanel = null;
        return _saved;
    }

    /// <summary>Drop maximize mode without returning a layout (e.g. Layout Reset).</summary>
    public void Clear(LayoutVisibilityFlags? newSaved = null)
    {
        MaximizedPanel = null;
        _saved = newSaved ?? new LayoutVisibilityFlags(true, true, true, true);
    }

    public static LayoutVisibilityFlags Only(WorkspacePanelKind panel) => panel switch
    {
        WorkspacePanelKind.Dashboard => new(true, false, false, false),
        WorkspacePanelKind.TestResults => new(false, true, false, false),
        WorkspacePanelKind.Map => new(false, false, true, false),
        WorkspacePanelKind.Chart => new(false, false, false, true),
        _ => throw new ArgumentOutOfRangeException(nameof(panel), panel, null)
    };
}