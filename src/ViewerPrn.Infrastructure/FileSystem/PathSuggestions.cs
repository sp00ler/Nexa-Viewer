using ViewerPrn.Domain.Archives;

namespace ViewerPrn.Infrastructure.FileSystem;

/// <summary>
/// Address-bar completion. A typed path that does not exist is read as "deepest ancestor that
/// does exist" plus a mask: the segment right below it. The mask matches by substring, ignoring
/// case, unless it carries <c>*</c> or <c>?</c>, in which case it is a wildcard pattern.
/// <para>
/// Folders and archives are offered, because both are navigated into. Nothing is offered for a
/// path that already exists — the drop-down keeps showing visited folders instead — except when
/// the text ends in a separator, which reads as "what is inside this folder".
/// </para>
/// </summary>
public static class PathSuggestions
{
    /// <summary>Enough to choose from; a longer list is a search, not a completion.</summary>
    private const int Limit = 50;

    public static IReadOnlyList<string> For(string typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return [];
        }

        string path = typed.Trim().Trim('"');
        bool wantsChildren = path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar);

        bool exists = Directory.Exists(path);
        if (exists && !wantsChildren)
        {
            return [];
        }

        // The last segment is what is being looked for; the folders above it only say where to
        // look. So the mask is fixed, and the search then climbs to the deepest folder that is
        // really there — a path pasted whole, with several levels gone, still completes.
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string mask = exists ? string.Empty : Path.GetFileName(trimmed);
        string? parent = exists ? path : Path.GetDirectoryName(trimmed);

        while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            parent = Path.GetDirectoryName(parent);
        }

        if (string.IsNullOrEmpty(parent) || (mask.Length == 0 && !wantsChildren))
        {
            return [];
        }

        bool wildcard = mask.Contains('*', StringComparison.Ordinal) || mask.Contains('?', StringComparison.Ordinal);
        string pattern = wildcard ? mask : "*";

        try
        {
            List<string> folders = [];
            List<string> archives = [];

            foreach (string entry in Directory.EnumerateFileSystemEntries(parent, pattern))
            {
                if (!wildcard && mask.Length > 0
                    && !Path.GetFileName(entry).Contains(mask, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    folders.Add(entry);
                }
                else if (ArchiveFormats.IsArchive(entry))
                {
                    archives.Add(entry);
                }
            }

            folders.Sort(NaturalStringComparer.Instance);
            archives.Sort(NaturalStringComparer.Instance);
            return [.. folders.Concat(archives).Take(Limit)];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (ArgumentException)
        {
            // An unfinished path can hold characters the enumerator rejects as a pattern.
            return [];
        }
    }
}
