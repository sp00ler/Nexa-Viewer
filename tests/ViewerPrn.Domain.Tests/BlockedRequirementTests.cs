using ViewerPrn.Domain;
using ViewerPrn.Domain.Viewer;

namespace ViewerPrn.Domain.Tests;

/// <summary>
/// Guards the requirements the specification leaves undefined. These tests assert that the
/// code refuses to guess. Each one must be replaced by a real behavioural test once the
/// corresponding question is answered — they are the checklist of what is still open.
/// </summary>
public sealed class BlockedRequirementTests
{
    /// <summary>
    /// docs/TESTING.md:10 makes 469 -> intro 15, cycle 50 mandatory, but 469 falls inside the
    /// 300-500 range that docs/VIEWER.md:116 marks BLOCKED. Until that range is resolved the
    /// table cannot answer for 469 without inventing a rule.
    /// </summary>
    [Fact]
    public void Gallery469_TableLookupIsBlockedAlthoughTestingDocRequiresIt()
    {
        BlockedRequirementException exception =
            Assert.Throws<BlockedRequirementException>(() => CycleTable.Resolve(469));

        Assert.Contains("300-500", exception.Requirement, StringComparison.Ordinal);
    }

    [Fact]
    public void HelperCounterDisplayDuringTheIntroductoryBlockIsBlocked()
    {
        IntroCounter counter = IntroCounter.ForGallery(951);
        counter.OnImageViewed();

        Assert.True(counter.IsIntroductory);
        Assert.Throws<BlockedRequirementException>(() => counter.Format());
    }

    [Fact]
    public void StopControlIsBlocked()
    {
        IntroCounter counter = IntroCounter.ForGallery(951);

        Assert.Throws<BlockedRequirementException>(counter.Stop);
    }

    [Fact]
    public void ResetSeverityBeyondFiveResetsIsBlocked()
    {
        IntroCounter counter = new(new CycleDefinition(469, 15, 50));
        for (int i = 0; i < 16; i++)
        {
            counter.OnImageViewed();
        }

        for (int i = 0; i < 6; i++)
        {
            counter.Reset();
        }

        Assert.Equal(6, counter.ResetCount);
        Assert.Throws<BlockedRequirementException>(() => counter.ResetSeverity);
    }
}
