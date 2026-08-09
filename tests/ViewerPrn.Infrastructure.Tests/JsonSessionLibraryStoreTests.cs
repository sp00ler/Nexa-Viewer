using ViewerPrn.Application.Session;
using ViewerPrn.Infrastructure.Session;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class JsonSessionLibraryStoreTests
{
    private static SessionState Tabs(params string[] paths) => new()
    {
        ActiveIndex = 0,
        Tabs = [.. paths.Select(path => new TabState { Path = path })],
    };

    [Fact]
    public async Task NoFileMeansNoSavedStates()
    {
        using TempDirectory temp = new();

        SessionLibrary library = await new JsonSessionLibraryStore(temp.Combine("states.json")).LoadAsync();

        Assert.Empty(library.Sessions);
    }

    [Fact]
    public async Task ASavedStateSurvivesARestart()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("states.json");

        await new JsonSessionLibraryStore(path).SaveAsync(
            SessionLibrary.Empty.With("Работа", Tabs(@"C:\photos", @"C:\raw")));

        SessionLibrary restored = await new JsonSessionLibraryStore(path).LoadAsync();

        SavedSession saved = Assert.Single(restored.Sessions);
        Assert.Equal("Работа", saved.Name);
        Assert.Equal(2, saved.State.Tabs.Count);
        Assert.Equal(@"C:\raw", saved.State.Tabs[1].Path);
    }

    [Fact]
    public void SavingUnderAnExistingNameReplacesIt()
    {
        SessionLibrary library = SessionLibrary.Empty
            .With("scan", Tabs(@"C:\first"))
            .With("SCAN", Tabs(@"C:\second"));

        SavedSession saved = Assert.Single(library.Sessions);
        Assert.Equal(@"C:\second", Assert.Single(saved.State.Tabs).Path);
    }

    [Fact]
    public void DeletingIsByNameAndIgnoresCase()
    {
        SessionLibrary library = SessionLibrary.Empty
            .With("keep", Tabs(@"C:\a"))
            .With("drop", Tabs(@"C:\b"))
            .Without("DROP");

        Assert.Equal("keep", Assert.Single(library.Sessions).Name);
        Assert.Null(library.Find("drop"));
    }

    [Fact]
    public async Task MoreThanTwentyFiveTabsAreTrimmedOnTheWayIn()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("states.json");

        await new JsonSessionLibraryStore(path).SaveAsync(SessionLibrary.Empty.With(
            "huge",
            Tabs([.. Enumerable.Range(0, 40).Select(i => $@"C:\f{i}")])));

        SessionLibrary restored = await new JsonSessionLibraryStore(path).LoadAsync();

        Assert.Equal(25, Assert.Single(restored.Sessions).State.Tabs.Count);
    }

    [Fact]
    public async Task ACorruptFileYieldsNoStatesAndIsLeftInPlace()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("states.json");
        await File.WriteAllTextAsync(path, "{ half a library");

        SessionLibrary library = await new JsonSessionLibraryStore(path).LoadAsync();

        Assert.Empty(library.Sessions);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task BlankNamesAreDropped()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("states.json");
        await File.WriteAllTextAsync(
            path,
            """{ "Sessions": [ { "Name": "  ", "State": { "Tabs": [] } }, { "Name": "real", "State": { "Tabs": [] } } ] }""");

        SessionLibrary library = await new JsonSessionLibraryStore(path).LoadAsync();

        Assert.Equal("real", Assert.Single(library.Sessions).Name);
    }
}
