using ViewerPrn.Application.Session;
using ViewerPrn.Domain.FileSystem;
using ViewerPrn.Infrastructure.Session;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class JsonSessionStoreTests
{
    private static SessionState OneTab(string path) => new()
    {
        ActiveIndex = 0,
        Tabs = [new TabState
        {
            Path = path,
            Criterion = SortCriterion.Size,
            Direction = SortDirection.Descending,
            SelectedNames = ["pick.jpg"],
        }],
    };

    [Fact]
    public async Task NoSessionFileMeansNoTabs()
    {
        using TempDirectory temp = new();

        SessionState state = await new JsonSessionStore(temp.Combine("session.json")).LoadAsync();

        Assert.Empty(state.Tabs);
        Assert.Equal(-1, state.ActiveIndex);
    }

    [Fact]
    public async Task TabsSurviveARestart()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("session.json");

        await new JsonSessionStore(path).SaveAsync(OneTab(@"C:\photos"));
        SessionState restored = await new JsonSessionStore(path).LoadAsync();

        TabState tab = Assert.Single(restored.Tabs);
        Assert.Equal(@"C:\photos", tab.Path);
        Assert.Equal(SortCriterion.Size, tab.Criterion);
        Assert.Equal(SortDirection.Descending, tab.Direction);
        Assert.Equal("pick.jpg", Assert.Single(tab.SelectedNames));
        Assert.Equal(0, restored.ActiveIndex);
    }

    [Fact]
    public async Task TwentyFiveTabsSurviveARestart()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("session.json");
        SessionState state = new()
        {
            ActiveIndex = 12,
            Tabs = [.. Enumerable.Range(0, 25).Select(i => new TabState { Path = $@"C:\f{i}" })],
        };

        await new JsonSessionStore(path).SaveAsync(state);
        SessionState restored = await new JsonSessionStore(path).LoadAsync();

        Assert.Equal(25, restored.Tabs.Count);
        Assert.Equal(12, restored.ActiveIndex);
    }

    [Fact]
    public async Task CorruptSessionFileYieldsNoTabsAndIsLeftInPlace()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("session.json");
        await File.WriteAllTextAsync(path, "{ half a session");

        SessionState state = await new JsonSessionStore(path).LoadAsync();

        Assert.Empty(state.Tabs);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task AMissingFolderIsStillRestoredAsATab()
    {
        // The tab must come back and report the problem, not vanish silently.
        using TempDirectory temp = new();
        string path = temp.Combine("session.json");

        await new JsonSessionStore(path).SaveAsync(OneTab(@"X:\gone\forever"));
        SessionState restored = await new JsonSessionStore(path).LoadAsync();

        Assert.Equal(@"X:\gone\forever", Assert.Single(restored.Tabs).Path);
    }

    [Fact]
    public async Task OverwritingKeepsTheOldSessionAsABackup()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("session.json");
        JsonSessionStore store = new(path);

        await store.SaveAsync(OneTab(@"C:\first"));
        await store.SaveAsync(OneTab(@"C:\second"));

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("first", await File.ReadAllTextAsync(path + ".bak"), StringComparison.Ordinal);
        Assert.Contains("second", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInterruptedWriteLeavesThePreviousSessionIntact()
    {
        // Simulates a crash between the temporary file and the swap: the leftover .tmp must not
        // affect what the next start reads.
        using TempDirectory temp = new();
        string path = temp.Combine("session.json");
        await new JsonSessionStore(path).SaveAsync(OneTab(@"C:\committed"));
        await File.WriteAllTextAsync(path + ".tmp", "{ torn");

        SessionState restored = await new JsonSessionStore(path).LoadAsync();

        Assert.Equal(@"C:\committed", Assert.Single(restored.Tabs).Path);
    }
}
