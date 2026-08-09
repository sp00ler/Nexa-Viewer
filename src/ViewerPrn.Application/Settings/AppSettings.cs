namespace ViewerPrn.Application.Settings;

/// <summary>Theme options from docs/REQUIREMENTS.md:40.</summary>
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>UI language. Russian is the primary language; English is the alternative.</summary>
public enum LanguagePreference
{
    Russian = 0,
    English = 1,
}

/// <summary>
/// Persisted application settings. <see cref="Version"/> exists so a future format change
/// can be migrated instead of silently discarded.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    public LanguagePreference Language { get; init; } = LanguagePreference.Russian;

    /// <summary>
    /// When set, the Viewer jumps around the gallery instead of walking it in order, and
    /// Backspace retraces what was actually seen (docs/REQUIREMENTS.md:22).
    /// </summary>
    public bool RandomViewer { get; init; }

    /// <summary>Accent colour as 0xAARRGGBB. Null means "follow the system accent".</summary>
    public uint? AccentColorArgb { get; init; }

    /// <summary>How many folders the address bar's drop-down remembers.</summary>
    public const int MaxRecentFolders = 20;

    /// <summary>Folders visited, most recent first (DECISION-0037).</summary>
    public IReadOnlyList<string> RecentFolders { get; init; } = [];

    /// <summary>
    /// Puts a folder at the top of the list, removing the older mention of it and anything past
    /// the limit. Returns the same instance when it is already at the top, so ordinary tab
    /// switching does not rewrite the settings file.
    /// </summary>
    public AppSettings WithRecentFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (RecentFolders.Count > 0 && string.Equals(RecentFolders[0], path, StringComparison.OrdinalIgnoreCase)))
        {
            return this;
        }

        return this with
        {
            RecentFolders =
            [
                path,
                .. RecentFolders
                    .Where(folder => !string.Equals(folder, path, StringComparison.OrdinalIgnoreCase))
                    .Take(MaxRecentFolders - 1),
            ],
        };
    }

    public static AppSettings Default { get; } = new();
}
