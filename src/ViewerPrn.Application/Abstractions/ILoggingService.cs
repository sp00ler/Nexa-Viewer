namespace ViewerPrn.Application.Abstractions;

/// <summary>
/// Diagnostic logging. docs/REQUIREMENTS.md:37 requires a transient log during normal
/// operation that is removed after a successful shutdown, and a crash log that survives
/// abnormal termination.
/// </summary>
public interface ILoggingService
{
    void Log(LogLevel level, string message, Exception? exception = null);

    /// <summary>Writes the crash report that must outlive the process.</summary>
    void WriteCrashReport(Exception exception, CrashContext context);

    /// <summary>Removes the transient log. Called only on a clean shutdown.</summary>
    void DiscardTransientLog();
}

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

/// <summary>
/// State captured alongside a crash (docs/REQUIREMENTS.md:37).
/// </summary>
public sealed record CrashContext(
    string AppVersion,
    string OperatingSystem,
    string RuntimeVersion,
    string? CurrentOperation,
    string? CurrentPath,
    string? CurrentFile,
    int? ActiveTabIndex,
    string? ViewerState);
