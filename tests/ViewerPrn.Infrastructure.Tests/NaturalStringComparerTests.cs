using ViewerPrn.Infrastructure.FileSystem;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class NaturalStringComparerTests
{
    [Fact]
    public void DigitsCompareNumericallyNotAlphabetically()
    {
        string[] names = ["img10.jpg", "img2.jpg", "img1.jpg"];
        Array.Sort(names, NaturalStringComparer.Instance);

        Assert.Equal(["img1.jpg", "img2.jpg", "img10.jpg"], names);
    }

    [Fact]
    public void OrdinalComparisonWouldGetThisWrong()
    {
        // Guards the reason this comparer exists at all.
        Assert.True(StringComparer.OrdinalIgnoreCase.Compare("img10.jpg", "img2.jpg") < 0);
        Assert.True(NaturalStringComparer.Instance.Compare("img10.jpg", "img2.jpg") > 0);
    }

    [Fact]
    public void NullsSortFirstAndAreEqualToEachOther()
    {
        Assert.Equal(0, NaturalStringComparer.Instance.Compare(null, null));
        Assert.True(NaturalStringComparer.Instance.Compare(null, "a") < 0);
        Assert.True(NaturalStringComparer.Instance.Compare("a", null) > 0);
    }
}
