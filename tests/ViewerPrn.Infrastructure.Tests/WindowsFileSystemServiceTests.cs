using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.FileSystem;
using ViewerPrn.Infrastructure.FileSystem;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class WindowsFileSystemServiceTests
{
    private static readonly WindowsFileSystemService Service = new();

    private static FileSystemEntry EntryFor(string path, EntryKind kind) =>
        new(Path.GetFileName(path), path, kind, 0, DateTimeOffset.Now);

    [Fact]
    public async Task ListsFilesAndFoldersWithSizeAndTimestamp()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Combine("sub"));
        await File.WriteAllTextAsync(temp.Combine("note.txt"), "12345");

        IReadOnlyList<FileSystemEntry> entries = await Service.EnumerateAsync(temp.Path);

        FileSystemEntry folder = Assert.Single(entries, e => e.Kind == EntryKind.Folder);
        FileSystemEntry file = Assert.Single(entries, e => e.Kind == EntryKind.File);

        Assert.Equal("sub", folder.Name);
        Assert.Equal(0, folder.Size);
        Assert.Equal("note.txt", file.Name);
        Assert.Equal(5, file.Size);
        Assert.Equal(".txt", file.Extension);
        Assert.True(file.Modified > DateTimeOffset.Now.AddMinutes(-5));
    }

    [Fact]
    public async Task HiddenEntriesFollowTheUsersExplorerSetting()
    {
        using TempDirectory temp = new();
        await File.WriteAllTextAsync(temp.Combine("visible.txt"), "x");
        await File.WriteAllTextAsync(temp.Combine("hidden.txt"), "x");
        File.SetAttributes(temp.Combine("hidden.txt"), FileAttributes.Hidden);

        IReadOnlyList<FileSystemEntry> entries = await Service.EnumerateAsync(temp.Path);

        // Asserted against the live setting rather than a fixed expectation: the point of this
        // test is that the two agree, whatever the user has configured.
        bool showHidden = ExplorerVisibilityOptions.Read().ShowHidden;
        Assert.Contains(entries, entry => entry.Name == "visible.txt");
        Assert.Equal(showHidden, entries.Any(entry => entry.Name == "hidden.txt"));
    }

    [Fact]
    public void ExplorerVisibilityOptionsAreReadable()
    {
        // Missing or unreadable registry values must fall back to hiding, not throw, and the
        // read must be stable between calls.
        Assert.Equal(ExplorerVisibilityOptions.Read(), ExplorerVisibilityOptions.Read());
    }

    [Fact]
    public async Task EmptyFolderListsNothing()
    {
        using TempDirectory temp = new();

        Assert.Empty(await Service.EnumerateAsync(temp.Path));
    }

    [Fact]
    public async Task MissingFolderThrows()
    {
        using TempDirectory temp = new();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => Service.EnumerateAsync(temp.Combine("nope")));
    }

    [Fact]
    public async Task RenamesAFile()
    {
        using TempDirectory temp = new();
        string original = temp.Combine("before.txt");
        await File.WriteAllTextAsync(original, "content");

        string result = await Service.RenameAsync(EntryFor(original, EntryKind.File), "after.txt");

        Assert.Equal(temp.Combine("after.txt"), result);
        Assert.False(File.Exists(original));
        Assert.Equal("content", await File.ReadAllTextAsync(result));
    }

    [Fact]
    public async Task RenamesAFolder()
    {
        using TempDirectory temp = new();
        string original = temp.Combine("before");
        Directory.CreateDirectory(original);

        string result = await Service.RenameAsync(EntryFor(original, EntryKind.Folder), "after");

        Assert.True(Directory.Exists(result));
        Assert.False(Directory.Exists(original));
    }

    [Fact]
    public async Task RenameOntoAnExistingNameIsRefusedAndChangesNothing()
    {
        using TempDirectory temp = new();
        string source = temp.Combine("source.txt");
        string occupied = temp.Combine("occupied.txt");
        await File.WriteAllTextAsync(source, "source");
        await File.WriteAllTextAsync(occupied, "occupied");

        await Assert.ThrowsAsync<NameConflictException>(
            () => Service.RenameAsync(EntryFor(source, EntryKind.File), "occupied.txt"));

        Assert.Equal("source", await File.ReadAllTextAsync(source));
        Assert.Equal("occupied", await File.ReadAllTextAsync(occupied));
    }

    [Fact]
    public async Task RenamingToTheSameNameIsANoOp()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("same.txt");
        await File.WriteAllTextAsync(path, "content");

        string result = await Service.RenameAsync(EntryFor(path, EntryKind.File), "same.txt");

        Assert.Equal(path, result);
        Assert.Equal("content", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task DeleteRemovesFilesAndFolders()
    {
        using TempDirectory temp = new();
        string file = temp.Combine("doomed.txt");
        string folder = temp.Combine("doomedFolder");
        await File.WriteAllTextAsync(file, "x");
        Directory.CreateDirectory(folder);

        await Service.DeleteAsync([EntryFor(file, EntryKind.File), EntryFor(folder, EntryKind.Folder)]);

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task DeletingSomethingThatIsGoneThrows()
    {
        using TempDirectory temp = new();

        await Assert.ThrowsAnyAsync<IOException>(
            () => Service.DeleteAsync([EntryFor(temp.Combine("ghost.txt"), EntryKind.File)]));
    }
}
