using ViewerPrn.Domain.FileSystem;

namespace ViewerPrn.Domain.Tests;

public sealed class EntrySorterTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static FileSystemEntry Folder(string name, int days = 0) =>
        new(name, @"C:\root\" + name, EntryKind.Folder, 0, Base.AddDays(days));

    private static FileSystemEntry File(string name, long size = 0, int days = 0) =>
        new(name, @"C:\root\" + name, EntryKind.File, size, Base.AddDays(days));

    private static IReadOnlyList<string> Sort(
        IEnumerable<FileSystemEntry> entries,
        SortCriterion criterion = SortCriterion.Name,
        SortDirection direction = SortDirection.Ascending) =>
        [.. EntrySorter.Sort(entries, criterion, direction, StringComparer.OrdinalIgnoreCase).Select(e => e.Name)];

    [Fact]
    public void FoldersComeBeforeFiles()
    {
        Assert.Equal(
            ["alpha", "zulu", "beta", "yankee"],
            Sort([File("beta"), Folder("zulu"), File("yankee"), Folder("alpha")]));
    }

    [Fact]
    public void FoldersStayFirstWhenSortingDescending()
    {
        Assert.Equal(
            ["zulu", "alpha", "yankee", "beta"],
            Sort([File("beta"), Folder("zulu"), File("yankee"), Folder("alpha")], direction: SortDirection.Descending));
    }

    [Fact]
    public void SortsBySize()
    {
        Assert.Equal(
            ["small.txt", "medium.txt", "big.txt"],
            Sort([File("big.txt", 900), File("small.txt", 1), File("medium.txt", 50)], SortCriterion.Size));
    }

    [Fact]
    public void SortsByType()
    {
        Assert.Equal(
            ["c.jpg", "a.png", "b.zip"],
            Sort([File("b.zip"), File("a.png"), File("c.jpg")], SortCriterion.Type));
    }

    [Fact]
    public void SortsByModified()
    {
        Assert.Equal(
            ["old.txt", "middle.txt", "new.txt"],
            Sort([File("new.txt", days: 9), File("old.txt", days: 1), File("middle.txt", days: 5)], SortCriterion.Modified));
    }

    [Fact]
    public void EqualKeysFallBackToName()
    {
        Assert.Equal(
            ["a.txt", "b.txt", "c.txt"],
            Sort([File("c.txt", 10), File("a.txt", 10), File("b.txt", 10)], SortCriterion.Size));
    }

    [Fact]
    public void FoldersSortByNameEvenWhenSortingBySize()
    {
        Assert.Equal(
            ["aaa", "bbb", "file.txt"],
            Sort([File("file.txt", 5), Folder("bbb"), Folder("aaa")], SortCriterion.Size));
    }

    [Fact]
    public void UsesTheSuppliedNameComparer()
    {
        IComparer<string> reversed = Comparer<string>.Create((a, b) => string.CompareOrdinal(b, a));

        IReadOnlyList<FileSystemEntry> sorted =
            EntrySorter.Sort([File("a"), File("b"), File("c")], SortCriterion.Name, SortDirection.Ascending, reversed);

        Assert.Equal(["c", "b", "a"], sorted.Select(e => e.Name));
    }

    [Fact]
    public void EmptyInputProducesEmptyOutput()
    {
        Assert.Empty(Sort([]));
    }
}
