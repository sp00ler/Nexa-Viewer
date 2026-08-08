using ViewerPrn.Domain.Viewer;

namespace ViewerPrn.Domain.Tests;

/// <summary>
/// The mandatory Intro Counter scenarios from docs/TESTING.md:8-13.
/// </summary>
public sealed class IntroCounterTests
{
    /// <summary>
    /// The 469 scenarios use intro 15 / cycle 50. That definition is only produced by a
    /// total inside the BLOCKED 300-500 range, so the control tests construct it directly
    /// instead of going through <see cref="CycleTable"/>. The control-availability rules
    /// themselves (docs/VIEWER.md:81-113) are not blocked.
    /// </summary>
    private static readonly CycleDefinition Gallery469 = new(469, 15, 50);

    private static IntroCounter AtCyclePosition(CycleDefinition definition, int cyclePosition)
    {
        IntroCounter counter = new(definition);
        for (int i = 0; i < definition.IntroCount + cyclePosition; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal(cyclePosition, counter.CyclePosition);
        return counter;
    }

    [Fact]
    public void Gallery951_Physical21_IsFirstCyclePosition()
    {
        IntroCounter counter = IntroCounter.ForGallery(951);
        for (int i = 0; i < 21; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal(21, counter.ViewedCount);
        Assert.Equal("1(20)/100", counter.Format());
        Assert.False(counter.IsWarningActive);
    }

    [Fact]
    public void Gallery951_Physical105_IsWarningStart()
    {
        IntroCounter counter = IntroCounter.ForGallery(951);
        for (int i = 0; i < 104; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal("84(20)/100", counter.Format());
        Assert.False(counter.IsWarningActive);

        counter.OnImageViewed();

        Assert.Equal("85(20)/100", counter.Format());
        Assert.True(counter.IsWarningActive);
    }

    [Fact]
    public void Gallery469_Physical16_IsFirstCyclePosition()
    {
        IntroCounter counter = new(Gallery469);
        for (int i = 0; i < 16; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal("1(15)/50", counter.Format());
    }

    [Fact]
    public void Gallery469_Physical50_IsWarningStart()
    {
        IntroCounter counter = new(Gallery469);
        for (int i = 0; i < 50; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal("35(15)/50", counter.Format());
        Assert.True(counter.IsWarningActive);
    }

    [Fact]
    public void IntroductoryImagesAreViewedButDoNotAdvanceTheCycle()
    {
        IntroCounter counter = IntroCounter.ForGallery(951);
        for (int i = 0; i < 20; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal(20, counter.ViewedCount);
        Assert.Equal(0, counter.CyclePosition);
        Assert.True(counter.IsIntroductory);
    }

    /// <summary>
    /// Confirmed for v1: the cycle position does not wrap at the end of the cycle
    /// (DECISION-0002). For 951 the last physical image reads 931(20)/100.
    /// </summary>
    [Fact]
    public void CyclePositionGrowsPastCycleLength()
    {
        IntroCounter counter = IntroCounter.ForGallery(951);
        for (int i = 0; i < 951; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal("931(20)/100", counter.Format());
        Assert.True(counter.IsWarningActive);
    }

    // ---- Reset Cycle (docs/TESTING.md:19-23) ----

    [Fact]
    public void Reset_ReturnsToPositionOneAndCountsResets()
    {
        IntroCounter counter = AtCyclePosition(Gallery469, 1);

        counter.Reset();
        Assert.Equal("1(15)/50", counter.Format());
        Assert.Equal(1, counter.ResetCount);

        for (int i = 0; i < 8; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal("9(15)/50", counter.Format());
        counter.Reset();
        Assert.Equal("1(15)/50", counter.Format());
        Assert.Equal(2, counter.ResetCount);

        for (int i = 0; i < 3; i++)
        {
            counter.OnImageViewed();
        }

        Assert.Equal("4(15)/50", counter.Format());
        counter.Reset();
        Assert.Equal("1(15)/50", counter.Format());
        Assert.Equal(3, counter.ResetCount);
    }

    [Fact]
    public void Reset_DoesNotChangeTheIntroductoryCount()
    {
        IntroCounter counter = AtCyclePosition(Gallery469, 9);
        counter.Reset();

        Assert.Equal(15, counter.Definition.IntroCount);
        Assert.Equal(24, counter.ViewedCount);
    }

    [Theory]
    [InlineData(3, ResetSeverity.Normal)]
    [InlineData(4, ResetSeverity.Orange)]
    [InlineData(5, ResetSeverity.RedWithExclamation)]
    public void ResetSeverity_FollowsSpecifiedColours(int resets, ResetSeverity expected)
    {
        IntroCounter counter = AtCyclePosition(Gallery469, 1);
        for (int i = 0; i < resets; i++)
        {
            counter.Reset();
        }

        Assert.Equal(expected, counter.ResetSeverity);
    }

    [Fact]
    public void Reset_IsDisabledFromCyclePosition11()
    {
        IntroCounter counter = AtCyclePosition(Gallery469, 11);

        Assert.False(counter.CanReset);
        Assert.Throws<InvalidOperationException>(counter.Reset);
    }

    [Fact]
    public void Reset_IsDisabledDuringTheIntroductoryBlock()
    {
        IntroCounter counter = new(Gallery469);
        counter.OnImageViewed();

        Assert.False(counter.CanReset);
    }

    // ---- Minus 10 (docs/TESTING.md:25-28) ----

    [Fact]
    public void Minus10_IsDisabledAtPosition10AndEnabledAt11()
    {
        Assert.False(AtCyclePosition(Gallery469, 10).CanMinus10);
        Assert.True(AtCyclePosition(Gallery469, 11).CanMinus10);
    }

    [Fact]
    public void Minus10_Subtracts10()
    {
        IntroCounter counter = AtCyclePosition(Gallery469, 34);
        counter.Minus10();

        Assert.Equal("24(15)/50", counter.Format());
    }

    [Fact]
    public void Minus10_NeverProducesAPositionBelow1()
    {
        IntroCounter counter = AtCyclePosition(Gallery469, 11);
        counter.Minus10();

        Assert.Equal(1, counter.CyclePosition);
    }

    // ---- Minus 1 (docs/TESTING.md:30-34) ----

    [Fact]
    public void Minus1_SmallGallery_IsDisabledAtPosition10AndEnabledAt11()
    {
        // docs/TESTING.md:31 writes this case as 10(5)/30 and 11(5)/30. Intro 5 with cycle 30
        // is not producible by CycleTable (intro 5 belongs to totals 1-50, cycle 30 to
        // 228-299 where intro is 10). Both intro values are exercised; the availability rule
        // under test depends only on the total being <= 299.
        Assert.False(AtCyclePosition(new CycleDefinition(299, 10, 30), 10).CanMinus1);
        Assert.True(AtCyclePosition(new CycleDefinition(299, 10, 30), 11).CanMinus1);
        Assert.False(AtCyclePosition(new CycleDefinition(299, 5, 30), 10).CanMinus1);
        Assert.True(AtCyclePosition(new CycleDefinition(299, 5, 30), 11).CanMinus1);
    }

    [Fact]
    public void Minus1_LargeGallery_IsEnabledFromTheWarningThreshold()
    {
        Assert.False(AtCyclePosition(Gallery469, 34).CanMinus1);
        Assert.True(AtCyclePosition(Gallery469, 35).CanMinus1);
    }

    [Theory]
    [InlineData(469, 15, 50, 35, "34(15)/50")]
    [InlineData(645, 15, 70, 55, "54(15)/70")]
    [InlineData(951, 20, 100, 85, "84(20)/100")]
    public void Minus1_Subtracts1(int total, int intro, int cycle, int from, string expected)
    {
        IntroCounter counter = AtCyclePosition(new CycleDefinition(total, intro, cycle), from);
        counter.Minus1();

        Assert.Equal(expected, counter.Format());
    }

    // ---- Standard counter is never touched by the helper controls (docs/VIEWER.md:66) ----

    [Fact]
    public void StandardCounterIsIndependentOfTheHelperCounter()
    {
        IntroCounter counter = AtCyclePosition(Gallery469, 34);
        counter.Minus10();

        StandardCounter standard = StandardCounter.FromIndex(469, DisplayPosition.ToIndex(49));

        Assert.Equal("469/49", standard.ToString());
        Assert.Equal("24(15)/50", counter.Format());
    }
}
