using System.Globalization;
using Microsoft.Data.Sqlite;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Infrastructure.Database;

namespace ViewerPrn.Infrastructure.Favorites;

/// <summary>Favourite groups and their targets, stored locally (docs/DATABASE.md).</summary>
public sealed class SqliteFavoritesService : IFavoritesService
{
    private readonly NexaDatabase _database;

    public SqliteFavoritesService(NexaDatabase database) => _database = database;

    public Task<IReadOnlyList<FavoriteGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<FavoriteGroup>>(
            () =>
            {
                using SqliteConnection connection = _database.Open();
                Dictionary<long, string> names = [];

                using (SqliteCommand groups = connection.CreateCommand())
                {
                    groups.CommandText = "SELECT Id, Name FROM FavoriteGroups ORDER BY SortOrder, Name;";
                    using SqliteDataReader reader = groups.ExecuteReader();
                    while (reader.Read())
                    {
                        names[reader.GetInt64(0)] = reader.GetString(1);
                    }
                }

                Dictionary<long, List<Favorite>> items = names.Keys.ToDictionary(id => id, _ => new List<Favorite>());

                using (SqliteCommand favorites = connection.CreateCommand())
                {
                    favorites.CommandText =
                        "SELECT Id, GroupId, Path, Kind, AddedUtc FROM Favorites ORDER BY AddedUtc;";
                    using SqliteDataReader reader = favorites.ExecuteReader();
                    while (reader.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        long groupId = reader.GetInt64(1);
                        string path = reader.GetString(2);

                        if (!items.TryGetValue(groupId, out List<Favorite>? bucket))
                        {
                            continue;
                        }

                        bucket.Add(new Favorite(
                            reader.GetInt64(0),
                            groupId,
                            path,
                            (FavoriteKind)reader.GetInt32(3),
                            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),

                            // Checked on read: a target can disappear at any time, and the user
                            // needs to see that rather than find out by clicking it.
                            File.Exists(path) || Directory.Exists(path)));
                    }
                }

                return [.. names.Select(pair => new FavoriteGroup(pair.Key, pair.Value, items[pair.Key]))];
            },
            cancellationToken);

    public Task<long> CreateGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Task.Run(
            () =>
            {
                using SqliteConnection connection = _database.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO FavoriteGroups (Name) VALUES ($name); SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$name", name.Trim());
                return (long)command.ExecuteScalar()!;
            },
            cancellationToken);
    }

    public Task RenameGroupAsync(long groupId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ExecuteAsync(
            "UPDATE FavoriteGroups SET Name = $name WHERE Id = $id;",
            command =>
            {
                command.Parameters.AddWithValue("$name", name.Trim());
                command.Parameters.AddWithValue("$id", groupId);
            },
            cancellationToken);
    }

    public Task DeleteGroupAsync(long groupId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "DELETE FROM FavoriteGroups WHERE Id = $id;",
            command => command.Parameters.AddWithValue("$id", groupId),
            cancellationToken);

    public Task AddAsync(long groupId, string path, FavoriteKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // The same target may belong to several groups, so the uniqueness is per group, and
        // adding it twice to one group is simply nothing to do.
        return ExecuteAsync(
            """
            INSERT INTO Favorites (GroupId, Path, Kind, AddedUtc)
            VALUES ($group, $path, $kind, $added)
            ON CONFLICT (GroupId, Path) DO NOTHING;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$group", groupId);
                command.Parameters.AddWithValue("$path", path);
                command.Parameters.AddWithValue("$kind", (int)kind);
                command.Parameters.AddWithValue("$added", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            },
            cancellationToken);
    }

    public Task RemoveAsync(long favoriteId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "DELETE FROM Favorites WHERE Id = $id;",
            command => command.Parameters.AddWithValue("$id", favoriteId),
            cancellationToken);

    public Task RepairAsync(long favoriteId, string newPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
        return ExecuteAsync(
            "UPDATE Favorites SET Path = $path WHERE Id = $id;",
            command =>
            {
                command.Parameters.AddWithValue("$path", newPath);
                command.Parameters.AddWithValue("$id", favoriteId);
            },
            cancellationToken);
    }

    private Task ExecuteAsync(string sql, Action<SqliteCommand> bind, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                using SqliteConnection connection = _database.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = sql;
                bind(command);
                command.ExecuteNonQuery();
            },
            cancellationToken);
}
