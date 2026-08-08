namespace ViewerPrn.Domain.Images;

/// <summary>EXIF orientation values 1-8, as stored in the file.</summary>
public enum ImageOrientation
{
    Normal = 1,
    FlipHorizontal = 2,
    Rotate180 = 3,
    FlipVertical = 4,
    Transpose = 5,
    Rotate90 = 6,
    Transverse = 7,
    Rotate270 = 8,
}

public readonly record struct PixelSize(int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Display sizing rules from docs/VIEWER.md:10 — fit large images down proportionally, never
/// upscale small ones, always preserve the aspect ratio, always respect EXIF orientation.
/// </summary>
public static class ImageScaling
{
    /// <summary>
    /// Width and height as the image should be presented: the stored dimensions with the
    /// orientation applied. Quarter turns swap the two.
    /// </summary>
    public static PixelSize ApplyOrientation(PixelSize stored, ImageOrientation orientation) =>
        orientation is ImageOrientation.Transpose
            or ImageOrientation.Rotate90
            or ImageOrientation.Transverse
            or ImageOrientation.Rotate270
            ? new PixelSize(stored.Height, stored.Width)
            : stored;

    /// <summary>
    /// Scales <paramref name="source"/> down to fit inside <paramref name="bounds"/>, keeping
    /// the aspect ratio. An image that already fits is returned untouched — this never enlarges.
    /// </summary>
    public static PixelSize FitDown(PixelSize source, PixelSize bounds)
    {
        if (source.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "Image dimensions must be positive.");
        }

        if (bounds.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), bounds, "Bounds must be positive.");
        }

        if (source.Width <= bounds.Width && source.Height <= bounds.Height)
        {
            return source;
        }

        double scale = Math.Min((double)bounds.Width / source.Width, (double)bounds.Height / source.Height);

        // At least one pixel each way: a 4000x1 image scaled to fit a small box must not vanish.
        return new PixelSize(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
    }
}
