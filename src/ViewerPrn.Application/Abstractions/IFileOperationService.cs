using ViewerPrn.Domain.FileSystem;

namespace ViewerPrn.Application.Abstractions;

public enum FileOperationKind
{
    Copy = 0,
    Move = 1,
}

/// <summary>What the user chose in the conflict dialog (docs/FILE_OPERATIONS.md:18).</summary>
public enum ConflictResolution
{
    Replace = 0,
    Rename = 1,
    Skip = 2,
    Cancel = 3,
}

/// <summary>
/// A destination name that is already taken. Carries both sides so the dialog can show a
/// comparison rather than just a name.
/// </summary>
public sealed record FileConflict(FileSystemEntry Source, FileSystemEntry Destination);

/// <param name="ApplyToAll">
/// Applies to the rest of this operation only — never remembered beyond it
/// (docs/FILE_OPERATIONS.md:20).
/// </param>
public sealed record ConflictChoice(ConflictResolution Resolution, bool ApplyToAll = false);

/// <summary>Progress for a running operation (docs/FILE_OPERATIONS.md:24).</summary>
public sealed record FileOperationProgress
{
    public required string CurrentItem { get; init; }

    public required int ItemsDone { get; init; }

    public required int ItemsTotal { get; init; }

    public required long BytesDone { get; init; }

    public required long BytesTotal { get; init; }

    /// <summary>Null until enough has been transferred for the figure to mean anything.</summary>
    public double? BytesPerSecond { get; init; }

    public TimeSpan? Remaining =>
        BytesPerSecond is > 0 && BytesTotal > BytesDone
            ? TimeSpan.FromSeconds((BytesTotal - BytesDone) / BytesPerSecond.Value)
            : null;

    public double Percent => BytesTotal > 0 ? 100.0 * BytesDone / BytesTotal : 0;
}

public sealed record FileOperationResult
{
    public required int Copied { get; init; }

    public required int Skipped { get; init; }

    public required int Replaced { get; init; }

    public required int Renamed { get; init; }

    public required bool Cancelled { get; init; }

    /// <summary>
    /// Items that failed, with the reason. One failure does not abandon the rest of the
    /// operation (docs/FILE_OPERATIONS.md:26).
    /// </summary>
    public required IReadOnlyList<FileOperationFailure> Failures { get; init; }
}

public sealed record FileOperationFailure(FileSystemEntry Entry, string Reason);

public interface IFileOperationService
{
    /// <summary>
    /// Copies or moves entries into a destination folder.
    /// <para>
    /// Move never removes the source until the destination has been written and verified
    /// (docs/FILE_OPERATIONS.md:5-9). Conflicts are never resolved silently: the callback is
    /// asked, and cancelling stops the whole operation.
    /// </para>
    /// </summary>
    Task<FileOperationResult> ExecuteAsync(
        FileOperationKind kind,
        IReadOnlyList<FileSystemEntry> sources,
        string destinationDirectory,
        Func<FileConflict, Task<ConflictChoice>> resolveConflict,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
