using ViewerPrn.Domain.Viewer;

namespace ViewerPrn.Domain.Tests;

public sealed class ViewerNavigatorTests
{
    private static string[] Gallery(int count) => [.. Enumerable.Range(1, count).Select(i => $@"C:\g\img{i}.jpg")];

    /// <summary>Hands out a fixed sequence of indices so random navigation is reproducible.</summary>
    private static Func<int, int> Picks(params int[] indices)
    {
        int next = 0;
        return _ => indices[next++];
    }

    [Fact]
    public void FirstImageIsPositionOneNotZero()
    {
        ViewerNavigator navigator = new(Gallery(469), 0);

        Assert.Equal(1, navigator.DisplayPosition);
        Assert.Equal("1/469", navigator.Counter.ToString());
    }

    [Fact]
    public void CounterShowsCurrentThenTotal()
    {
        ViewerNavigator navigator = new(Gallery(469), 68);

        Assert.Equal("69/469", navigator.Counter.ToString());
    }

    [Fact]
    public void MovesForwardAndBackward()
    {
        ViewerNavigator navigator = new(Gallery(5), 2);

        Assert.True(navigator.MoveNext());
        Assert.Equal(4, navigator.DisplayPosition);
        Assert.True(navigator.MovePrevious());
        Assert.Equal(3, navigator.DisplayPosition);
        Assert.Equal(ViewerEdge.None, navigator.Edge);
    }

    // ---- Ends of the list stop and say so; they never wrap (DECISION-0023) ----

    [Fact]
    public void TheEndOfTheListStopsAndReportsIt()
    {
        ViewerNavigator navigator = new(Gallery(469), 468);

        Assert.False(navigator.MoveNext());
        Assert.Equal(469, navigator.DisplayPosition);
        Assert.Equal(ViewerEdge.End, navigator.Edge);

        Assert.False(navigator.MoveNext());
        Assert.Equal(469, navigator.DisplayPosition);
    }

    [Fact]
    public void TheStartOfTheListStopsAndReportsIt()
    {
        ViewerNavigator navigator = new(Gallery(469), 0);

        Assert.False(navigator.MovePrevious());
        Assert.Equal(1, navigator.DisplayPosition);
        Assert.Equal(ViewerEdge.Start, navigator.Edge);
    }

    [Fact]
    public void MovingAgainAfterAnEdgeClearsTheIndication()
    {
        ViewerNavigator navigator = new(Gallery(5), 4);
        navigator.MoveNext();

        Assert.Equal(ViewerEdge.End, navigator.Edge);
        navigator.MovePrevious();
        Assert.Equal(ViewerEdge.None, navigator.Edge);
    }

    [Fact]
    public void ASingleImageGalleryIsAtBothEnds()
    {
        ViewerNavigator navigator = new(Gallery(1), 0);

        Assert.False(navigator.CanMoveNext);
        Assert.False(navigator.CanMovePrevious);
        Assert.Equal("1/1", navigator.Counter.ToString());
    }

    // ---- Random navigation is a history (docs/TESTING.md:15) ----

    [Fact]
    public void BackspaceWalksBackThroughWhatWasSeen()
    {
        // The scenario from docs/TESTING.md: 35 -> 102 -> 17 -> 88, then back to 17, 102, 35.
        ViewerNavigator navigator = new(Gallery(200), 34, Picks(101, 16, 87));

        navigator.MoveRandom();
        navigator.MoveRandom();
        navigator.MoveRandom();
        Assert.Equal(88, navigator.DisplayPosition);

        Assert.True(navigator.MoveBack());
        Assert.Equal(17, navigator.DisplayPosition);
        Assert.True(navigator.MoveBack());
        Assert.Equal(102, navigator.DisplayPosition);
        Assert.True(navigator.MoveBack());
        Assert.Equal(35, navigator.DisplayPosition);
    }

    [Fact]
    public void ForwardAfterBackRetracesInsteadOfDrawingANewImage()
    {
        // The picker offers only three indices; a fourth call would throw, which is exactly the
        // point — going forward after going back must not draw anything new.
        ViewerNavigator navigator = new(Gallery(200), 34, Picks(101, 16, 87));
        navigator.MoveRandom();
        navigator.MoveRandom();
        navigator.MoveRandom();

        navigator.MoveBack();
        navigator.MoveBack();
        Assert.Equal(102, navigator.DisplayPosition);
        Assert.True(navigator.CanRetraceForward);

        Assert.True(navigator.MoveRandom());
        Assert.Equal(17, navigator.DisplayPosition);
        Assert.True(navigator.MoveRandom());
        Assert.Equal(88, navigator.DisplayPosition);
        Assert.False(navigator.CanRetraceForward);
    }

    [Fact]
    public void GoingForwardPastTheHistoryDrawsANewImage()
    {
        ViewerNavigator navigator = new(Gallery(200), 0, Picks(10, 20));
        navigator.MoveRandom();
        navigator.MoveBack();
        navigator.MoveRandom();

        Assert.Equal(11, navigator.DisplayPosition);
        Assert.False(navigator.CanRetraceForward);

        navigator.MoveRandom();
        Assert.Equal(21, navigator.DisplayPosition);
    }

    [Fact]
    public void GoingBackPastTheStartOfTheHistoryStops()
    {
        ViewerNavigator navigator = new(Gallery(10), 0, Picks(5));
        navigator.MoveRandom();
        navigator.MoveBack();

        Assert.False(navigator.MoveBack());
        Assert.Equal(1, navigator.DisplayPosition);
        Assert.Equal(ViewerEdge.Start, navigator.Edge);
    }

    [Fact]
    public void RandomAvoidsRepeatingTheImageItIsAlreadyOn()
    {
        // The picker offers the current index twice before offering a different one.
        ViewerNavigator navigator = new(Gallery(10), 3, Picks(3, 3, 7));
        navigator.MoveRandom();

        Assert.Equal(8, navigator.DisplayPosition);
    }

    [Fact]
    public void RandomOnASingleImageGalleryGoesNowhere()
    {
        ViewerNavigator navigator = new(Gallery(1), 0);

        Assert.False(navigator.MoveRandom());
        Assert.Equal(1, navigator.DisplayPosition);
    }

    // ---- Gallery seen through ----

    [Fact]
    public void AGalleryIsNotExhaustedUntilEveryImageHasBeenOnScreen()
    {
        ViewerNavigator navigator = new(Gallery(3), 0);

        Assert.False(navigator.GalleryExhausted);
        navigator.MoveNext();
        Assert.False(navigator.GalleryExhausted);
        navigator.MoveNext();
        Assert.True(navigator.GalleryExhausted);
    }

    [Fact]
    public void RevisitingDoesNotCountTwice()
    {
        ViewerNavigator navigator = new(Gallery(3), 0);

        navigator.MoveNext();
        navigator.MovePrevious();
        navigator.MoveNext();

        Assert.False(navigator.GalleryExhausted);
    }

    [Fact]
    public void RandomJumpsExhaustTheGalleryToo()
    {
        ViewerNavigator navigator = new(Gallery(3), 0, Picks(2, 1)) { Mode = ViewerMode.Random };

        navigator.MoveRandom();
        Assert.False(navigator.GalleryExhausted);
        navigator.MoveRandom();
        Assert.True(navigator.GalleryExhausted);
    }

    [Fact]
    public void ASingleImageGalleryIsExhaustedFromTheStart()
    {
        Assert.True(new ViewerNavigator(Gallery(1), 0).GalleryExhausted);
    }

    // ---- Construction ----

    [Fact]
    public void AnEmptyGalleryIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ViewerNavigator([], 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void AStartIndexOutsideTheGalleryIsRejected(int startIndex)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewerNavigator(Gallery(5), startIndex));
    }
}
