using System.Globalization;
using System.Runtime.InteropServices;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.Images;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ViewerPrn.Infrastructure.Images;

/// <summary>
/// Dimensions, orientation and the EXIF fields the Viewer status bar shows, read through the
/// Windows Imaging Component. WIC is part of the operating system and already exposes these as
/// <c>System.Photo.*</c> properties, so no third-party metadata library is needed
/// (DECISION-0005, revised).
/// </summary>
public sealed class WicMetadataReader : IImageMetadataReader
{
    private static readonly string[] PhotoProperties =
    [
        "System.Photo.Orientation",
        "System.Photo.DateTaken",
        "System.Photo.CameraManufacturer",
        "System.Photo.CameraModel",
        "System.Photo.FocalLength",
        "System.Photo.FNumber",
        "System.Photo.ExposureTime",
        "System.Photo.ISOSpeed",
    ];

    private readonly ILoggingService? _logger;

    public WicMetadataReader(ILoggingService? logger = null) => _logger = logger;

    public async Task<ImageMetadata> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(false);
        using IRandomAccessStream stream = await file.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);

        PixelSize stored = new((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        IDictionary<string, BitmapTypedValue> properties = await ReadPhotoPropertiesAsync(decoder, path, cancellationToken)
            .ConfigureAwait(false);

        return new ImageMetadata
        {
            StoredSize = stored,
            Orientation = ReadOrientation(properties),
            DateTaken = Get<DateTimeOffset>(properties, "System.Photo.DateTaken") is { } taken && taken != default
                ? taken
                : null,
            CameraMaker = Text(properties, "System.Photo.CameraManufacturer"),
            CameraModel = Text(properties, "System.Photo.CameraModel"),
            FocalLengthMm = Get<double>(properties, "System.Photo.FocalLength"),
            FNumber = Get<double>(properties, "System.Photo.FNumber"),
            ExposureTimeSeconds = Get<double>(properties, "System.Photo.ExposureTime"),
            IsoSpeed = Get<ushort>(properties, "System.Photo.ISOSpeed") is { } iso ? iso : null,
        };
    }

    private async Task<IDictionary<string, BitmapTypedValue>> ReadPhotoPropertiesAsync(
        BitmapDecoder decoder,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await decoder.BitmapProperties
                .GetPropertiesAsync(PhotoProperties)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is COMException or NotSupportedException or ArgumentException)
        {
            // Formats such as BMP carry no EXIF at all, and some codecs refuse the whole request
            // rather than returning what they have. Dimensions still work; the rest stays empty.
            _logger?.Log(LogLevel.Debug, $"No photo properties for '{path}': {exception.Message}");
            return new BitmapPropertySet();
        }
    }

    private static ImageOrientation ReadOrientation(IDictionary<string, BitmapTypedValue> properties) =>
        Get<ushort>(properties, "System.Photo.Orientation") is { } value && Enum.IsDefined((ImageOrientation)value)
            ? (ImageOrientation)value
            : ImageOrientation.Normal;

    private static T? Get<T>(IDictionary<string, BitmapTypedValue> properties, string key)
        where T : struct =>
        properties.TryGetValue(key, out BitmapTypedValue? entry) && entry?.Value is T value ? value : null;

    private static string? Text(IDictionary<string, BitmapTypedValue> properties, string key)
    {
        if (!properties.TryGetValue(key, out BitmapTypedValue? entry) || entry?.Value is null)
        {
            return null;
        }

        string? text = entry.Value is string s
            ? s
            : Convert.ToString(entry.Value, CultureInfo.InvariantCulture);

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
