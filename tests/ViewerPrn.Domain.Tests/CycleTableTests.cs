using ViewerPrn.Domain;
using ViewerPrn.Domain.Viewer;

namespace ViewerPrn.Domain.Tests;

/// <summary>
/// Covers the boundary set from docs/TESTING.md:6.
/// </summary>
public sealed class CycleTableTests
{
    [Theory]
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
    // 501-799: intro 15, cycle = ceil(N/100)*10
    [InlineData(501, 15, 60)]
    [InlineData(505, 15, 60)]
    [InlineData(645, 15, 70)]
    [InlineData(799, 15, 80)]
    // 800-1199: intro 20, cycle = ceil(N/100)*10
    [InlineData(800, 20, 80)]
    [InlineData(951, 20, 100)]
    [InlineData(1199, 20, 120)]
    public void Resolve_ReturnsSpecifiedIntroAndCycle(int total, int expectedIntro, int expectedCycle)
    {
        CycleDefinition definition = CycleTable.Resolve(total);

        Assert.Equal(total, definition.TotalImages);
        Assert.Equal(expectedIntro, definition.IntroCount);
        Assert.Equal(expectedCycle, definition.CycleLength);
    }

    [Theory]
    [InlineData(1)]     // 1-50: "special `-` state", display never specified
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(300)]   // 300-500: docs/VIEWER.md:116 forbids inventing this range
    [InlineData(469)]   // see IntroCounterBlockedRequirementTests for why this one matters
    [InlineData(500)]
    [InlineData(1200)]  // >1199: unresolved continuation
    public void Resolve_ThrowsForBlockedRanges(int total)
    {
        Assert.Throws<BlockedRequirementException>(() => CycleTable.Resolve(total));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Resolve_RejectsNonPositiveTotals(int total)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CycleTable.Resolve(total));
    }
}
