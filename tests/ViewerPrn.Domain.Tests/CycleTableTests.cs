using ViewerPrn.Domain.Viewer;

namespace ViewerPrn.Domain.Tests;

/// <summary>
/// The boundary set from docs/TESTING.md:6, plus the bands above 1199 that the user settled on
/// 2026-08-08.
/// </summary>
public sealed class CycleTableTests
{
    [Theory]
    // 1-50: intro 5, no cycle at all
    [InlineData(1, 5, CycleTable.NoCycle)]
    [InlineData(5, 5, CycleTable.NoCycle)]
    [InlineData(50, 5, CycleTable.NoCycle)]
    // 51-77: intro = ceil(N/10), cycle 5
    [InlineData(51, 6, 5)]
    [InlineData(59, 6, 5)]
    [InlineData(60, 6, 5)]
    [InlineData(77, 8, 5)]
    // 78-127: intro 10, cycle 5
    [InlineData(78, 10, 5)]
    [InlineData(127, 10, 5)]
    // 128-177: intro 15, cycle 7
    [InlineData(128, 15, 7)]
    [InlineData(177, 15, 7)]
    // 178-227: intro 20, cycle 10
    [InlineData(178, 20, 10)]
    [InlineData(227, 20, 10)]
    // 228-299: intro 10, cycle 30
    [InlineData(228, 10, 30)]
    [InlineData(299, 10, 30)]
    // 300-799: intro 15, cycle = ceil(N/100)*10 - the formula now covers the whole range
    [InlineData(300, 15, 50)]
    [InlineData(345, 15, 50)]
    [InlineData(469, 15, 50)]
    [InlineData(500, 15, 50)]
    [InlineData(501, 15, 60)]
    [InlineData(505, 15, 60)]
    [InlineData(645, 15, 70)]
    [InlineData(769, 15, 80)]
    [InlineData(799, 15, 80)]
    // 800-1199: intro 20
    [InlineData(800, 20, 80)]
    [InlineData(951, 20, 100)]
    [InlineData(1199, 20, 120)]
    // Further bands of 400, intro five higher each time
    [InlineData(1200, 25, 120)]
    [InlineData(1599, 25, 160)]
    [InlineData(1600, 30, 160)]
    [InlineData(9999, 130, 1000)]
    public void ResolvesIntroAndCycle(int total, int expectedIntro, int expectedCycle)
    {
        CycleDefinition definition = CycleTable.Resolve(total);

        Assert.Equal(total, definition.TotalImages);
        Assert.Equal(expectedIntro, definition.IntroCount);
        Assert.Equal(expectedCycle, definition.CycleLength);
    }

    [Fact]
    public void TheMandatoryExamplesFromTheSpecification()
    {
        Assert.Equal(new CycleDefinition(951, 20, 100), CycleTable.Resolve(951));
        Assert.Equal(new CycleDefinition(469, 15, 50), CycleTable.Resolve(469));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000)]
    public void TotalsOutsideTheSupportedRangeAreRejected(int total)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CycleTable.Resolve(total));
    }

    [Fact]
    public void CycleLengthNeverDropsAsTheGalleryGrows()
    {
        // A gallery gaining one image must not shrink its cycle. Guards the band arithmetic.
        int previous = 0;
        for (int total = 300; total <= CycleTable.MaxSupportedTotal; total += 7)
        {
            int cycle = CycleTable.Resolve(total).CycleLength;
            Assert.True(cycle >= previous, $"cycle went backwards at {total}");
            previous = cycle;
        }
    }

    [Fact]
    public void IntroNeverDropsAboveThreeHundred()
    {
        int previous = 0;
        for (int total = 300; total <= CycleTable.MaxSupportedTotal; total += 7)
        {
            int intro = CycleTable.Resolve(total).IntroCount;
            Assert.True(intro >= previous, $"intro went backwards at {total}");
            previous = intro;
        }
    }
}
