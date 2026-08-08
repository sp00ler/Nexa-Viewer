using ViewerPrn.Domain.Tabs;

namespace ViewerPrn.Domain.Tests;

public sealed class TabSetTests
{
    private static TabSet WithTabs(int count)
    {
        TabSet tabs = new();
        for (int i = 0; i < count; i++)
        {
            tabs.Open($@"C:\folder{i}", $"folder{i}");
        }

        return tabs;
    }

    [Fact]
    public void StartsEmptyWithNoActiveTab()
    {
        TabSet tabs = new();

        Assert.Equal(0, tabs.Count);
        Assert.Equal(-1, tabs.ActiveIndex);
        Assert.Null(tabs.Active);
        Assert.True(tabs.CanOpen);
    }

    [Fact]
    public void OpeningATabMakesItActive()
    {
        TabSet tabs = WithTabs(3);

        Assert.Equal(3, tabs.Count);
        Assert.Equal(2, tabs.ActiveIndex);
        Assert.Equal("folder2", tabs.Active!.Title);
    }

    [Fact]
    public void TwentyFiveTabsAreAllowed()
    {
        TabSet tabs = WithTabs(TabSet.MaxTabs);

        Assert.Equal(25, tabs.Count);
        Assert.False(tabs.CanOpen);
    }

    [Fact]
    public void TwentySixthTabIsRejected()
    {
        TabSet tabs = WithTabs(TabSet.MaxTabs);

        Assert.Throws<InvalidOperationException>(() => tabs.Open(@"C:\overflow", "overflow"));
        Assert.Equal(25, tabs.Count);
    }

    [Fact]
    public void ClosingTheActiveTabActivatesTheOneOnTheRight()
    {
        TabSet tabs = WithTabs(3);
        tabs.Activate(1);

        tabs.Close(1);

        Assert.Equal(1, tabs.ActiveIndex);
        Assert.Equal("folder2", tabs.Active!.Title);
    }

    [Fact]
    public void ClosingTheLastTabActivatesTheOneOnTheLeft()
    {
        TabSet tabs = WithTabs(3);

        tabs.Close(2);

        Assert.Equal(1, tabs.ActiveIndex);
        Assert.Equal("folder1", tabs.Active!.Title);
    }

    [Fact]
    public void ClosingATabBeforeTheActiveOneKeepsTheSameTabActive()
    {
        TabSet tabs = WithTabs(3);
        tabs.Activate(2);

        tabs.Close(0);

        Assert.Equal(1, tabs.ActiveIndex);
        Assert.Equal("folder2", tabs.Active!.Title);
    }

    [Fact]
    public void ClosingTheOnlyTabLeavesNoActiveTab()
    {
        TabSet tabs = WithTabs(1);

        tabs.Close(0);

        Assert.Equal(0, tabs.Count);
        Assert.Equal(-1, tabs.ActiveIndex);
        Assert.Null(tabs.Active);
    }

    [Fact]
    public void ClosingFreesRoomForANewTab()
    {
        TabSet tabs = WithTabs(TabSet.MaxTabs);
        tabs.Close(0);

        Assert.True(tabs.CanOpen);
        tabs.Open(@"C:\again", "again");
        Assert.Equal(25, tabs.Count);
    }

    [Fact]
    public void MovingATabKeepsTheSameTabActive()
    {
        TabSet tabs = WithTabs(3);
        tabs.Activate(0);

        tabs.Move(0, 2);

        Assert.Equal(2, tabs.ActiveIndex);
        Assert.Equal("folder0", tabs.Active!.Title);
        Assert.Equal(["folder1", "folder2", "folder0"], tabs.Tabs.Select(t => t.Title));
    }

    [Fact]
    public void TabsHaveDistinctIdentities()
    {
        TabSet tabs = new();
        TabDescriptor first = tabs.Open(@"C:\same", "same");
        TabDescriptor second = tabs.Open(@"C:\same", "same");

        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void IndexOutsideTheSetIsRejected(int index)
    {
        TabSet tabs = WithTabs(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => tabs.Activate(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => tabs.Close(index));
    }

    [Fact]
    public void EmptyPathIsRejected()
    {
        TabSet tabs = new();

        Assert.Throws<ArgumentException>(() => tabs.Open("  ", "title"));
    }
}
