using ViewerPrn.Domain.FileOperations;

namespace ViewerPrn.Domain.Tests;

public sealed class UniqueNameTests
{
    private static Func<string, bool> Taken(params string[] names) =>
        name => names.Contains(name, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AFreeNameIsUsedAsIs()
    {
        Assert.Equal("photo.jpg", UniqueName.For("photo.jpg", Taken()));
    }

    [Fact]
    public void TheFirstDuplicateBecomesNumberTwo()
    {
        Assert.Equal("photo (2).jpg", UniqueName.For("photo.jpg", Taken("photo.jpg")));
    }

    [Fact]
    public void CountsPastExistingCopies()
    {
        Assert.Equal(
            "photo (4).jpg",
            UniqueName.For("photo.jpg", Taken("photo.jpg", "photo (2).jpg", "photo (3).jpg")));
    }

    [Fact]
    public void NamesWithoutAnExtensionStillWork()
    {
        Assert.Equal("archive (2)", UniqueName.For("archive", Taken("archive")));
    }

    [Fact]
    public void TheSuffixGoesBeforeTheExtensionNotAfterIt()
    {
        Assert.Equal("holiday.photo (2).jpg", UniqueName.For("holiday.photo.jpg", Taken("holiday.photo.jpg")));
    }

    [Fact]
    public void ComparisonIgnoresCaseTheWayWindowsDoes()
    {
        Assert.Equal("Photo (2).JPG", UniqueName.For("Photo.JPG", Taken("photo.jpg")));
    }

    [Fact]
    public void AnEmptyNameIsRejected()
    {
        Assert.Throws<ArgumentException>(() => UniqueName.For("   ", Taken()));
    }
}
