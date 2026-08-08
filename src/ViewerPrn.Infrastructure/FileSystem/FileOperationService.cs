using System.Diagnostics;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.FileOperations;
using ViewerPrn.Domain.FileSystem;

namespace ViewerPrn.Infrastructure.FileSystem;

/// <summary>
/// Copy and Move with progress, cancellation and conflict resolution
/// (docs/FILE_OPERATIONS.md).
/// <para>
/// Move is deliberately not a rename-with-fallback. Within a volume it is an atomic move; across
/// volumes the source is only deleted once the destination has been written and its length
/// verified. A cancelled or failed transfer leaves the source untouched.
/// </para>
/// </summary>
public sealed class FileOperationService : IFileOperationService
{
    // 1 MiB: large enough that per-buffer overhead disappears, small enough that progress and
    // cancellation stay responsive on a slow disk.
    private const int BufferSize = 1024 * 1024;

    private readonly ILoggingService? _logger;

    public FileOperationService(ILoggingService? logger = null) => _logger = logger;

    public async Task<FileOperationResult> ExecuteAsync(
        FileOperationKind kind,
        IReadOnlyList<FileSystemEntry> sources,
        string destinationDirectory,
        Func<FileConflict, Task<ConflictChoice>> resolveConflict,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(resolveConflict);

        Directory.CreateDirectory(destinationDirectory);

        long bytesTotal = sources.Where(entry => entry.Kind == EntryKind.File).Sum(entry => entry.Size);
        long bytesDone = 0;
        int copied = 0, skipped = 0, replaced = 0, renamed = 0, index = 0;
        bool cancelled = false;
        List<FileOperationFailure> failures = [];
        ConflictResolution? rememberedChoice = null;
        Stopwatch clock = Stopwatch.StartNew();

        foreach (FileSystemEntry source in sources)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            index++;
            Report(source.Name);

            string target = Path.Combine(destinationDirectory, source.Name);

            try
            {
                if (IsInsideItself(source, destinationDirectory))
                {
                    failures.Add(new FileOperationFailure(source, "A folder cannot be copied into itself."));
                    continue;
                }

                if (Exists(target))
                {
                    ConflictResolution resolution = rememberedChoice ?? await AskAsync(source, target).ConfigureAwait(false);

                    switch (resolution)
                    {
                        case ConflictResolution.Cancel:
                            cancelled = true;
                            continue;

                        case ConflictResolution.Skip:
                            skipped++;
                            bytesDone += source.Size;
                            continue;

                        case ConflictResolution.Rename:
                            target = Path.Combine(
                                destinationDirectory,
                                UniqueName.For(source.Name, name => Exists(Path.Combine(destinationDirectory, name))));
                            renamed++;
                            break;

                        case ConflictResolution.Replace:
                            replaced++;
                            break;

                        default:
                            break;
                    }
                }
                else
                {
                    copied++;
                }

                if (cancelled)
                {
                    break;
                }

                await TransferAsync(kind, source, target, OnBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // One bad item must not abandon the rest (docs/FILE_OPERATIONS.md:26).
                _logger?.Log(LogLevel.Error, $"{kind} of '{source.FullPath}' failed.", exception);
                failures.Add(new FileOperationFailure(source, Describe(exception)));
            }
        }

        Report(string.Empty);

        return new FileOperationResult
        {
            Copied = copied,
            Skipped = skipped,
            Replaced = replaced,
            Renamed = renamed,
            Cancelled = cancelled,
            Failures = failures,
        };

        async Task<ConflictResolution> AskAsync(FileSystemEntry source, string target)
        {
            ConflictChoice choice = await resolveConflict(
                new FileConflict(source, Describe(target))).ConfigureAwait(false);

            if (choice.ApplyToAll)
            {
                // Remembered for the rest of this operation only.
                rememberedChoice = choice.Resolution;
            }

            return choice.Resolution;
        }

        void OnBytes(long delta)
        {
            bytesDone += delta;
            Report(sources[Math.Min(index, sources.Count) - 1].Name);
        }

        void Report(string currentItem) => progress?.Report(new FileOperationProgress
        {
            CurrentItem = currentItem,
            ItemsDone = index,
            ItemsTotal = sources.Count,
            BytesDone = bytesDone,
            BytesTotal = bytesTotal,

            // Withheld until a second has passed: before that the figure swings wildly and an
            // unreliable ETA is worse than none (docs/FILE_OPERATIONS.md:24).
            BytesPerSecond = clock.Elapsed.TotalSeconds >= 1 ? bytesDone / clock.Elapsed.TotalSeconds : null,
        });
    }

    private static async Task TransferAsync(
        FileOperationKind kind,
        FileSystemEntry source,
        string target,
        Action<long> onBytes,
        CancellationToken cancellationToken)
    {
        if (source.Kind == EntryKind.Folder)
        {
            await TransferFolderAsync(kind, source, target, onBytes, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (kind == FileOperationKind.Move && SameVolume(source.FullPath, target))
        {
            // Same volume: the file system can do this atomically, so nothing is ever in flight.
            File.Move(source.FullPath, target, overwrite: true);
            onBytes(source.Size);
            return;
        }

        await CopyFileAsync(source.FullPath, target, onBytes, cancellationToken).ConfigureAwait(false);

        if (kind == FileOperationKind.Move)
        {
            VerifyThenDeleteSource(source.FullPath, target);
        }
    }

    private static async Task TransferFolderAsync(
        FileOperationKind kind,
        FileSystemEntry source,
        string target,
        Action<long> onBytes,
        CancellationToken cancellationToken)
    {
        if (kind == FileOperationKind.Move && SameVolume(source.FullPath, target) && !Directory.Exists(target))
        {
            Directory.Move(source.FullPath, target);
            return;
        }

        Directory.CreateDirectory(target);

        foreach (FileInfo file in new DirectoryInfo(source.FullPath).EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string childTarget = Path.Combine(target, file.Name);
            await CopyFileAsync(file.FullName, childTarget, onBytes, cancellationToken).ConfigureAwait(false);

            if (kind == FileOperationKind.Move)
            {
                VerifyThenDeleteSource(file.FullName, childTarget);
            }
        }

        foreach (DirectoryInfo child in new DirectoryInfo(source.FullPath).EnumerateDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileSystemEntry entry = new(child.Name, child.FullName, EntryKind.Folder, 0, child.LastWriteTime);
            await TransferFolderAsync(kind, entry, Path.Combine(target, child.Name), onBytes, cancellationToken)
                .ConfigureAwait(false);
        }

        if (kind == FileOperationKind.Move && Directory.Exists(source.FullPath))
        {
            // Only now, with everything below it transferred and verified.
            Directory.Delete(source.FullPath, recursive: true);
        }
    }

    /// <summary>
    /// Copies to a temporary name and moves it into place, so a cancelled or failed copy never
    /// leaves a partial file that looks like the real thing.
    /// </summary>
    private static async Task CopyFileAsync(
        string sourcePath,
        string targetPath,
        Action<long> onBytes,
        CancellationToken cancellationToken)
    {
        string partial = targetPath + ".nexapart";

        try
        {
            await using (FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
            await using (FileStream output = new(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                byte[] buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    onBytes(read);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(partial, targetPath, overwrite: true);
            File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>
    /// The source is removed only after the destination exists and matches in length
    /// (docs/FILE_OPERATIONS.md:5-9). Length is the cheap comparison the specification asks for;
    /// the bytes were written by this process moments earlier, so hashing would buy nothing.
    /// </summary>
    private static void VerifyThenDeleteSource(string sourcePath, string targetPath)
    {
        FileInfo target = new(targetPath);
        FileInfo source = new(sourcePath);

        if (!target.Exists || target.Length != source.Length)
        {
            throw new IOException(
                $"'{targetPath}' did not match the source after copying, so '{sourcePath}' was left in place.");
        }

        File.Delete(sourcePath);
    }

    private static bool SameVolume(string left, string right) =>
        string.Equals(
            Path.GetPathRoot(Path.GetFullPath(left)),
            Path.GetPathRoot(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Guards the classic mistake of dropping a folder into its own subtree.</summary>
    private static bool IsInsideItself(FileSystemEntry source, string destinationDirectory)
    {
        if (source.Kind != EntryKind.Folder)
        {
            return false;
        }

        string from = Path.GetFullPath(source.FullPath).TrimEnd(Path.DirectorySeparatorChar);
        string into = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar);

        return into.Equals(from, StringComparison.OrdinalIgnoreCase)
            || into.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static FileSystemEntry Describe(string path)
    {
        FileInfo file = new(path);
        return file.Exists
            ? new FileSystemEntry(file.Name, file.FullName, EntryKind.File, file.Length, file.LastWriteTime)
            : new FileSystemEntry(
                Path.GetFileName(path),
                path,
                EntryKind.Folder,
                0,
                Directory.Exists(path) ? Directory.GetLastWriteTime(path) : DateTimeOffset.MinValue);
    }

    /// <summary>Plain language for the user; the exception itself goes to the log.</summary>
    private static string Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Access denied.",
        FileNotFoundException => "The file was not found.",
        DirectoryNotFoundException => "The folder was not found.",
        PathTooLongException => "The path is too long.",
        IOException io when io.Message.Contains("space", StringComparison.OrdinalIgnoreCase) => "Not enough space.",
        IOException io when io.Message.Contains("being used", StringComparison.OrdinalIgnoreCase) => "The file is in use.",
        _ => "The operation failed.",
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Nothing better to do while already handling a failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
