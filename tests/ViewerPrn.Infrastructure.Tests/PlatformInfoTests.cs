using ViewerPrn.Infrastructure;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class PlatformInfoTests
{
    [Fact]
    public void ReportsTheRunningEnvironment()
    {
        Assert.False(string.IsNullOrWhiteSpace(PlatformInfo.OperatingSystem));
        Assert.False(string.IsNullOrWhiteSpace(PlatformInfo.RuntimeVersion));
        Assert.Equal("X64", PlatformInfo.Architecture);
    }
}
