using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.FileSystem;
using ViewerPrn.Infrastructure.FileSystem;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class FileOperationServiceTests
{
    private static readonly FileOperationService Service = new();

    private static Func<FileConflict, Task<ConflictChoice>> Always(ConflictResolution resolution, bool applyToAll = false) =>
        _ => Task.FromResult(new ConflictChoice(resolution, applyToAll));

    private static Func<FileConflict, Task<ConflictChoice>> NeverAsked() =>
        _ => throw new InvalidOperationException("A conflict was raised where none was expected.");

    private static async Task<FileSystemEntry> WriteFileAsync(string path, string content)
    {
        await File.WriteAllTextAsync(path, content);
        FileInfo info = new(path);
        return new FileSystemEntry(info.Name, info.FullName, EntryKind.File, info.Length, info.LastWriteTime);
    }

    private static FileSystemEntry FolderEntry(string path)
    {
        DirectoryInfo info = new(path);
        return new FileSystemEntry(info.Name, info.FullName, EntryKind.Folder, 0, info.LastWriteTime);
    }

    // ---- Copy ----

    [Fact]
    public async Task CopyLeavesTheSourceInPlace()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        string to = temp.Combine("to");
        FileSystemEntry file = await WriteFileAsync(Path.Combine(from, "a.txt"), "hello");

        FileOperationResult result = await Service.ExecuteAsync(FileOperationKind.Copy, [file], to, NeverAsked());

        Assert.Equal(1, result.Copied);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(file.FullPath));
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(to, "a.txt")));
    }

    [Fact]
    public async Task CopyReportsProgressThatEndsAtTheTotal()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        FileSystemEntry a = await WriteFileAsync(Path.Combine(from, "a.txt"), new string('x', 5000));
        FileSystemEntry b = await WriteFileAsync(Path.Combine(from, "b.txt"), new string('y', 3000));

        List<FileOperationProgress> reports = [];
        await Service.ExecuteAsync(
            FileOperationKind.Copy, [a, b], temp.Combine("to"), NeverAsked(),
            new Progress<FileOperationProgress>(reports.Add));

        // Progress is delivered on the captured context, so give the posted callbacks a moment.
        await Task.Delay(50);

        Assert.NotEmpty(reports);
        Assert.Equal(8000, reports[^1].BytesTotal);
        Assert.Equal(8000, reports.Max(r => r.BytesDone));
        Assert.Equal(2, reports.Max(r => r.ItemsTotal));
    }

    [Fact]
    public async Task NoPartialFileSurvivesASuccessfulCopy()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        string to = temp.Combine("to");
        FileSystemEntry file = await WriteFileAsync(Path.Combine(from, "a.txt"), "hello");

        await Service.ExecuteAsync(FileOperationKind.Copy, [file], to, NeverAsked());

        Assert.Empty(Directory.GetFiles(to, "*.nexapart"));
    }

    // ---- Move ----

    [Fact]
    public async Task MoveRemovesTheSourceOnlyAfterTheDestinationExists()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        string to = temp.Combine("to");
        FileSystemEntry file = await WriteFileAsync(Path.Combine(from, "a.txt"), "contents");

        await Service.ExecuteAsync(FileOperationKind.Move, [file], to, NeverAsked());

        Assert.False(File.Exists(file.FullPath));
        Assert.Equal("contents", await File.ReadAllTextAsync(Path.Combine(to, "a.txt")));
    }

    [Fact]
    public async Task MoveAcrossVolumesCopiesVerifiesThenDeletes()
    {
        // The repository lives on E: and the temp directory on C:, so this really does cross a
        // volume boundary rather than turning into a rename.
        using TempDirectory source = new();
        string crossVolume = Path.Combine(@"E:\", "NexaViewer.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            FileSystemEntry file = await WriteFileAsync(source.Combine("a.txt"), "across");

            FileOperationResult result = await Service.ExecuteAsync(
                FileOperationKind.Move, [file], crossVolume, NeverAsked());

            Assert.Empty(result.Failures);
            Assert.False(File.Exists(file.FullPath));
            Assert.Equal("across", await File.ReadAllTextAsync(Path.Combine(crossVolume, "a.txt")));
        }
        finally
        {
            if (Directory.Exists(crossVolume))
            {
                Directory.Delete(crossVolume, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FoldersAreMovedWithTheirContents()
    {
        using TempDirectory temp = new();
        string tree = Directory.CreateDirectory(temp.Combine(@"from\photos\day1")).FullName;
        await File.WriteAllTextAsync(Path.Combine(tree, "a.txt"), "deep");
        string to = temp.Combine("to");

        await Service.ExecuteAsync(
            FileOperationKind.Move, [FolderEntry(temp.Combine(@"from\photos"))], to, NeverAsked());

        Assert.Equal("deep", await File.ReadAllTextAsync(Path.Combine(to, "photos", "day1", "a.txt")));
        Assert.False(Directory.Exists(temp.Combine(@"from\photos")));
    }

    // ---- Conflicts ----

    [Fact]
    public async Task SkipLeavesTheDestinationUntouched()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        string to = Directory.CreateDirectory(temp.Combine("to")).FullName;
        FileSystemEntry file = await WriteFileAsync(Path.Combine(from, "a.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(to, "a.txt"), "old");

        FileOperationResult result = await Service.ExecuteAsync(
            FileOperationKind.Copy, [file], to, Always(ConflictResolution.Skip));

        Assert.Equal(1, result.Skipped);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(to, "a.txt")));
    }

    [Fact]
    public async Task ReplaceOverwritesOnlyWhenAsked()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        string to = Directory.CreateDirectory(temp.Combine("to")).FullName;
        FileSystemEntry file = await WriteFileAsync(Path.Combine(from, "a.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(to, "a.txt"), "old");

        FileOperationResult result = await Service.ExecuteAsync(
            FileOperationKind.Copy, [file], to, Always(ConflictResolution.Replace));

        Assert.Equal(1, result.Replaced);
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(to, "a.txt")));
    }

    [Fact]
    public async Task RenameKeepsBothFiles()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        string to = Directory.CreateDirectory(temp.Combine("to")).FullName;
        FileSystemEntry file = await WriteFileAsync(Path.Combine(from, "a.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(to, "a.txt"), "old");

        FileOperationResult result = await Service.ExecuteAsync(
            FileOperationKind.Copy, [file], to, Always(ConflictResolution.Rename));

        Assert.Equal(1, result.Renamed);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(to, "a.txt")));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(to, "a (2).txt")));
    }

    [Fact]
    public async Task CancelStopsTheWholeOperation()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        string to = Directory.CreateDirectory(temp.Combine("to")).FullName;
        FileSystemEntry first = await WriteFileAsync(Path.Combine(from, "a.txt"), "1");
        FileSystemEntry second = await WriteFileAsync(Path.Combine(from, "b.txt"), "2");
        await File.WriteAllTextAsync(Path.Combine(to, "a.txt"), "existing");

        FileOperationResult result = await Service.ExecuteAsync(
            FileOperationKind.Copy, [first, second], to, Always(ConflictResolution.Cancel));

        Assert.True(result.Cancelled);
        Assert.False(File.Exists(Path.Combine(to, "b.txt")));
    }

    [Fact]
    public async Task ApplyToAllAsksOnlyOnce()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        string to = Directory.CreateDirectory(temp.Combine("to")).FullName;
        FileSystemEntry a = await WriteFileAsync(Path.Combine(from, "a.txt"), "new-a");
        FileSystemEntry b = await WriteFileAsync(Path.Combine(from, "b.txt"), "new-b");
        await File.WriteAllTextAsync(Path.Combine(to, "a.txt"), "old-a");
        await File.WriteAllTextAsync(Path.Combine(to, "b.txt"), "old-b");

        int asked = 0;
        FileOperationResult result = await Service.ExecuteAsync(
            FileOperationKind.Copy, [a, b], to,
            _ =>
            {
                asked++;
                return Task.FromResult(new ConflictChoice(ConflictResolution.Replace, ApplyToAll: true));
            });

        Assert.Equal(1, asked);
        Assert.Equal(2, result.Replaced);
        Assert.Equal("new-b", await File.ReadAllTextAsync(Path.Combine(to, "b.txt")));
    }

    [Fact]
    public async Task ANewOperationForgetsTheRememberedChoice()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        string to = Directory.CreateDirectory(temp.Combine("to")).FullName;
        FileSystemEntry file = await WriteFileAsync(Path.Combine(from, "a.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(to, "a.txt"), "old");

        await Service.ExecuteAsync(
            FileOperationKind.Copy, [file], to, Always(ConflictResolution.Replace, applyToAll: true));

        int asked = 0;
        await Service.ExecuteAsync(
            FileOperationKind.Copy, [file], to,
            _ =>
            {
                asked++;
                return Task.FromResult(new ConflictChoice(ConflictResolution.Skip));
            });

        Assert.Equal(1, asked);
    }

    // ---- Failure handling ----

    [Fact]
    public async Task AFolderCannotBeCopiedIntoItself()
    {
        using TempDirectory temp = new();
        string folder = Directory.CreateDirectory(temp.Combine("photos")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "x");

        FileOperationResult result = await Service.ExecuteAsync(
            FileOperationKind.Copy, [FolderEntry(folder)], Path.Combine(folder, "inner"), NeverAsked());

        Assert.Single(result.Failures);
        Assert.Contains("itself", result.Failures[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AMissingSourceIsReportedAndTheRestStillRuns()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        FileSystemEntry good = await WriteFileAsync(Path.Combine(from, "good.txt"), "fine");
        FileSystemEntry ghost = new("ghost.txt", Path.Combine(from, "ghost.txt"), EntryKind.File, 10, DateTimeOffset.Now);
        string to = temp.Combine("to");

        FileOperationResult result = await Service.ExecuteAsync(
            FileOperationKind.Copy, [ghost, good], to, NeverAsked());

        Assert.Single(result.Failures);
        Assert.Equal("ghost.txt", result.Failures[0].Entry.Name);
        Assert.True(File.Exists(Path.Combine(to, "good.txt")));
    }

    [Fact]
    public async Task CancellingMidwayLeavesNoPartialFile()
    {
        using TempDirectory temp = new();
        string from = Directory.CreateDirectory(temp.Combine("from")).FullName;
        string to = temp.Combine("to");
        FileSystemEntry file = await WriteFileAsync(Path.Combine(from, "big.txt"), new string('x', 4 * 1024 * 1024));

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        FileOperationResult result = await Service.ExecuteAsync(
            FileOperationKind.Copy, [file], to, NeverAsked(), progress: null, cancellation.Token);

        Assert.True(result.Cancelled);
        Assert.True(File.Exists(file.FullPath));
        Assert.False(Directory.Exists(to) && Directory.GetFiles(to).Length > 0);
    }
}
