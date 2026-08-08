using ViewerPrn.Application.Abstractions;
using ViewerPrn.Infrastructure.Database;
using ViewerPrn.Infrastructure.Favorites;
using ViewerPrn.Infrastructure.Statistics;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class FavoritesAndStatisticsTests
{
    private static NexaDatabase FreshDatabase(TempDirectory temp)
    {
        NexaDatabase database = new(temp.Combine("nexa.db"));
        database.Migrate();
        return database;
    }

    // ---- Migrations ----

    [Fact]
    public void MigratingTwiceIsHarmless()
    {
        using TempDirectory temp = new();
        NexaDatabase database = new(temp.Combine("nexa.db"));

        database.Migrate();
        database.Migrate();

        Assert.True(File.Exists(temp.Combine("nexa.db")));
    }

    // ---- Favourites ----

    [Fact]
    public async Task GroupsAndTargetsRoundTrip()
    {
        using TempDirectory temp = new();
        SqliteFavoritesService favorites = new(FreshDatabase(temp));
        string folder = Directory.CreateDirectory(temp.Combine("photos")).FullName;

        long group = await favorites.CreateGroupAsync("Trips");
        await favorites.AddAsync(group, folder, FavoriteKind.Folder);

        FavoriteGroup stored = Assert.Single(await favorites.GetGroupsAsync());
        Assert.Equal("Trips", stored.Name);
        Favorite item = Assert.Single(stored.Items);
        Assert.Equal(folder, item.Path);
        Assert.Equal(FavoriteKind.Folder, item.Kind);
        Assert.True(item.Exists);
    }

    [Fact]
    public async Task OneTargetCanBelongToSeveralGroups()
    {
        using TempDirectory temp = new();
        SqliteFavoritesService favorites = new(FreshDatabase(temp));
        string folder = Directory.CreateDirectory(temp.Combine("photos")).FullName;

        long first = await favorites.CreateGroupAsync("Trips");
        long second = await favorites.CreateGroupAsync("Best of");
        await favorites.AddAsync(first, folder, FavoriteKind.Folder);
        await favorites.AddAsync(second, folder, FavoriteKind.Folder);

        IReadOnlyList<FavoriteGroup> groups = await favorites.GetGroupsAsync();
        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Single(group.Items));
    }

    [Fact]
    public async Task AddingTheSameTargetTwiceToOneGroupChangesNothing()
    {
        using TempDirectory temp = new();
        SqliteFavoritesService favorites = new(FreshDatabase(temp));
        string folder = Directory.CreateDirectory(temp.Combine("photos")).FullName;
        long group = await favorites.CreateGroupAsync("Trips");

        await favorites.AddAsync(group, folder, FavoriteKind.Folder);
        await favorites.AddAsync(group, folder, FavoriteKind.Folder);

        Assert.Single(Assert.Single(await favorites.GetGroupsAsync()).Items);
    }

    [Fact]
    public async Task AMissingTargetIsReportedAsBrokenRatherThanHidden()
    {
        using TempDirectory temp = new();
        SqliteFavoritesService favorites = new(FreshDatabase(temp));
        long group = await favorites.CreateGroupAsync("Trips");
        await favorites.AddAsync(group, temp.Combine("gone"), FavoriteKind.Folder);

        Favorite item = Assert.Single(Assert.Single(await favorites.GetGroupsAsync()).Items);

        Assert.False(item.Exists);
    }

    [Fact]
    public async Task ABrokenTargetCanBeRepaired()
    {
        using TempDirectory temp = new();
        SqliteFavoritesService favorites = new(FreshDatabase(temp));
        long group = await favorites.CreateGroupAsync("Trips");
        await favorites.AddAsync(group, temp.Combine("gone"), FavoriteKind.Folder);
        string moved = Directory.CreateDirectory(temp.Combine("moved")).FullName;

        Favorite broken = Assert.Single(Assert.Single(await favorites.GetGroupsAsync()).Items);
        await favorites.RepairAsync(broken.Id, moved);

        Favorite repaired = Assert.Single(Assert.Single(await favorites.GetGroupsAsync()).Items);
        Assert.Equal(moved, repaired.Path);
        Assert.True(repaired.Exists);
    }

    [Fact]
    public async Task DeletingAGroupTakesItsTargetsWithIt()
    {
        using TempDirectory temp = new();
        SqliteFavoritesService favorites = new(FreshDatabase(temp));
        long group = await favorites.CreateGroupAsync("Trips");
        await favorites.AddAsync(group, temp.Path, FavoriteKind.Folder);

        await favorites.DeleteGroupAsync(group);

        Assert.Empty(await favorites.GetGroupsAsync());
    }

    [Fact]
    public async Task GroupsCanBeRenamed()
    {
        using TempDirectory temp = new();
        SqliteFavoritesService favorites = new(FreshDatabase(temp));
        long group = await favorites.CreateGroupAsync("Trips");

        await favorites.RenameGroupAsync(group, "Holidays");

        Assert.Equal("Holidays", Assert.Single(await favorites.GetGroupsAsync()).Name);
    }

    [Fact]
    public async Task RemovingOneTargetLeavesTheGroup()
    {
        using TempDirectory temp = new();
        SqliteFavoritesService favorites = new(FreshDatabase(temp));
        long group = await favorites.CreateGroupAsync("Trips");
        await favorites.AddAsync(group, temp.Path, FavoriteKind.Folder);

        Favorite item = Assert.Single(Assert.Single(await favorites.GetGroupsAsync()).Items);
        await favorites.RemoveAsync(item.Id);

        Assert.Empty(Assert.Single(await favorites.GetGroupsAsync()).Items);
    }

    // ---- Statistics ----

    [Fact]
    public async Task ASessionRecordsViewsAndTotals()
    {
        using TempDirectory temp = new();
        SqliteViewStatisticsService statistics = new(FreshDatabase(temp));

        long session = await statistics.StartSessionAsync(@"E:\photos");
        statistics.RecordImageView(session, @"E:\photos\a.jpg", 1);
        statistics.RecordImageView(session, @"E:\photos\b.jpg", 2);
        statistics.RecordImageView(session, @"E:\photos\a.jpg", 1);
        await statistics.EndSessionAsync(session);

        ViewStatistics stored = Assert.IsType<ViewStatistics>(await statistics.GetAsync(@"E:\photos"));
        Assert.Equal(1, stored.Sessions);
        Assert.Equal(3, stored.TotalImageViews);
        Assert.Equal(2, stored.UniqueImages);
        Assert.Equal(@"E:\photos\a.jpg", stored.LastImagePath);
        Assert.Equal(1, stored.LastPosition);
    }

    [Fact]
    public async Task SessionsAccumulateAcrossVisits()
    {
        using TempDirectory temp = new();
        SqliteViewStatisticsService statistics = new(FreshDatabase(temp));

        for (int visit = 0; visit < 3; visit++)
        {
            long session = await statistics.StartSessionAsync(@"E:\photos");
            statistics.RecordImageView(session, @"E:\photos\a.jpg", 1);
            await statistics.EndSessionAsync(session);
        }

        ViewStatistics stored = Assert.IsType<ViewStatistics>(await statistics.GetAsync(@"E:\photos"));
        Assert.Equal(3, stored.Sessions);
        Assert.Equal(3, stored.TotalImageViews);
        Assert.Equal(1, stored.UniqueImages);
        Assert.True(stored.LastViewed >= stored.FirstViewed);
    }

    [Fact]
    public async Task NavigationDoesNotWriteUntilAskedTo()
    {
        using TempDirectory temp = new();
        SqliteViewStatisticsService statistics = new(FreshDatabase(temp));
        long session = await statistics.StartSessionAsync(@"E:\photos");

        statistics.RecordImageView(session, @"E:\photos\a.jpg", 1);

        // Nothing is aggregated before the session ends: recording only touches memory.
        Assert.Null(await statistics.GetAsync(@"E:\photos"));

        await statistics.FlushAsync();
        await statistics.EndSessionAsync(session);
        Assert.NotNull(await statistics.GetAsync(@"E:\photos"));
    }

    [Fact]
    public async Task AnUnknownSourceHasNoStatistics()
    {
        using TempDirectory temp = new();
        SqliteViewStatisticsService statistics = new(FreshDatabase(temp));

        Assert.Null(await statistics.GetAsync(@"E:\never-opened"));
    }

    [Fact]
    public async Task RecordingAgainstAnUnknownSessionIsIgnored()
    {
        using TempDirectory temp = new();
        SqliteViewStatisticsService statistics = new(FreshDatabase(temp));

        statistics.RecordImageView(9999, @"E:\photos\a.jpg", 1);
        await statistics.FlushAsync();

        Assert.Null(await statistics.GetAsync(@"E:\photos"));
    }

    [Fact]
    public async Task ABufferLargerThanTheFlushThresholdIsWrittenInOneBatch()
    {
        using TempDirectory temp = new();
        SqliteViewStatisticsService statistics = new(FreshDatabase(temp));
        long session = await statistics.StartSessionAsync(@"E:\photos");

        for (int i = 0; i < 200; i++)
        {
            statistics.RecordImageView(session, $@"E:\photos\img{i}.jpg", i + 1);
        }

        await statistics.EndSessionAsync(session);

        ViewStatistics stored = Assert.IsType<ViewStatistics>(await statistics.GetAsync(@"E:\photos"));
        Assert.Equal(200, stored.TotalImageViews);
        Assert.Equal(200, stored.UniqueImages);
    }
}
