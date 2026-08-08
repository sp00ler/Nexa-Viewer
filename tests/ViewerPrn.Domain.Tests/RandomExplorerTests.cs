using ViewerPrn.Domain.FileSystem;

namespace ViewerPrn.Domain.Tests;

/// <summary>Random Explorer (docs/REQUIREMENTS.md:7).</summary>
public sealed class RandomExplorerTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<FileSystemEntry> Mixed(int folders, int files) =>
    [
        .. Enumerable.Range(0, folders).Select(i =>
            new FileSystemEntry($"folder{i}", $@"C:\root\folder{i}", EntryKind.Folder, 0, Base)),
        .. Enumerable.Range(0, files).Select(i =>
            new FileSystemEntry($"file{i}.jpg", $@"C:\root\file{i}.jpg", EntryKind.File, i, Base)),
    ];

    private static IReadOnlyList<string> Shuffled(IReadOnlyList<FileSystemEntry> entries, int seed) =>
    [
        .. EntrySorter.Sort(entries, SortCriterion.Random, SortDirection.Ascending, StringComparer.Ordinal, seed)
            .Select(entry => entry.Name),
    ];

    [Fact]
    public void EverythingIsKeptAndNothingIsInvented()
    {
        IReadOnlyList<FileSystemEntry> entries = Mixed(4, 6);

        IReadOnlyList<string> shuffled = Shuffled(entries, seed: 7);

        Assert.Equal(10, shuffled.Count);
        Assert.Equal(entries.Select(e => e.Name).Order(), shuffled.Order());
    }

    [Fact]
    public void FoldersAreMixedInRatherThanKeptFirst()
    {
        IReadOnlyList<FileSystemEntry> entries = Mixed(10, 10);

        // With twenty entries, a shuffle that still had all folders first would be a one-in-
        // 184756 coincidence; a fixed seed makes it deterministic anyway.
        IReadOnlyList<string> shuffled = Shuffled(entries, seed: 12345);
        int lastFolder = shuffled.Select((name, index) => (name, index))
            .Where(x => x.name.StartsWith("folder", StringComparison.Ordinal))
            .Max(x => x.index);
        int firstFile = shuffled.Select((name, index) => (name, index))
            .Where(x => x.name.StartsWith("file", StringComparison.Ordinal))
            .Min(x => x.index);

        Assert.True(lastFolder > firstFile, "Folders and files should be interleaved.");
    }

    [Fact]
    public void TheSameSeedGivesTheSameOrder()
    {
        IReadOnlyList<FileSystemEntry> entries = Mixed(5, 5);

        Assert.Equal(Shuffled(entries, 99), Shuffled(entries, 99));
    }

    [Fact]
    public void ADifferentSeedGivesADifferentOrder()
    {
        IReadOnlyList<FileSystemEntry> entries = Mixed(10, 10);

        Assert.NotEqual(Shuffled(entries, 1), Shuffled(entries, 2));
    }

    [Fact]
    public void ChoosingAnotherCriterionRestoresTheNormalOrder()
    {
        // "Reversible to normal sorting": the shuffle is a view, not a change to the data.
        IReadOnlyList<FileSystemEntry> entries = Mixed(2, 2);
        _ = EntrySorter.Sort(entries, SortCriterion.Random, SortDirection.Ascending, StringComparer.Ordinal, 5);

        IReadOnlyList<FileSystemEntry> normal =
            EntrySorter.Sort(entries, SortCriterion.Name, SortDirection.Ascending, StringComparer.Ordinal);

        Assert.Equal(["folder0", "folder1", "file0.jpg", "file1.jpg"], normal.Select(e => e.Name));
    }

    [Fact]
    public void AnEmptyListShufflesToNothing()
    {
        Assert.Empty(Shuffled([], 1));
    }

    [Fact]
    public void ASingleEntryIsUnchanged()
    {
        Assert.Equal(["folder0"], Shuffled(Mixed(1, 0), 3));
    }
}
