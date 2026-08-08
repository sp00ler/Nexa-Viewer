using ViewerPrn.Domain.Navigation;

namespace ViewerPrn.Domain.Tests;

public sealed class NavigationHistoryTests
{
    [Fact]
    public void AFreshHistoryGoesNowhere()
    {
        NavigationHistory history = new();

        Assert.Null(history.Current);
        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void WalksBackAndForwardThroughVisitedFolders()
    {
        NavigationHistory history = new();
        history.Visit(@"C:\a");
        history.Visit(@"C:\a\b");
        history.Visit(@"C:\a\b\c");

        Assert.Equal(@"C:\a\b", history.GoBack());
        Assert.Equal(@"C:\a", history.GoBack());
        Assert.False(history.CanGoBack);
        Assert.Equal(@"C:\a\b", history.GoForward());
        Assert.Equal(@"C:\a\b\c", history.GoForward());
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void GoingSomewhereNewDiscardsWhatWasAhead()
    {
        NavigationHistory history = new();
        history.Visit(@"C:\a");
        history.Visit(@"C:\b");
        history.GoBack();

        history.Visit(@"C:\c");

        Assert.False(history.CanGoForward);
        Assert.Equal(@"C:\c", history.Current);
        Assert.Equal(@"C:\a", history.GoBack());
    }

    [Fact]
    public void RevisitingTheCurrentFolderIsNotRecorded()
    {
        NavigationHistory history = new();
        history.Visit(@"C:\a");
        history.Visit(@"C:\A");

        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void GoingBackPastTheStartReturnsNothing()
    {
        NavigationHistory history = new();
        history.Visit(@"C:\a");

        Assert.Null(history.GoBack());
        Assert.Equal(@"C:\a", history.Current);
    }

    [Fact]
    public void AnEmptyPathIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new NavigationHistory().Visit("  "));
    }
}
