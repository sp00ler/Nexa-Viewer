using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using ViewerPrn.Application.Abstractions;

namespace ViewerPrn.Infrastructure.Logging;

/// <summary>
/// The transient/crash logging split required by docs/REQUIREMENTS.md:37.
/// <para>
/// One transient log per run, buffered and deleted by <see cref="DiscardTransientLog"/> on a
/// clean shutdown. A crash report is written to its own file, flushed immediately, and is
/// never deleted — so an abnormal termination always leaves evidence behind.
/// </para>
/// </summary>
public sealed class FileLoggingService : ILoggingService, IDisposable
{
    private readonly Lock _gate = new();
    private readonly string _logDirectory;
    private readonly StreamWriter _transientWriter;
    private bool _disposed;

    public FileLoggingService(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        Directory.CreateDirectory(logDirectory);

        _logDirectory = logDirectory;
        TransientLogPath = Path.Combine(
            logDirectory,
            $"session-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");

        _transientWriter = new StreamWriter(
            new FileStream(TransientLogPath, FileMode.Create, FileAccess.Write, FileShare.Read),
            Encoding.UTF8)
        {
            AutoFlush = false,
        };
    }

    public string TransientLogPath { get; }

    /// <summary>Path of the crash report written by the last <see cref="WriteCrashReport"/> call.</summary>
    public string? LastCrashReportPath { get; private set; }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _transientWriter.WriteLine(Format(level, message, exception));

            // Errors are flushed straight away: they are the entries most likely to be
            // followed by a crash that would otherwise lose the buffer.
            if (level == LogLevel.Error)
            {
                _transientWriter.Flush();
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Crash reporting runs while the process is already failing; it must never throw over the original exception.")]
    public void WriteCrashReport(Exception exception, CrashContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        string path = Path.Combine(
            _logDirectory,
            $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");

        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"Timestamp:        {DateTimeOffset.Now:O}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Version:          {context.AppVersion}");
        report.AppendLine(CultureInfo.InvariantCulture, $"OperatingSystem:  {context.OperatingSystem}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Runtime:          {context.RuntimeVersion}");
        report.AppendLine(CultureInfo.InvariantCulture, $"CurrentOperation: {context.CurrentOperation ?? "-"}");
        report.AppendLine(CultureInfo.InvariantCulture, $"CurrentPath:      {context.CurrentPath ?? "-"}");
        report.AppendLine(CultureInfo.InvariantCulture, $"CurrentFile:      {context.CurrentFile ?? "-"}");
        report.AppendLine(CultureInfo.InvariantCulture, $"ActiveTab:        {context.ActiveTabIndex?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        report.AppendLine(CultureInfo.InvariantCulture, $"ViewerState:      {context.ViewerState ?? "-"}");
        report.AppendLine();
        report.AppendLine("Exception:");
        report.AppendLine(exception.ToString());

        try
        {
            File.WriteAllText(path, report.ToString(), Encoding.UTF8);
            LastCrashReportPath = path;
        }
        catch (Exception writeFailure)
        {
            // Nothing better is available at this point; keep the original exception intact.
            System.Diagnostics.Debug.WriteLine(writeFailure);
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                _transientWriter.Flush();
            }
        }
    }

    /// <summary>
    /// Pushes the buffer to disk. Called at points where the log becomes worth reading even
    /// if the process never exits cleanly — after startup, before a long operation.
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _transientWriter.Flush();
            }
        }
    }

    /// <summary>Removes the transient log. Called only after a clean shutdown.</summary>
    public void DiscardTransientLog()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _transientWriter.Flush();
                _transientWriter.Dispose();
                _disposed = true;
            }

            if (File.Exists(TransientLogPath))
            {
                File.Delete(TransientLogPath);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _transientWriter.Flush();
            _transientWriter.Dispose();
            _disposed = true;
        }
    }

    private static string Format(LogLevel level, string message, Exception? exception)
    {
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:HH:mm:ss.fff} [{level}] {message}");

        return exception is null ? line : $"{line}{Environment.NewLine}{exception}";
    }
}
