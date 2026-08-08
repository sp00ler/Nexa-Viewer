namespace ViewerPrn.Domain.FileSystem;

public enum SortCriterion
{
    Name = 0,
    Size = 1,
    Type = 2,
    Modified = 3,
}

public enum SortDirection
{
    Ascending = 0,
    Descending = 1,
}

/// <summary>
/// Explorer ordering: folders first, files second, then the chosen criterion
/// (docs/REQUIREMENTS.md:4). Folders stay grouped first in both directions, which is what
/// File Explorer does.
/// </summary>
public static class EntrySorter
{
    public static IReadOnlyList<FileSystemEntry> Sort(
        IEnumerable<FileSystemEntry> entries,
        SortCriterion criterion,
        SortDirection direction,
        IComparer<string> nameComparer)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(nameComparer);

        IOrderedEnumerable<FileSystemEntry> ordered = entries.OrderBy(entry => entry.Kind);

        ordered = criterion switch
        {
            SortCriterion.Name => ThenBy(ordered, entry => entry.Name, nameComparer, direction),
            SortCriterion.Size => ThenBy(ordered, entry => entry.Size, Comparer<long>.Default, direction),
            SortCriterion.Type => ThenBy(ordered, entry => entry.Extension, StringComparer.OrdinalIgnoreCase, direction),
            SortCriterion.Modified => ThenBy(ordered, entry => entry.Modified, Comparer<DateTimeOffset>.Default, direction),
            _ => ordered,
        };

        // Equal keys (same size, same extension, same timestamp) fall back to the name so the
        // order is stable and predictable rather than whatever the file system returned.
        if (criterion != SortCriterion.Name)
        {
            ordered = ordered.ThenBy(entry => entry.Name, nameComparer);
        }

        return [.. ordered];
    }

    private static IOrderedEnumerable<FileSystemEntry> ThenBy<TKey>(
        IOrderedEnumerable<FileSystemEntry> source,
        Func<FileSystemEntry, TKey> key,
        IComparer<TKey> comparer,
        SortDirection direction) =>
        direction == SortDirection.Ascending
            ? source.ThenBy(key, comparer)
            : source.ThenByDescending(key, comparer);
}
