using ViewerPrn.Domain.Archives;

namespace ViewerPrn.Domain.Tests;

public sealed class ArchiveLocationTests
{
    [Theory]
    [InlineData(@"E:\photos\trip.zip")]
    [InlineData(@"E:\photos\trip.RAR")]
    public void RecognisesArchives(string name) => Assert.True(ArchiveFormats.IsArchive(name));

    [Theory]
    [InlineData(@"E:\photos\image.jpg")]
    [InlineData(@"E:\photos")]
    [InlineData(@"E:\photos\notes.7z")]
    public void RejectsEverythingElse(string name) => Assert.False(ArchiveFormats.IsArchive(name));

    [Fact]
    public void OrdinaryPathsAreNotArchiveLocations()
    {
        Assert.False(ArchiveLocation.TryParse(@"E:\photos\day1", out _));
        Assert.False(ArchiveLocation.TryParse("", out _));
    }

    [Fact]
    public void TheArchiveItselfIsItsRoot()
    {
        Assert.True(ArchiveLocation.TryParse(@"E:\photos\trip.zip", out ArchiveLocation? location));

        Assert.Equal(@"E:\photos\trip.zip", location.ArchiveFilePath);
        Assert.Equal(string.Empty, location.EntryPath);
        Assert.True(location.IsRoot);
        Assert.Equal("trip.zip", location.Name);
    }

    [Fact]
    public void SplitsAPathInsideAnArchive()
    {
        Assert.True(ArchiveLocation.TryParse(@"E:\photos\trip.zip\day1\IMG_0042.jpg", out ArchiveLocation? location));

        Assert.Equal(@"E:\photos\trip.zip", location.ArchiveFilePath);
        Assert.Equal(@"day1\IMG_0042.jpg", location.EntryPath);
        Assert.False(location.IsRoot);
        Assert.Equal("IMG_0042.jpg", location.Name);
    }

    [Fact]
    public void ForwardSlashesInsideTheArchiveAreAccepted()
    {
        // Archives store forward slashes; the shell shows backslashes.
        Assert.True(ArchiveLocation.TryParse("E:/photos/trip.zip/day1/IMG.jpg", out ArchiveLocation? location));

        Assert.Equal(@"E:\photos\trip.zip", location.ArchiveFilePath);
        Assert.Equal(@"day1\IMG.jpg", location.EntryPath);
    }

    [Fact]
    public void AFolderThatMerelyEndsInAnArchiveNameDoesNotSplitThere()
    {
        Assert.True(ArchiveLocation.TryParse(@"E:\backup.zip\inner.zip\pic.jpg", out ArchiveLocation? location));

        // The innermost archive wins, so the nested one is what gets opened.
        Assert.Equal(@"E:\backup.zip\inner.zip", location.ArchiveFilePath);
        Assert.Equal("pic.jpg", location.EntryPath);
    }

    [Fact]
    public void WalksUpToTheArchiveRootAndStops()
    {
        ArchiveLocation deep = new(@"E:\trip.zip", @"a\b\c.jpg");

        ArchiveLocation? up1 = deep.Parent();
        Assert.Equal(@"a\b", up1!.EntryPath);
        ArchiveLocation? up2 = up1.Parent();
        Assert.Equal("a", up2!.EntryPath);
        ArchiveLocation? up3 = up2.Parent();
        Assert.True(up3!.IsRoot);
        Assert.Null(up3.Parent());
    }

    [Fact]
    public void ChildAppendsBelowTheCurrentLevel()
    {
        ArchiveLocation root = new(@"E:\trip.zip", string.Empty);

        Assert.Equal("day1", root.Child("day1").EntryPath);
        Assert.Equal(@"day1\pic.jpg", root.Child("day1").Child("pic.jpg").EntryPath);
    }

    [Fact]
    public void RoundTripsThroughItsStringForm()
    {
        ArchiveLocation original = new(@"E:\photos\trip.zip", @"day1\IMG.jpg");

        Assert.True(ArchiveLocation.TryParse(original.ToString(), out ArchiveLocation? parsed));
        Assert.Equal(original, parsed);
    }
}
