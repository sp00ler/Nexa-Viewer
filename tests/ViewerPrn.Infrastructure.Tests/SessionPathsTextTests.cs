using ViewerPrn.Application.Session;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class SessionPathsTextTests
{
    [Fact]
    public void EveryTabIsOneLineInTabOrder()
    {
        SessionState state = new()
        {
            ActiveIndex = 1,
            Tabs = [new TabState { Path = @"E:\one" }, new TabState { Path = @"E:\two" }],
        };

        Assert.Equal([@"E:\one", @"E:\two"], SessionPathsText.ToLines(state));
    }

    [Fact]
    public void BlankLinesCommentsAndQuotesAreDroppedOnTheWayBack()
    {
        string[] lines = ["", "   ", "# a comment", "\"E:\\quoted\"", @"  E:\spaced  "];

        Assert.Equal([@"E:\quoted", @"E:\spaced"], SessionPathsText.ParseLines(lines));
    }

    [Fact]
    public void WhatIsWrittenIsWhatIsReadBack()
    {
        SessionState state = new()
        {
            ActiveIndex = 0,
            Tabs =
            [
                new TabState { Path = @"E:\photos\2024" },
                new TabState { Path = @"E:\photos\trip.zip\day1" },
                new TabState { Path = @"\\nas\share\raw" },
            ],
        };

        Assert.Equal(
            SessionPathsText.ToLines(state),
            SessionPathsText.ParseLines(SessionPathsText.ToLines(state)));
    }
}
