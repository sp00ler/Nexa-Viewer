using ViewerPrn.Domain.FileSystem;

namespace ViewerPrn.Application.Abstractions;

/// <summary>Raised when an operation would overwrite an existing name. Never overwrite silently.</summary>
public sealed class NameConflictException : IOException
{
    public NameConflictException(string path)
        : base($"'{path}' already exists.")
    {
        Path = path;
    }

    public string Path { get; }
}

public interface IFileSystemService
{
    /// <summary>Lists one directory. Never recurses, never computes folder sizes.</summary>
    Task<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>Renames an entry in place and returns the new full path.</summary>
    /// <exception cref="NameConflictException">The new name is already taken.</exception>
    Task<string> RenameAsync(FileSystemEntry entry, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes entries to the Recycle Bin, so a mistake stays recoverable.</summary>
    Task DeleteAsync(IReadOnlyList<FileSystemEntry> entries, CancellationToken cancellationToken = default);
}
