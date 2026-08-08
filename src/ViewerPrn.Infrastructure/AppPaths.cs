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

    public string LogDirectory => Path.Combine(Root, "logs");

    public string DatabaseFile => Path.Combine(Root, "viewerprn.db");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
    }
}
