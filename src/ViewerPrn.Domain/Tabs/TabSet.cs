namespace ViewerPrn.Domain.Tabs;

/// <summary>
/// The ordered set of open tabs and which one is active (docs/REQUIREMENTS.md:10).
/// Pure state: no I/O, no eager loading. Persistence arrives in Phase 3.
/// </summary>
public sealed class TabSet
{
    /// <summary>Hard limit from docs/REQUIREMENTS.md:10.</summary>
    public const int MaxTabs = 25;

    private readonly List<TabDescriptor> _tabs = [];

    public IReadOnlyList<TabDescriptor> Tabs => _tabs;

    public int Count => _tabs.Count;

    /// <summary>Index of the active tab, or -1 when no tab is open.</summary>
    public int ActiveIndex { get; private set; } = -1;

    public TabDescriptor? Active => ActiveIndex >= 0 ? _tabs[ActiveIndex] : null;

    public bool CanOpen => _tabs.Count < MaxTabs;

    /// <summary>
    /// Opens a tab and makes it active. Throws when the limit is reached — callers disable
    /// the command via <see cref="CanOpen"/> instead of relying on the exception.
    /// </summary>
    public TabDescriptor Open(string path, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (!CanOpen)
        {
            throw new InvalidOperationException($"Tab limit of {MaxTabs} reached.");
        }

        TabDescriptor tab = new(Guid.NewGuid(), path, title);
        _tabs.Add(tab);
        ActiveIndex = _tabs.Count - 1;
        return tab;
    }

    /// <summary>
    /// Closes a tab. When the closed tab was active, focus moves to the tab on the right,
    /// or to the one on the left when there is none — the familiar Windows behaviour
    /// (docs/REQUIREMENTS.md priority 6). See DECISION-0013.
    /// </summary>
    public void Close(int index)
    {
        ValidateIndex(index);
        _tabs.RemoveAt(index);

        if (_tabs.Count == 0)
        {
            ActiveIndex = -1;
            return;
        }

        if (index < ActiveIndex)
        {
            ActiveIndex--;
        }
        else if (index == ActiveIndex)
        {
            ActiveIndex = Math.Min(index, _tabs.Count - 1);
        }
    }

    /// <summary>Repoints a tab after navigating inside it. Identity and position are kept.</summary>
    public TabDescriptor UpdatePath(int index, string path, string title)
    {
        ValidateIndex(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        TabDescriptor updated = _tabs[index] with { Path = path, Title = title };
        _tabs[index] = updated;
        return updated;
    }

    public void Activate(int index)
    {
        ValidateIndex(index);
        ActiveIndex = index;
    }

    /// <summary>Reorders a tab, keeping the same tab active.</summary>
    public void Move(int fromIndex, int toIndex)
    {
        ValidateIndex(fromIndex);
        ValidateIndex(toIndex);

        TabDescriptor? active = Active;
        TabDescriptor moved = _tabs[fromIndex];
        _tabs.RemoveAt(fromIndex);
        _tabs.Insert(toIndex, moved);

        if (active is not null)
        {
            ActiveIndex = _tabs.IndexOf(active);
        }
    }

    private void ValidateIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _tabs.Count);
    }
}
