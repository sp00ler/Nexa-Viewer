using System.Runtime.InteropServices;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Infrastructure.Caching;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace ViewerPrn.Infrastructure.Images;

/// <summary>
/// Thumbnails from the Windows shell, which means the cache File Explorer has already filled
/// for any folder the user has browsed. Decoding every image ourselves would repeat work the
/// operating system has already done and stored.
/// </summary>
public sealed class ShellThumbnailProvider : IThumbnailProvider, IDisposable
{
    /// <summary>
    /// Placeholder budget per DECISION-0010 — the real figure comes from the Phase 14
    /// measurements, not from a guess made here.
    /// </summary>
    public const long DefaultCacheBytes = 64L * 1024 * 1024;

    private readonly BoundedLruCache<string, byte[]> _cache;
    private readonly SemaphoreSlim _concurrency;
    private readonly ILoggingService? _logger;

    public ShellThumbnailProvider(
        ILoggingService? logger = null,
        long cacheBytes = DefaultCacheBytes,
        int maxConcurrency = 4)
    {
        _logger = logger;
        _cache = new BoundedLruCache<string, byte[]>(cacheBytes, bytes => bytes.LongLength, StringComparer.OrdinalIgnoreCase);
        _concurrency = new SemaphoreSlim(maxConcurrency);
    }

    public long CachedBytes => _cache.CurrentBytes;

    public async Task<byte[]?> GetThumbnailAsync(
        string path,
        DateTimeOffset modified,
        int edgePixels,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(edgePixels, 1);

        // The timestamp is part of the key so an edited file stops showing its old picture.
        string key = $"{path}|{modified.UtcTicks}|{edgePixels}";
        if (_cache.TryGet(key, out byte[]? cached))
        {
            return cached;
        }

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGet(key, out cached))
            {
                return cached;
            }

            StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(false);
            using StorageItemThumbnail? thumbnail = await file
                .GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)edgePixels, ThumbnailOptions.ResizeThumbnail)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (thumbnail is null || thumbnail.Size == 0)
            {
                return null;
            }

            byte[] bytes = new byte[thumbnail.Size];
            using DataReader reader = new(thumbnail);
            await reader.LoadAsync((uint)thumbnail.Size).AsTask(cancellationToken).ConfigureAwait(false);
            reader.ReadBytes(bytes);

            _cache.Set(key, bytes);
            return bytes;
        }
        catch (Exception exception) when (exception is FileNotFoundException
            or UnauthorizedAccessException
            or ArgumentException
            or COMException)
        {
            // A missing thumbnail is not a failure worth interrupting a folder listing over.
            _logger?.Log(LogLevel.Debug, $"No thumbnail for '{path}': {exception.Message}");
            return null;
        }
        finally
        {
            _concurrency.Release();
        }
    }

    public void Dispose() => _concurrency.Dispose();
}
