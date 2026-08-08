using ViewerPrn.Domain.Images;

namespace ViewerPrn.Application.Abstractions;

/// <summary>
/// What the Viewer status bar shows (docs/VIEWER.md:7). Every EXIF field is optional: empty
/// ones are hidden rather than displayed blank.
/// </summary>
public sealed record ImageMetadata
{
    public required PixelSize StoredSize { get; init; }

    public ImageOrientation Orientation { get; init; } = ImageOrientation.Normal;

    /// <summary>Stored size with <see cref="Orientation"/> applied — what the user sees.</summary>
    public PixelSize DisplaySize => ImageScaling.ApplyOrientation(StoredSize, Orientation);

    public DateTimeOffset? DateTaken { get; init; }

    public string? CameraMaker { get; init; }

    public string? CameraModel { get; init; }

    public double? FocalLengthMm { get; init; }

    public double? FNumber { get; init; }

    public double? ExposureTimeSeconds { get; init; }

    public int? IsoSpeed { get; init; }
}

public interface IImageMetadataReader
{
    Task<ImageMetadata> ReadAsync(string path, CancellationToken cancellationToken = default);
}

public interface IThumbnailProvider
{
    /// <summary>
    /// A thumbnail for the file, as encoded image bytes, or null when the system has none.
    /// <paramref name="modified"/> takes part in the cache key so an edited file does not keep
    /// showing its old picture.
    /// </summary>
    Task<byte[]?> GetThumbnailAsync(
        string path,
        DateTimeOffset modified,
        int edgePixels,
        CancellationToken cancellationToken = default);
}

// ponytail: no IImageDecoder yet. Nothing decodes a full image until the Viewer in Phase 5, and
// its surface depends on what the Viewer wants to hand to XAML — designing it now would mean
// guessing. The sizing rules it needs already exist and are tested in ImageScaling.
