namespace ViewerPrn.Domain.FileSystem;

/// <summary>
/// Whether an entry is listed, following the same rule File Explorer uses.
/// <para>
/// Explorer distinguishes two cases. An entry marked Hidden is an ordinary hidden entry,
/// governed by "Show hidden files, folders, and drives". An entry marked Hidden *and* System
/// is a protected operating system file, governed by the separate "Hide protected operating
/// system files" option. An entry marked System alone is not hidden at all and is always shown.
/// </para>
/// </summary>
public static class EntryVisibility
{
    public static bool IsVisible(FileAttributes attributes, bool showHidden, bool showProtectedSystem)
    {
        bool hidden = attributes.HasFlag(FileAttributes.Hidden);
        if (!hidden)
        {
            return true;
        }

        bool protectedSystem = attributes.HasFlag(FileAttributes.System);
        return protectedSystem ? showProtectedSystem : showHidden;
    }
}
