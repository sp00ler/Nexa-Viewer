using ViewerPrn.Domain.FileSystem;
using ViewerPrn.Domain.Tabs;

namespace ViewerPrn.Application.Session;

/// <summary>
/// One restored tab: where it pointed, how it was sorted and what was selected
/// (docs/REQUIREMENTS.md:10).
/// </summary>
public sealed record TabState
{
    public required string Path { get; init; }

    public SortCriterion Criterion { get; init; } = SortCriterion.Name;

    public SortDirection Direction { get; init; } = SortDirection.Ascending;

    /// <summary>Names, not full paths — a tab that moved still restores sensibly.</summary>
    public IReadOnlyList<string> SelectedNames { get; init; } = [];

    /// <summary>Folders this tab had expanded in the tree (DECISION-0032).</summary>
    public IReadOnlyList<string> ExpandedTreePaths { get; init; } = [];
}

/// <summary>
/// The window state carried across restarts. Written on every structural change, so an
/// abnormal termination restores the last committed state rather than nothing.
/// </summary>
public sealed record SessionState
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public IReadOnlyList<TabState> Tabs { get; init; } = [];

    /// <summary>Index into <see cref="Tabs"/>, or -1 when nothing was open.</summary>
    public int ActiveIndex { get; init; } = -1;

    public static SessionState Empty { get; } = new();

    /// <summary>
    /// Drops anything a hand-edited or future-version file could contain that this build cannot
    /// honour: more than <see cref="TabSet.MaxTabs"/> tabs, blank paths, an out-of-range active
    /// index.
    /// </summary>
    public SessionState Sanitised()
    {
        List<TabState> tabs = [.. Tabs
            .Where(tab => !string.IsNullOrWhiteSpace(tab.Path))
            .Take(TabSet.MaxTabs)];

        int active = tabs.Count == 0 ? -1 : Math.Clamp(ActiveIndex, 0, tabs.Count - 1);

        return this with { Tabs = tabs, ActiveIndex = active };
    }
}
