using ViewerPrn.Domain.Viewer;

namespace ViewerPrn.Domain.Tests;

/// <summary>
/// Regression guard for CLAUDE.md "Critical 1-based rule" and docs/TESTING.md:12:
/// the first image must display 1, never 0.
/// </summary>
public sealed class DisplayPositionTests
{
    [Fact]
    public void FirstImageDisplaysOne()
    {
        Assert.Equal(1, DisplayPosition.FromIndex(0));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(468, 469)]
    public void FromIndexAddsOne(int index, int expected)
    {
        Assert.Equal(expected, DisplayPosition.FromIndex(index));
    }

    [Fact]
    public void ToIndexIsTheInverse()
    {
        Assert.Equal(0, DisplayPosition.ToIndex(1));
        Assert.Equal(468, DisplayPosition.ToIndex(469));
    }

    [Fact]
    public void PositionZeroIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayPosition.ToIndex(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayPosition.FromIndex(-1));
    }

    [Fact]
    public void StandardCounterShowsCurrentThenTotal()
    {
        // Current first, total second: the sixth image of 155 reads 6/155.
        Assert.Equal("1/469", StandardCounter.FromIndex(469, 0).ToString());
        Assert.Equal("69/469", StandardCounter.FromIndex(469, 68).ToString());
    }

    [Fact]
    public void StandardCounterRejectsAnIndexOutsideTheGallery()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StandardCounter.FromIndex(469, 469));
    }
}
