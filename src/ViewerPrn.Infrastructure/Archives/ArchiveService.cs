using System.Security.Cryptography;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Readers;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.Archives;
using ViewerPrn.Domain.FileSystem;

namespace ViewerPrn.Infrastructure.Archives;

/// <summary>
/// Browses ZIP and RAR containers through SharpCompress, and hands the rest of the application
/// ordinary file paths.
/// <para>
/// Entries are extracted to a cache directory on first use rather than being streamed. That
/// keeps the thumbnail provider, the metadata reader and the Viewer completely unaware that
/// archives exist — they all keep working on real files.
/// </para>
/// </summary>
// ponytail: extract-to-cache instead of plumbing streams through four layers. Ceiling is disk
// space for the images actually looked at, not the whole archive; the cache is cleared on a
// clean shutdown. Switch to streaming only if temp space ever becomes the problem.
public sealed class ArchiveService : IArchiveService
{
    private readonly string _cacheRoot;
    private readonly ILoggingService? _logger;

    public ArchiveService(string cacheRoot, ILoggingService? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _cacheRoot = cacheRoot;
        _logger = logger;
    }

    public Task<IReadOnlyList<FileSystemEntry>> ListAsync(
        ArchiveLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        return Task.Run<IReadOnlyList<FileSystemEntry>>(
            () =>
            {
                using IArchive archive = ArchiveFactory.OpenArchive(location.ArchiveFilePath, new ReaderOptions());

                string prefix = location.IsRoot ? string.Empty : location.EntryPath + "\\";
                Dictionary<string, FileSystemEntry> folders = new(StringComparer.OrdinalIgnoreCase);
                List<FileSystemEntry> files = [];

                foreach (IArchiveEntry entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.IsDirectory || entry.Key is null)
                    {
                        continue;
                    }

                    string key = entry.Key.Replace('/', '\\').TrimStart('\\');
                    if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relative = key[prefix.Length..];
                    int separator = relative.IndexOf('\\');

                    if (separator < 0)
                    {
                        files.Add(new FileSystemEntry(
                            relative,
                            location.Child(relative).ToString(),
                            EntryKind.File,
                            Math.Max(0, entry.Size),
                            entry.LastModifiedTime ?? DateTime.MinValue));
                        continue;
                    }

                    // Everything deeper collapses into the folder at this level. Archives store a
                    // flat list of paths, so the folders are inferred rather than listed.
                    string folder = relative[..separator];
                    folders.TryAdd(folder, new FileSystemEntry(
                        folder,
                        location.Child(folder).ToString(),
                        EntryKind.Folder,
                        0,
                        entry.LastModifiedTime ?? DateTime.MinValue));
                }

                return [.. folders.Values, .. files];
            },
            cancellationToken);
    }

    public Task<string> MaterialiseAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!ArchiveLocation.TryParse(path, out ArchiveLocation? location) || location.IsRoot)
        {
            return Task.FromResult(path);
        }

        return Task.Run(
            () =>
            {
                string cached = CachePathFor(location);
                if (File.Exists(cached))
                {
                    return cached;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(cached)!);

                using IArchive archive = ArchiveFactory.OpenArchive(location.ArchiveFilePath, new ReaderOptions());
                IArchiveEntry entry = archive.Entries.FirstOrDefault(candidate =>
                    !candidate.IsDirectory
                    && string.Equals(
                        candidate.Key?.Replace('/', '\\').TrimStart('\\'),
                        location.EntryPath,
                        StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException($"'{location.EntryPath}' is not in the archive.", path);

                // Write to a temporary name and move it into place, so a cancelled or failed
                // extraction never leaves a half-written file that later looks like a valid cache hit.
                string partial = cached + ".partial";
                using (FileStream output = File.Create(partial))
                using (Stream input = entry.OpenEntryStream())
                {
                    input.CopyTo(output);
                }

                File.Move(partial, cached, overwrite: true);
                _logger?.Log(LogLevel.Debug, $"Extracted '{location.EntryPath}' from '{location.ArchiveFilePath}'.");
                return cached;
            },
            cancellationToken);
    }

    /// <summary>Removes everything extracted so far. Called on a clean shutdown.</summary>
    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(_cacheRoot))
            {
                Directory.Delete(_cacheRoot, recursive: true);
            }
        }
        catch (IOException exception)
        {
            _logger?.Log(LogLevel.Warning, "Could not clear the archive cache.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger?.Log(LogLevel.Warning, "Could not clear the archive cache.", exception);
        }
    }

    /// <summary>
    /// Cache path built from a hash of the archive path plus the entry's own extension. Entry
    /// names inside archives can contain characters that are illegal on disk, and can be far
    /// longer than the path limit.
    /// </summary>
    private string CachePathFor(ArchiveLocation location)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(location.ToString().ToUpperInvariant()));
        string name = Convert.ToHexStringLower(hash)[..32] + Path.GetExtension(location.EntryPath);
        string bucket = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(location.ArchiveFilePath.ToUpperInvariant())))[..16];

        return Path.Combine(_cacheRoot, bucket, name);
    }
}
