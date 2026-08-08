using Microsoft.Win32;

namespace ViewerPrn.Infrastructure.FileSystem;

/// <summary>
/// The user's File Explorer folder options for hidden and protected system files. Read fresh
/// per listing so changing the setting in Explorer takes effect here without a restart.
/// </summary>
/// <param name="ShowHidden">"Show hidden files, folders, and drives".</param>
/// <param name="ShowProtectedSystem">The inverse of "Hide protected operating system files".</param>
public readonly record struct ExplorerVisibilityOptions(bool ShowHidden, bool ShowProtectedSystem)
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public static ExplorerVisibilityOptions Read()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AdvancedKey);

        // Both values default to "do not show" — the Windows default and the safe one when the
        // key is missing or unreadable.
        return new ExplorerVisibilityOptions(
            ShowHidden: key?.GetValue("Hidden") as int? == 1,
            ShowProtectedSystem: key?.GetValue("ShowSuperHidden") as int? == 1);
    }
}
