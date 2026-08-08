using System.Runtime.InteropServices;

namespace ViewerPrn.Infrastructure;

/// <summary>
/// Environment facts recorded in crash reports and in docs/PERFORMANCE.md runs.
/// </summary>
public static class PlatformInfo
{
    public static string OperatingSystem => RuntimeInformation.OSDescription;

    public static string RuntimeVersion => RuntimeInformation.FrameworkDescription;

    public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString();
}
