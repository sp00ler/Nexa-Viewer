using ViewerPrn.Infrastructure.FileSystem;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class PathSuggestionsTests
{
    [Fact]
    public void TailOfAMissingPathMatchesBySubstring()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Combine("TushyRaw Ivy Ireland - 74x"));
        Directory.CreateDirectory(temp.Combine("Best of tushyraw 2024"));
        Directory.CreateDirectory(temp.Combine("Something else"));

        IReadOnlyList<string> matches = PathSuggestions.For(temp.Combine("TushyRaw"));

        Assert.Equal(
            [temp.Combine("Best of tushyraw 2024"), temp.Combine("TushyRaw Ivy Ireland - 74x")],
            matches);
    }

    [Fact]
    public void SearchClimbsToTheDeepestFolderThatExists()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Combine("TushyRaw Ivy Ireland - 74x"));

        // The whole tail is gone, not just the last segment: a pasted dead path still completes.
        IReadOnlyList<string> matches = PathSuggestions.For(
            Path.Combine(temp.Path, "19.12.2024", "Hardcore Photo Sets", "TushyRaw"));

        Assert.Equal([temp.Combine("TushyRaw Ivy Ireland - 74x")], matches);
    }

    [Fact]
    public void ArchivesAreOfferedAfterFolders()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Combine("set alpha"));
        File.WriteAllBytes(temp.Combine("set beta.zip"), []);
        File.WriteAllBytes(temp.Combine("set gamma.jpg"), []);

        IReadOnlyList<string> matches = PathSuggestions.For(temp.Combine("set"));

        Assert.Equal([temp.Combine("set alpha"), temp.Combine("set beta.zip")], matches);
    }

    [Fact]
    public void WildcardsAreHonouredWhenTyped()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Combine("IMG 2024 sorted"));
        Directory.CreateDirectory(temp.Combine("IMG 2023 raw"));

        Assert.Equal([temp.Combine("IMG 2024 sorted")], PathSuggestions.For(temp.Combine("IMG*sorted")));
    }

    [Fact]
    public void MatchesAreNaturallySorted()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Combine("set 10"));
        Directory.CreateDirectory(temp.Combine("set 2"));

        Assert.Equal([temp.Combine("set 2"), temp.Combine("set 10")], PathSuggestions.For(temp.Combine("set")));
    }

    [Fact]
    public void APathThatExistsSuggestsNothingUntilItEndsInASeparator()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Combine("inside"));

        Assert.Empty(PathSuggestions.For(temp.Path));
        Assert.Equal([temp.Combine("inside")], PathSuggestions.For(temp.Path + Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"Q:\no such drive\anything")]
    public void NothingToOfferIsAnEmptyList(string typed)
    {
        Assert.Empty(PathSuggestions.For(typed));
    }

    [Fact]
    public void QuotesFromAPastedPathAreIgnored()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Combine("quoted set"));

        Assert.Equal([temp.Combine("quoted set")], PathSuggestions.For($"\"{temp.Combine("quoted")}\""));
    }
}
