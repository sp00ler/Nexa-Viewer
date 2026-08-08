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

    /// <summary>Accent colour as 0xAARRGGBB. Null means "follow the system accent".</summary>
    public uint? AccentColorArgb { get; init; }

    public static AppSettings Default { get; } = new();
}
