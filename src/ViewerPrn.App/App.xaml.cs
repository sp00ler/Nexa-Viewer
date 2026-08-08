using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Microsoft.UI.Xaml;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Application.Session;
using ViewerPrn.Application.Settings;
using ViewerPrn.Infrastructure.Session;
using ViewerPrn.Infrastructure;
using ViewerPrn.Infrastructure.Archives;
using ViewerPrn.Infrastructure.Database;
using ViewerPrn.Infrastructure.Favorites;
using ViewerPrn.Infrastructure.Statistics;
using ViewerPrn.Infrastructure.FileSystem;
using ViewerPrn.Infrastructure.Images;
using ViewerPrn.Infrastructure.Logging;
using ViewerPrn.Infrastructure.Settings;

namespace ViewerPrn.App;

/// <summary>
/// The base type is fully qualified because the layer project <c>ViewerPrn.Application</c>
/// makes the bare name <c>Application</c> resolve to a namespace inside <c>ViewerPrn.*</c>.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application, IDisposable
{
    private readonly AppPaths _paths = AppPaths.Default;
    private FileLoggingService? _logger;
    private JsonSessionStore? _sessionStore;
    private ShellThumbnailProvider? _thumbnails;
    private ArchiveService? _archives;
    private SqliteViewStatisticsService? _statistics;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _paths.EnsureCreated();
        _logger = new FileLoggingService(_paths.LogDirectory);
        _logger.Log(LogLevel.Information, $"Starting. {PlatformInfo.RuntimeVersion} on {PlatformInfo.OperatingSystem}.");

        JsonSettingsStore settingsStore = new(_paths.SettingsFile, _logger);
        _sessionStore = new JsonSessionStore(_paths.SessionFile, _logger);
        AppSettings settings = await settingsStore.LoadAsync();

        ApplyLanguage(settings);
        ApplyAccent(settings);

        // One provider for the whole application: its cache is shared across tabs, so revisiting
        // a folder in another tab does not re-fetch the same thumbnails.
        _thumbnails = new ShellThumbnailProvider(_logger);
        _archives = new ArchiveService(_paths.ArchiveCacheDirectory, _logger);

        NexaDatabase database = new(_paths.DatabaseFile, _logger);
        database.Migrate();
        _statistics = new SqliteViewStatisticsService(database);

        _window = new MainWindow(
            settings,
            settingsStore,
            _sessionStore,
            new WindowsFileSystemService(_logger),
            _thumbnails,
            new WicMetadataReader(_logger),
            _archives,
            new FileOperationService(_logger),
            new SqliteFavoritesService(database),
            _statistics,
            _logger);
        _window.Closed += OnWindowClosed;
        _window.Activate();

        await _window.RestoreAsync(await _sessionStore.LoadAsync());

        // Measured from process start so the number includes runtime and framework startup,
        // not only the part after OnLaunched.
        TimeSpan startup = DateTime.Now - Process.GetCurrentProcess().StartTime;
        _window.ReportStartupDuration(startup);
        _logger.Log(LogLevel.Information, $"Shell ready in {startup.TotalMilliseconds:F0} ms.");
        _logger.Flush();
    }

    /// <summary>
    /// Russian is the neutral resource set, so only English needs an explicit culture.
    /// The language is fixed for the lifetime of the process; changing it takes effect on the
    /// next start, which is what the UI tells the user.
    /// </summary>
    private static void ApplyLanguage(AppSettings settings)
    {
        if (settings.Language == LanguagePreference.English)
        {
            CultureInfo english = new("en");
            CultureInfo.DefaultThreadCurrentUICulture = english;
            CultureInfo.CurrentUICulture = english;
        }
    }

    /// <summary>
    /// Overrides the accent colour before any window is created. WinUI resolves the accent
    /// brushes when they are first loaded, so a change made later only takes full effect on
    /// the next start — which is what the UI tells the user.
    /// </summary>
    private void ApplyAccent(AppSettings settings)
    {
        if (AccentColors.ToColor(settings.AccentColorArgb) is { } accent)
        {
            Resources["SystemAccentColor"] = accent;
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        SaveSessionBeforeExit();

        // Clean shutdown: the transient log has no reason to outlive the session
        // (docs/REQUIREMENTS.md:37).
        // Anything still buffered is written before the process goes away.
        _statistics?.FlushAsync().GetAwaiter().GetResult();

        // Extracted archive entries have no reason to outlive the session.
        _archives?.ClearCache();

        _logger?.Log(LogLevel.Information, "Clean shutdown.");
        _logger?.DiscardTransientLog();
        Dispose();
    }

    /// <summary>
    /// The final session write. State is captured on the UI thread, then the write is waited on
    /// rather than left dangling — the process is about to exit and a fire-and-forget save would
    /// be a race with it.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Shutdown must complete even if the session cannot be written.")]
    private void SaveSessionBeforeExit()
    {
        if (_window is null || _sessionStore is null)
        {
            return;
        }

        try
        {
            SessionState state = _window.CaptureSession();
            _sessionStore.SaveAsync(state).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger?.Log(LogLevel.Error, "Could not save the session on shutdown.", exception);
        }
    }

    public void Dispose()
    {
        _statistics?.Dispose();
        _statistics = null;
        _thumbnails?.Dispose();
        _thumbnails = null;
        _logger?.Dispose();
        _logger = null;
        GC.SuppressFinalize(this);
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Report(e.Exception);
    }

    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Report(exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Report(e.Exception);
    }

    private void Report(Exception exception)
    {
        _logger?.WriteCrashReport(exception, new CrashContext(
            AppVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            OperatingSystem: PlatformInfo.OperatingSystem,
            RuntimeVersion: PlatformInfo.RuntimeVersion,
            CurrentOperation: null,
            CurrentPath: _window?.ActivePath,
            CurrentFile: null,
            ActiveTabIndex: _window?.ActiveTabIndex,
            ViewerState: null));
    }
}
