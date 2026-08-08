using ViewerPrn.Application.Session;
using ViewerPrn.Domain.FileSystem;
using ViewerPrn.Domain.Tabs;

namespace ViewerPrn.Infrastructure.Tests;

// ponytail: an Application-layer type tested from the Infrastructure test project, which already
// references it. Give it its own project when there is more than a screenful of these.
public sealed class SessionStateTests
{
    private static SessionState WithTabs(int count, int activeIndex) => new()
    {
        ActiveIndex = activeIndex,
        Tabs = [.. Enumerable.Range(0, count).Select(i => new TabState { Path = $@"C:\folder{i}" })],
    };

    [Fact]
    public void EmptySessionHasNoActiveTab()
    {
        SessionState state = SessionState.Empty.Sanitised();

        Assert.Empty(state.Tabs);
        Assert.Equal(-1, state.ActiveIndex);
    }

    [Fact]
    public void TabsBeyondTheLimitAreDropped()
    {
        SessionState state = WithTabs(40, 0).Sanitised();

        Assert.Equal(TabSet.MaxTabs, state.Tabs.Count);
    }

    [Fact]
    public void ExactlyTwentyFiveTabsSurvive()
    {
        SessionState state = WithTabs(TabSet.MaxTabs, 24).Sanitised();

        Assert.Equal(25, state.Tabs.Count);
        Assert.Equal(24, state.ActiveIndex);
    }

    [Fact]
    public void BlankPathsAreDropped()
    {
        SessionState state = new()
        {
            ActiveIndex = 0,
            Tabs = [new TabState { Path = "   " }, new TabState { Path = @"C:\real" }],
        };

        Assert.Equal(@"C:\real", Assert.Single(state.Sanitised().Tabs).Path);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(99, 2)]
    public void ActiveIndexIsClampedIntoRange(int stored, int expected)
    {
        Assert.Equal(expected, WithTabs(3, stored).Sanitised().ActiveIndex);
    }

    [Fact]
    public void ActiveIndexBecomesMinusOneWhenNoTabsSurvive()
    {
        SessionState state = new() { ActiveIndex = 3, Tabs = [] };

        Assert.Equal(-1, state.Sanitised().ActiveIndex);
    }

    [Fact]
    public void SortAndSelectionAreCarried()
    {
        TabState tab = new()
        {
            Path = @"C:\photos",
            Criterion = SortCriterion.Modified,
            Direction = SortDirection.Descending,
            SelectedNames = ["a.jpg", "b.jpg"],
        };

        TabState restored = Assert.Single(new SessionState { Tabs = [tab], ActiveIndex = 0 }.Sanitised().Tabs);

        Assert.Equal(SortCriterion.Modified, restored.Criterion);
        Assert.Equal(SortDirection.Descending, restored.Direction);
        Assert.Equal(["a.jpg", "b.jpg"], restored.SelectedNames);
    }
}
