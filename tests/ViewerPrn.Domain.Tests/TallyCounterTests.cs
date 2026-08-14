using ViewerPrn.Domain.Viewer;

namespace ViewerPrn.Domain.Tests;

/// <summary>The "cum" / "ANALize" click counters (specs/viewer-counter-controls.md).</summary>
public sealed class TallyCounterTests
{
    [Fact]
    public void ZeroRendersAsNothingAndTheFirstClickShowsOne()
    {
        TallyCounter counter = new();

        Assert.Equal(string.Empty, counter.Format());

        counter.Click(bracketValue: 15);
        Assert.Equal("1", counter.Format());
        Assert.False(counter.IsHot);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(10)]
    public void ReachingATresholdBracketTurnsTheCountRedWithAnExclamation(int bracket)
    {
        TallyCounter counter = new();

        for (int i = 0; i < bracket - 1; i++)
        {
            counter.Click(bracket);
        }

        Assert.False(counter.IsHot);
        counter.Click(bracket);
        Assert.True(counter.IsHot);
        Assert.Equal($"{bracket}!", counter.Format());

        counter.Click(bracket);
        Assert.Equal($"{bracket + 1}!", counter.Format());
    }

    [Fact]
    public void BracketsOtherThanFiveSevenTenCarryNoThreshold()
    {
        TallyCounter counter = new();

        for (int i = 0; i < 25; i++)
        {
            counter.Click(bracketValue: 20);
        }

        Assert.False(counter.IsHot);
        Assert.Equal("25", counter.Format());
    }

    [Fact]
    public void OnceHotStaysHotEvenWhenTheBracketChanges()
    {
        TallyCounter counter = new();
        for (int i = 0; i < 7; i++)
        {
            counter.Click(bracketValue: 7);
        }

        Assert.True(counter.IsHot);

        // A new gallery with no threshold does not cool the counter down.
        counter.Click(bracketValue: 20);
        Assert.Equal("8!", counter.Format());
    }

    [Fact]
    public void IgniteSetsFiveHotAndNeverLowersAHigherCount()
    {
        TallyCounter fresh = new();
        fresh.Ignite();
        Assert.Equal("5!", fresh.Format());

        TallyCounter low = new();
        low.Click(20);
        low.Click(20);
        low.Ignite();
        Assert.Equal("5!", low.Format());

        TallyCounter high = new();
        for (int i = 0; i < 8; i++)
        {
            high.Click(20);
        }

        high.Ignite();
        Assert.Equal("8!", high.Format());
    }
}
