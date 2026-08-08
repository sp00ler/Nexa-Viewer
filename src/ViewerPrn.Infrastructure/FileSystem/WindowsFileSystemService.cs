using Microsoft.VisualBasic.FileIO;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.FileSystem;

// The class name collides with this project's own FileSystem namespace.
using VbFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;

namespace ViewerPrn.Infrastructure.FileSystem;

/// <summary>
/// Directory listing and the two destructive operations Phase 2 needs. Copy, Move and the
/// conflict dialog are Phases 7-8.
/// </summary>
public sealed class WindowsFileSystemService : IFileSystemService
{
    private readonly ILoggingService? _logger;

    public WindowsFileSystemService(ILoggingService? logger = null) => _logger = logger;

    public Task<IReadOnlyList<FileSystemEntry>> EnumerateAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        // ponytail: the whole listing is materialised on a worker thread and handed over in one
        // go; the ListView virtualises the rendering. Ceiling is memory on very large folders —
        // roughly 100 bytes per entry. Switch to IAsyncEnumerable with incremental loading if
        // the 100k benchmark in Phase 14 says it matters.
        return Task.Run<IReadOnlyList<FileSystemEntry>>(
            () =>
            {
                DirectoryInfo directory = new(directoryPath);
                ExplorerVisibilityOptions visibility = ExplorerVisibilityOptions.Read();
                List<FileSystemEntry> entries = [];

                // EnumerateFileSystemInfos carries the attributes the enumeration already
                // returned, so reading Length and LastWriteTime costs no extra syscall.
                foreach (FileSystemInfo info in directory.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!EntryVisibility.IsVisible(info.Attributes, visibility.ShowHidden, visibility.ShowProtectedSystem))
                    {
                        continue;
                    }

                    bool isFolder = info is DirectoryInfo;
                    entries.Add(new FileSystemEntry(
                        info.Name,
                        info.FullName,
                        isFolder ? EntryKind.Folder : EntryKind.File,
                        isFolder ? 0 : ((FileInfo)info).Length,
                        info.LastWriteTime));
                }

                return entries;
            },
            cancellationToken);
    }

    public Task<string> RenameAsync(
        FileSystemEntry entry,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        return Task.Run(
            () =>
            {
                string directory = Path.GetDirectoryName(entry.FullPath)
                    ?? throw new IOException($"'{entry.FullPath}' has no parent directory.");
                string target = Path.Combine(directory, newName);

                if (string.Equals(target, entry.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.FullPath;
                }

                if (File.Exists(target) || Directory.Exists(target))
                {
                    throw new NameConflictException(target);
                }

                if (entry.Kind == EntryKind.Folder)
                {
                    Directory.Move(entry.FullPath, target);
                }
                else
                {
                    // overwrite: false — the existence check above can race, and losing a file
                    // to a race is exactly what must not happen.
                    File.Move(entry.FullPath, target, overwrite: false);
                }

                _logger?.Log(LogLevel.Information, $"Renamed '{entry.FullPath}' to '{target}'.");
                return target;
            },
            cancellationToken);
    }

    public Task DeleteAsync(
        IReadOnlyList<FileSystemEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return Task.Run(
            () =>
            {
                foreach (FileSystemEntry entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Recycle Bin, not permanent deletion: priority 1 is protecting the user's
                    // files, and this is also what Delete does everywhere else in Windows.
                    if (entry.Kind == EntryKind.Folder)
                    {
                        VbFileSystem.DeleteDirectory(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }
                    else
                    {
                        VbFileSystem.DeleteFile(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }

                    _logger?.Log(LogLevel.Information, $"Deleted '{entry.FullPath}' to the Recycle Bin.");
                }
            },
            cancellationToken);
    }
}
