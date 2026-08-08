using System.Diagnostics.CodeAnalysis;

namespace ViewerPrn.Domain.Archives;

/// <summary>
/// Which archives this application can browse. Read-only containers, never written to
/// (docs/ARCHITECTURE.md:14).
/// </summary>
public static class ArchiveFormats
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip",
        ".rar",
    };

    public static bool IsArchive(string fileNameOrPath) =>
        !string.IsNullOrEmpty(fileNameOrPath) && Extensions.Contains(Path.GetExtension(fileNameOrPath));
}

/// <summary>
/// A place inside an archive, written the way Windows writes it: the archive file followed by
/// the path within it, e.g. <c>E:\photos\trip.zip\day1\IMG_0042.jpg</c>. An empty
/// <see cref="EntryPath"/> is the archive's own root.
/// </summary>
public sealed record ArchiveLocation(string ArchiveFilePath, string EntryPath)
{
    public bool IsRoot => EntryPath.Length == 0;

    /// <summary>The name to show for this location: the entry's own name, or the archive's.</summary>
    public string Name => IsRoot
        ? Path.GetFileName(ArchiveFilePath)
        : EntryPath[(EntryPath.LastIndexOf('\\') + 1)..];

    /// <summary>
    /// Splits a combined path at the archive file. Returns false for ordinary paths, which is
    /// how callers decide whether to use the file system or the archive service.
    /// </summary>
    public static bool TryParse(string path, [NotNullWhen(true)] out ArchiveLocation? location)
    {
        location = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalised = path.Replace('/', '\\');

        // Walk outwards from the full path: the first segment that is an archive name wins, so
        // an archive inside a folder called "backup.zip.old" cannot confuse the split.
        for (int cut = normalised.Length; cut > 0; cut = normalised.LastIndexOf('\\', cut - 1))
        {
            string head = normalised[..cut];
            if (!ArchiveFormats.IsArchive(head))
            {
                continue;
            }

            string tail = cut < normalised.Length ? normalised[(cut + 1)..].Trim('\\') : string.Empty;
            location = new ArchiveLocation(head, tail);
            return true;
        }

        return false;
    }

    /// <summary>The location one level up, or null when this is the archive's root.</summary>
    public ArchiveLocation? Parent()
    {
        if (IsRoot)
        {
            return null;
        }

        int cut = EntryPath.LastIndexOf('\\');
        return new ArchiveLocation(ArchiveFilePath, cut < 0 ? string.Empty : EntryPath[..cut]);
    }

    public ArchiveLocation Child(string name) =>
        new(ArchiveFilePath, IsRoot ? name : $"{EntryPath}\\{name}");

    public override string ToString() => IsRoot ? ArchiveFilePath : $"{ArchiveFilePath}\\{EntryPath}";
}
