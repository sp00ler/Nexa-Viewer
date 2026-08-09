namespace ViewerPrn.Infrastructure;

/// <summary>
/// Where the application keeps its own data. Everything lives under the local (non-roaming)
/// profile: this product is offline and machine-local by contract.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        Root = rootDirectory;
    }

    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexaViewer"));

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string SessionFile => Path.Combine(Root, "session.json");

    /// <summary>The states saved by hand from the Sessions menu (DECISION-0036).</summary>
    public string SessionLibraryFile => Path.Combine(Root, "states.json");

    public string LogDirectory => Path.Combine(Root, "logs");

    /// <summary>Empty sample files the shell is asked for file-type icons.</summary>
    public string IconSampleDirectory => Path.Combine(Root, "cache", "icons");

    /// <summary>Archive entries extracted for viewing. Cleared on a clean shutdown.</summary>
    public string ArchiveCacheDirectory => Path.Combine(Root, "cache", "archives");

    public string DatabaseFile => Path.Combine(Root, "viewerprn.db");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
    }
}
