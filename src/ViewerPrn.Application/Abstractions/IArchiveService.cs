using ViewerPrn.Domain.Archives;
using ViewerPrn.Domain.FileSystem;

namespace ViewerPrn.Application.Abstractions;

/// <summary>
/// Read-only browsing of archive containers (docs/REQUIREMENTS.md:4). Archives are never
/// modified: no rename, no delete, no writing back.
/// </summary>
public interface IArchiveService
{
    /// <summary>Lists one level inside an archive, as virtual folders and files.</summary>
    Task<IReadOnlyList<FileSystemEntry>> ListAsync(ArchiveLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a real file path for something that may live inside an archive, extracting it to
    /// a cache if needed. Ordinary paths are returned unchanged, so every consumer downstream —
    /// thumbnails, metadata, the Viewer — keeps working on plain files.
    /// </summary>
    Task<string> MaterialiseAsync(string path, CancellationToken cancellationToken = default);
}
