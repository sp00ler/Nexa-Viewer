namespace ViewerPrn.Domain.FileSystem;

public enum SortCriterion
{
    Name = 0,
    Size = 1,
    Type = 2,
    Modified = 3,

    /// <summary>
    /// Random Explorer (docs/REQUIREMENTS.md:7): folders, files and archives mixed into one
    /// shuffled list. Reversible by choosing any other criterion.
    /// </summary>
    Random = 4,
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
    /// <param name="randomSeed">
    /// Used only by <see cref="SortCriterion.Random"/>. The same seed gives the same order, so a
    /// shuffled listing stays put until it is deliberately reshuffled.
    /// </param>
    public static IReadOnlyList<FileSystemEntry> Sort(
        IEnumerable<FileSystemEntry> entries,
        SortCriterion criterion,
        SortDirection direction,
        IComparer<string> nameComparer,
        int randomSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(nameComparer);

        if (criterion == SortCriterion.Random)
        {
            return Shuffle(entries, randomSeed);
        }

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

    /// <summary>
    /// Fisher-Yates, seeded so the result is reproducible. Folders are not kept first here: the
    /// point of Random Explorer is that everything is mixed together.
    /// </summary>
    private static List<FileSystemEntry> Shuffle(IEnumerable<FileSystemEntry> entries, int seed)
    {
        List<FileSystemEntry> shuffled = [.. entries];
        Random random = new(seed);

        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled;
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
