using ViewerPrn.Domain.Viewer;

namespace ViewerPrn.Domain.Tests;

/// <summary>The Intro Counter scenarios from docs/TESTING.md and docs/VIEWER.md.</summary>
public sealed class IntroCounterTests
{
    private static IntroCounter After(int totalImages, int imagesViewed)
    {
        IntroCounter counter = IntroCounter.ForGallery(totalImages);
        for (int i = 0; i < imagesViewed; i++)
        {
            counter.OnImageViewed();
        }

        return counter;
    }

    private static IntroCounter AtCyclePosition(int totalImages, int cyclePosition) =>
        After(totalImages, CycleTable.Resolve(totalImages).IntroCount + cyclePosition);

    // ---- The two mandatory galleries ----

    [Fact]
    public void Gallery951_TheIntroductoryBlockShowsPositionOne()
    {
        Assert.Equal("1(20)/100", After(951, 1).Format());
        Assert.Equal("1(20)/100", After(951, 20).Format());

        // The first counted image lands on the position the block was already showing.
        Assert.Equal("1(20)/100", After(951, 21).Format());
    }

    [Fact]
    public void Gallery951_Physical105StartsTheWarning()
    {
        Assert.Equal("84(20)/100", After(951, 104).Format());
        Assert.False(After(951, 104).IsWarningActive);

        Assert.Equal("85(20)/100", After(951, 105).Format());
        Assert.True(After(951, 105).IsWarningActive);
    }

    [Fact]
    public void Gallery469_MatchesTheSpecification()
    {
        Assert.Equal("1(15)/50", After(469, 16).Format());
        Assert.Equal("35(15)/50", After(469, 50).Format());
        Assert.True(After(469, 50).IsWarningActive);
    }

    [Fact]
    public void TheCyclePositionKeepsGrowingPastTheCycleLength()
    {
        Assert.Equal("931(20)/100", After(951, 951).Format());
    }

    // ---- Galleries too small for a cycle ----

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    public void SmallGalleriesShowTheDashState(int total)
    {
        IntroCounter counter = After(total, 1);

        Assert.False(counter.HasCycle);
        Assert.Equal("-(5)/-", counter.Format());
        Assert.False(counter.IsWarningActive);
    }

    [Fact]
    public void NoControlIsAvailableWithoutACycle()
    {
        IntroCounter counter = After(50, 10);

        Assert.False(counter.CanReset);
        Assert.False(counter.CanMinus10);
        Assert.False(counter.CanMinus1);
    }

    // ---- Stop / Do Not Count ----

    [Fact]
    public void StopHoldsTheCounterForExactlyOneImage()
    {
        IntroCounter counter = AtCyclePosition(469, 15);
        Assert.Equal("15(15)/50", counter.Format());

        counter.Stop();
        counter.OnImageViewed();
        Assert.Equal("15(15)/50", counter.Format());

        counter.OnImageViewed();
        Assert.Equal("16(15)/50", counter.Format());
    }

    [Fact]
    public void PressesAccumulate()
    {
        IntroCounter counter = AtCyclePosition(469, 15);

        counter.Stop();
        counter.Stop();
        counter.Stop();
        Assert.Equal(3, counter.PendingSkips);

        counter.OnImageViewed();
        counter.OnImageViewed();
        counter.OnImageViewed();
        Assert.Equal("15(15)/50", counter.Format());

        counter.OnImageViewed();
        Assert.Equal("16(15)/50", counter.Format());
    }

    [Fact]
    public void StopStillCountsTheImageAsViewed()
    {
        // The standard counter tracks the gallery; only the helper counter stands still.
        IntroCounter counter = AtCyclePosition(469, 15);
        int before = counter.ViewedCount;

        counter.Stop();
        counter.OnImageViewed();

        Assert.Equal(before + 1, counter.ViewedCount);
    }

    // ---- Reset ----

    [Fact]
    public void ResetReturnsToPositionOneAndCounts()
    {
        IntroCounter counter = AtCyclePosition(469, 1);

        counter.Reset();
        Assert.Equal("1(15)/50", counter.Format());
        Assert.Equal(1, counter.ResetCount);

        for (int i = 0; i < 8; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal("9(15)/50", counter.Format());
        counter.Reset();
        Assert.Equal(2, counter.ResetCount);

        for (int i = 0; i < 3; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal("4(15)/50", counter.Format());
        counter.Reset();
        Assert.Equal(3, counter.ResetCount);
        Assert.Equal("1(15)/50", counter.Format());
    }

    [Theory]
    [InlineData(3, ResetSeverity.Normal)]
    [InlineData(4, ResetSeverity.Orange)]
    [InlineData(5, ResetSeverity.RedWithExclamation)]
    [InlineData(6, ResetSeverity.RedWithExclamation)]
    [InlineData(12, ResetSeverity.RedWithExclamation)]
    public void ResetSeverityStopsChangingAfterTheFifth(int resets, ResetSeverity expected)
    {
        IntroCounter counter = AtCyclePosition(469, 1);
        for (int i = 0; i < resets; i++)
        {
            counter.Reset();
        }

        Assert.Equal(expected, counter.ResetSeverity);
    }

    [Fact]
    public void ResetIsDisabledFromPositionEleven()
    {
        Assert.True(AtCyclePosition(469, 10).CanReset);
        Assert.False(AtCyclePosition(469, 11).CanReset);
        Assert.Throws<InvalidOperationException>(AtCyclePosition(469, 11).Reset);
    }

    [Fact]
    public void ResetDoesNotRecountTheIntroductoryBlock()
    {
        IntroCounter counter = AtCyclePosition(469, 9);
        counter.Reset();

        Assert.Equal(15, counter.Definition.IntroCount);
        Assert.Equal(24, counter.ViewedCount);
    }

    // ---- Minus 10 and Minus 1 ----

    [Fact]
    public void Minus10NeedsPositionElevenAndSubtractsTen()
    {
        Assert.False(AtCyclePosition(469, 10).CanMinus10);
        Assert.True(AtCyclePosition(469, 11).CanMinus10);

        IntroCounter counter = AtCyclePosition(469, 34);
        counter.Minus10();
        Assert.Equal("24(15)/50", counter.Format());
    }

    [Fact]
    public void Minus10NeverGoesBelowOne()
    {
        IntroCounter counter = AtCyclePosition(469, 11);
        counter.Minus10();

        Assert.Equal(1, counter.CyclePosition);
    }

    [Fact]
    public void Minus1SmallGalleryNeedsPositionEleven()
    {
        // docs/TESTING.md wrote this as 10(5)/30; intro 5 with cycle 30 does not exist and the
        // user confirmed the typo. Cycle 30 means totals 228-299, where intro is 10.
        Assert.Equal("10(10)/30", AtCyclePosition(299, 10).Format());
        Assert.False(AtCyclePosition(299, 10).CanMinus1);
        Assert.True(AtCyclePosition(299, 11).CanMinus1);
    }

    [Theory]
    [InlineData(469, 35, "34(15)/50")]
    [InlineData(645, 55, "54(15)/70")]
    [InlineData(951, 85, "84(20)/100")]
    public void Minus1SubtractsOne(int total, int from, string expected)
    {
        IntroCounter counter = AtCyclePosition(total, from);
        counter.Minus1();

        Assert.Equal(expected, counter.Format());
    }

    [Fact]
    public void Minus1LargeGalleryNeedsTheWarningThreshold()
    {
        Assert.False(AtCyclePosition(469, 34).CanMinus1);
        Assert.True(AtCyclePosition(469, 35).CanMinus1);
    }

    // ---- Going backwards ----

    [Fact]
    public void GoingBackwardsWalksThePositionBack()
    {
        IntroCounter counter = AtCyclePosition(469, 20);

        counter.OnImageUnviewed();
        Assert.Equal("19(15)/50", counter.Format());

        counter.OnImageUnviewed();
        Assert.Equal("18(15)/50", counter.Format());
    }

    [Fact]
    public void GoingBackwardsStopsAtTheIntroductoryBlock()
    {
        IntroCounter counter = AtCyclePosition(469, 1);

        counter.OnImageUnviewed();
        counter.OnImageUnviewed();

        Assert.True(counter.IsIntroductory);
        Assert.Equal("1(15)/50", counter.Format());
    }

    [Fact]
    public void ForwardAfterBackwardReturnsToTheSamePosition()
    {
        IntroCounter counter = AtCyclePosition(469, 20);

        counter.OnImageUnviewed();
        counter.OnImageUnviewed();
        counter.OnImageViewed();
        counter.OnImageViewed();

        Assert.Equal("20(15)/50", counter.Format());
    }
}
