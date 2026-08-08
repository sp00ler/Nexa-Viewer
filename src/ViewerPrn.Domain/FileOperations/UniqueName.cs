namespace ViewerPrn.Domain.FileOperations;

/// <summary>
/// Builds the "Rename" answer to a conflict the way File Explorer does:
/// <c>photo.jpg</c> becomes <c>photo (2).jpg</c>, then <c>photo (3).jpg</c>, and so on.
/// </summary>
public static class UniqueName
{
    /// <param name="exists">
    /// Asked whether a candidate name is taken. Passed in so the rule can be tested without a
    /// file system, and so the caller decides what "taken" means.
    /// </param>
    public static string For(string fileName, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(exists);

        if (!exists(fileName))
        {
            return fileName;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        // Explorer starts at 2: the original is number one.
        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            string candidate = $"{stem} ({suffix}){extension}";
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No free name for '{fileName}'.");
    }
}
