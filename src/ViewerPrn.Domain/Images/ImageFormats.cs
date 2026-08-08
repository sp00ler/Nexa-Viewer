namespace ViewerPrn.Domain.Images;

/// <summary>
/// Which files this application treats as images. The list is deliberately the set the
/// Windows Imaging Component decodes out of the box; anything exotic fails at decode time with
/// a message rather than being promised here.
/// </summary>
public static class ImageFormats
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".png",
        ".gif",
        ".bmp", ".dib",
        ".tif", ".tiff",
        ".webp",
        ".heic", ".heif",
        ".avif",
        ".ico",
    };

    public static bool IsImage(string fileNameOrPath) =>
        !string.IsNullOrEmpty(fileNameOrPath) && Extensions.Contains(Path.GetExtension(fileNameOrPath));
}
