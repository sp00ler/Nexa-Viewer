using System.IO.Compression;
using System.Text;
using ViewerPrn.Domain.Archives;
using ViewerPrn.Domain.FileSystem;
using ViewerPrn.Infrastructure.Archives;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class ArchiveServiceTests
{
    /// <summary>Builds a real ZIP so the tests exercise SharpCompress, not a stub.</summary>
    private static string WriteZip(TempDirectory temp, string name, params (string Path, string Content)[] entries)
    {
        string path = temp.Combine(name);
        using FileStream file = File.Create(path);
        using ZipArchive zip = new(file, ZipArchiveMode.Create);

        foreach ((string entryPath, string content) in entries)
        {
            // No byte-order mark: the size assertions below count the bytes that were written.
            using StreamWriter writer = new(zip.CreateEntry(entryPath).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        return path;
    }

    private static ArchiveService ServiceIn(TempDirectory temp) => new(temp.Combine("cache"));

    [Fact]
    public async Task ListsTheArchiveRoot()
    {
        using TempDirectory temp = new();
        string zip = WriteZip(
            temp,
            "trip.zip",
            ("readme.txt", "hello"),
            ("day1/a.jpg", "a"),
            ("day1/b.jpg", "bb"),
            ("day2/c.jpg", "ccc"));

        IReadOnlyList<FileSystemEntry> entries = await ServiceIn(temp)
            .ListAsync(new ArchiveLocation(zip, string.Empty));

        Assert.Equal(["day1", "day2"], entries.Where(e => e.Kind == EntryKind.Folder).Select(e => e.Name).Order());
        Assert.Equal("readme.txt", Assert.Single(entries, e => e.Kind == EntryKind.File).Name);
    }

    [Fact]
    public async Task ListsAFolderInsideTheArchive()
    {
        using TempDirectory temp = new();
        string zip = WriteZip(temp, "trip.zip", ("day1/a.jpg", "a"), ("day1/b.jpg", "bb"), ("day2/c.jpg", "ccc"));

        IReadOnlyList<FileSystemEntry> entries = await ServiceIn(temp)
            .ListAsync(new ArchiveLocation(zip, "day1"));

        Assert.Equal(["a.jpg", "b.jpg"], entries.Select(e => e.Name).Order());
        Assert.All(entries, e => Assert.Equal(EntryKind.File, e.Kind));
    }

    [Fact]
    public async Task EntryPathsAreBrowsableBackIntoTheService()
    {
        using TempDirectory temp = new();
        string zip = WriteZip(temp, "trip.zip", ("day1/a.jpg", "a"));

        FileSystemEntry folder = Assert.Single(await ServiceIn(temp).ListAsync(new ArchiveLocation(zip, string.Empty)));

        Assert.Equal(Path.Combine(zip, "day1"), folder.FullPath);
        Assert.True(ArchiveLocation.TryParse(folder.FullPath, out ArchiveLocation? parsed));
        Assert.Equal("day1", parsed.EntryPath);
    }

    [Fact]
    public async Task SizesComeFromTheArchive()
    {
        using TempDirectory temp = new();
        string zip = WriteZip(temp, "sizes.zip", ("three.txt", "abc"));

        FileSystemEntry entry = Assert.Single(await ServiceIn(temp).ListAsync(new ArchiveLocation(zip, string.Empty)));

        Assert.Equal(3, entry.Size);
    }

    [Fact]
    public async Task AnEmptyArchiveListsNothing()
    {
        using TempDirectory temp = new();
        string zip = WriteZip(temp, "empty.zip");

        Assert.Empty(await ServiceIn(temp).ListAsync(new ArchiveLocation(zip, string.Empty)));
    }

    [Fact]
    public async Task MaterialisingExtractsTheEntryToARealFile()
    {
        using TempDirectory temp = new();
        string zip = WriteZip(temp, "trip.zip", ("day1/a.txt", "contents of a"));

        string real = await ServiceIn(temp).MaterialiseAsync(Path.Combine(zip, "day1", "a.txt"));

        Assert.True(File.Exists(real));
        Assert.Equal("contents of a", await File.ReadAllTextAsync(real));
        Assert.Equal(".txt", Path.GetExtension(real));
    }

    [Fact]
    public async Task TheSecondMaterialisationReusesTheCachedFile()
    {
        using TempDirectory temp = new();
        string zip = WriteZip(temp, "trip.zip", ("a.txt", "x"));
        ArchiveService service = ServiceIn(temp);
        string virtualPath = Path.Combine(zip, "a.txt");

        string first = await service.MaterialiseAsync(virtualPath);
        DateTime writtenAt = File.GetLastWriteTimeUtc(first);
        string second = await service.MaterialiseAsync(virtualPath);

        Assert.Equal(first, second);
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(second));
    }

    [Fact]
    public async Task AnOrdinaryPathIsReturnedUnchanged()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("plain.jpg");
        await File.WriteAllTextAsync(path, "x");

        Assert.Equal(path, await ServiceIn(temp).MaterialiseAsync(path));
    }

    [Fact]
    public async Task AMissingEntryFailsClearly()
    {
        using TempDirectory temp = new();
        string zip = WriteZip(temp, "trip.zip", ("a.txt", "x"));

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => ServiceIn(temp).MaterialiseAsync(Path.Combine(zip, "ghost.txt")));
    }

    [Fact]
    public async Task NoHalfWrittenFileSurvivesAFailedExtraction()
    {
        using TempDirectory temp = new();
        string zip = WriteZip(temp, "trip.zip", ("a.txt", "x"));
        ArchiveService service = ServiceIn(temp);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.MaterialiseAsync(Path.Combine(zip, "ghost.txt")));

        string[] leftovers = Directory.Exists(temp.Combine("cache"))
            ? Directory.GetFiles(temp.Combine("cache"), "*.partial", SearchOption.AllDirectories)
            : [];
        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task ClearingTheCacheRemovesExtractedFiles()
    {
        using TempDirectory temp = new();
        string zip = WriteZip(temp, "trip.zip", ("a.txt", "x"));
        ArchiveService service = ServiceIn(temp);
        string real = await service.MaterialiseAsync(Path.Combine(zip, "a.txt"));

        service.ClearCache();

        Assert.False(File.Exists(real));
    }

    [Fact]
    public async Task ACorruptArchiveFailsRatherThanReturningNothing()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("broken.zip");
        await File.WriteAllTextAsync(path, "this is not a zip file");

        await Assert.ThrowsAnyAsync<Exception>(
            () => ServiceIn(temp).ListAsync(new ArchiveLocation(path, string.Empty)));
    }
}
