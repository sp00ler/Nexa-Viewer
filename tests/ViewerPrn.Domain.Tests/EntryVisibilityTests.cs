using ViewerPrn.Domain.FileSystem;

namespace ViewerPrn.Domain.Tests;

public sealed class EntryVisibilityTests
{
    private const FileAttributes Plain = FileAttributes.Normal;
    private const FileAttributes Hidden = FileAttributes.Hidden;
    private const FileAttributes SystemOnly = FileAttributes.System;
    private const FileAttributes ProtectedSystem = FileAttributes.Hidden | FileAttributes.System;

    [Theory]
    // A plain entry is always listed.
    [InlineData(Plain, false, false, true)]
    [InlineData(Plain, true, true, true)]
    // System without Hidden is not hidden at all — Explorer always shows it.
    [InlineData(SystemOnly, false, false, true)]
    // Hidden follows "Show hidden files, folders, and drives".
    [InlineData(Hidden, false, false, false)]
    [InlineData(Hidden, true, false, true)]
    // Hidden + System is a protected operating system file and follows its own option,
    // regardless of the plain hidden setting.
    [InlineData(ProtectedSystem, false, false, false)]
    [InlineData(ProtectedSystem, true, false, false)]
    [InlineData(ProtectedSystem, false, true, true)]
    [InlineData(ProtectedSystem, true, true, true)]
    public void FollowsExplorerRules(
        FileAttributes attributes,
        bool showHidden,
        bool showProtectedSystem,
        bool expected)
    {
        Assert.Equal(expected, EntryVisibility.IsVisible(attributes, showHidden, showProtectedSystem));
    }
}
