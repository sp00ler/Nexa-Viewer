using System.Collections.Concurrent;
using ViewerPrn.Application.Abstractions;

namespace ViewerPrn.Infrastructure.Images;

/// <summary>
/// The icon Windows shows for a kind of file, so the list looks like the shell rather than like
/// one glyph repeated.
/// <para>
/// Asked of the shell once per extension, not once per file: an empty sample file of that
/// extension has no content to make a thumbnail from, so what comes back is the registered type
/// icon. Two extensions registered to the same application give the same icon, which is exactly
/// what Explorer shows.
/// </para>
/// </summary>
// ponytail: sample files instead of SHGetFileInfo. That P/Invoke returns an HICON, and turning
// one into something XAML accepts needs GDI interop and a bitmap copy; this reuses the thumbnail
// provider already here. Swap it for the interop if the sample files ever become a nuisance.
public sealed class FileTypeIcons
{
    /// <summary>Key used for folders, which have no extension.</summary>
    private const string FolderKey = "<folder>";

    private readonly ConcurrentDictionary<string, Task<byte[]?>> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly IThumbnailProvider _thumbnails;
    private readonly string _sampleDirectory;

    public FileTypeIcons(IThumbnailProvider thumbnails, string sampleDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleDirectory);
        _thumbnails = thumbnails;
        _sampleDirectory = sampleDirectory;
    }

    /// <param name="extension">Including the dot, or empty for a folder.</param>
    public Task<byte[]?> GetAsync(string extension, bool isFolder, int edgePixels, CancellationToken cancellationToken = default)
    {
        string key = isFolder ? FolderKey : (string.IsNullOrEmpty(extension) ? "<none>" : extension);

        // One request per kind, ever: the task itself is the cache entry, so a hundred rows of
        // the same type wait on one shell call rather than making a hundred.
        return _icons.GetOrAdd($"{key}|{edgePixels}", _ => LoadAsync(extension, isFolder, edgePixels, cancellationToken));
    }

    private async Task<byte[]?> LoadAsync(string extension, bool isFolder, int edgePixels, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_sampleDirectory);
            string sample;

            if (isFolder)
            {
                sample = Path.Combine(_sampleDirectory, "folder");
                Directory.CreateDirectory(sample);
            }
            else
            {
                // The name is fixed per extension; only the extension decides the icon.
                sample = Path.Combine(_sampleDirectory, "sample" + Sanitise(extension));
                if (!File.Exists(sample))
                {
                    await File.WriteAllBytesAsync(sample, [], cancellationToken).ConfigureAwait(false);
                }
            }

            return await _thumbnails
                .GetThumbnailAsync(sample, DateTimeOffset.UnixEpoch, edgePixels, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // No icon simply means the row keeps its fallback glyph.
            return null;
        }
    }

    /// <summary>Extensions come from real file names and can hold anything a path cannot.</summary>
    private static string Sanitise(string extension) =>
        string.IsNullOrEmpty(extension) || extension.Any(c => Path.GetInvalidFileNameChars().Contains(c))
            ? ".dat"
            : extension;
}
