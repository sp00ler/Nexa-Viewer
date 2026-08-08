using ViewerPrn.Application.Abstractions;
using ViewerPrn.Infrastructure.Logging;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class FileLoggingServiceTests
{
    private static CrashContext SampleContext() => new(
        AppVersion: "0.1.0",
        OperatingSystem: "Windows 11",
        RuntimeVersion: ".NET 10.0",
        CurrentOperation: "MoveTo",
        CurrentPath: @"E:\photos",
        CurrentFile: "IMG_0042.jpg",
        ActiveTabIndex: 3,
        ViewerState: "Sequential 105/951");

    [Fact]
    public void TransientLogIsCreatedOnStart()
    {
        using TempDirectory temp = new();
        using FileLoggingService logger = new(temp.Path);

        Assert.True(File.Exists(logger.TransientLogPath));
    }

    [Fact]
    public void CleanShutdownRemovesTheTransientLog()
    {
        using TempDirectory temp = new();
        FileLoggingService logger = new(temp.Path);
        logger.Log(LogLevel.Information, "started");
        string path = logger.TransientLogPath;

        logger.DiscardTransientLog();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CrashReportSurvivesAndCarriesTheRequiredFields()
    {
        using TempDirectory temp = new();
        FileLoggingService logger = new(temp.Path);

        logger.WriteCrashReport(new InvalidOperationException("boom"), SampleContext());
        logger.DiscardTransientLog();

        Assert.NotNull(logger.LastCrashReportPath);
        Assert.True(File.Exists(logger.LastCrashReportPath));

        string report = File.ReadAllText(logger.LastCrashReportPath!);
        Assert.Contains("0.1.0", report, StringComparison.Ordinal);
        Assert.Contains("Windows 11", report, StringComparison.Ordinal);
        Assert.Contains(".NET 10.0", report, StringComparison.Ordinal);
        Assert.Contains("MoveTo", report, StringComparison.Ordinal);
        Assert.Contains(@"E:\photos", report, StringComparison.Ordinal);
        Assert.Contains("IMG_0042.jpg", report, StringComparison.Ordinal);
        Assert.Contains("Sequential 105/951", report, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", report, StringComparison.Ordinal);
        Assert.Contains("boom", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorEntriesReachDiskWithoutAShutdown()
    {
        using TempDirectory temp = new();
        using FileLoggingService logger = new(temp.Path);

        logger.Log(LogLevel.Error, "disk full");

        using FileStream stream = File.Open(logger.TransientLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream);
        Assert.Contains("disk full", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void FlushMakesBufferedEntriesReadableWhileRunning()
    {
        using TempDirectory temp = new();
        using FileLoggingService logger = new(temp.Path);
        logger.Log(LogLevel.Information, "startup entry");

        logger.Flush();

        using FileStream stream = File.Open(logger.TransientLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream);
        Assert.Contains("startup entry", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void DisposeFlushesTheBuffer()
    {
        using TempDirectory temp = new();
        FileLoggingService logger = new(temp.Path);
        string path = logger.TransientLogPath;
        logger.Log(LogLevel.Information, "buffered entry");

        logger.Dispose();

        Assert.Contains("buffered entry", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void LoggingAfterDiscardIsIgnored()
    {
        using TempDirectory temp = new();
        FileLoggingService logger = new(temp.Path);
        logger.DiscardTransientLog();

        logger.Log(LogLevel.Information, "too late");

        Assert.False(File.Exists(logger.TransientLogPath));
    }
}
