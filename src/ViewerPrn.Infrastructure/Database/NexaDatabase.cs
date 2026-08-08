using Microsoft.Data.Sqlite;
using ViewerPrn.Application.Abstractions;

namespace ViewerPrn.Infrastructure.Database;

/// <summary>
/// The local SQLite database and its migrations (docs/DATABASE.md). Nothing here ever leaves
/// the machine.
/// </summary>
public sealed class NexaDatabase
{
    private readonly string _connectionString;
    private readonly ILoggingService? _logger;

    public NexaDatabase(string databaseFilePath, ILoggingService? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFilePath);

        string? directory = Path.GetDirectoryName(databaseFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _logger = logger;
    }

    /// <summary>Each migration is applied once, in order, tracked by SQLite's own user_version.</summary>
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE FavoriteGroups (
            Id        INTEGER PRIMARY KEY AUTOINCREMENT,
            Name      TEXT    NOT NULL UNIQUE,
            SortOrder INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE Favorites (
            Id       INTEGER PRIMARY KEY AUTOINCREMENT,
            GroupId  INTEGER NOT NULL REFERENCES FavoriteGroups(Id) ON DELETE CASCADE,
            Path     TEXT    NOT NULL,
            Kind     INTEGER NOT NULL,
            AddedUtc TEXT    NOT NULL,
            UNIQUE (GroupId, Path)
        );

        CREATE INDEX IX_Favorites_Group ON Favorites (GroupId);

        CREATE TABLE ViewSessions (
            Id           INTEGER PRIMARY KEY AUTOINCREMENT,
            SourcePath   TEXT    NOT NULL,
            StartedUtc   TEXT    NOT NULL,
            EndedUtc     TEXT,
            ImagesViewed INTEGER NOT NULL DEFAULT 0,
            LastPosition INTEGER,
            Mode         INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IX_ViewSessions_Source ON ViewSessions (SourcePath);

        CREATE TABLE ViewAggregates (
            SourcePath      TEXT    PRIMARY KEY,
            FirstViewedUtc  TEXT    NOT NULL,
            LastViewedUtc   TEXT    NOT NULL,
            Sessions        INTEGER NOT NULL DEFAULT 0,
            TotalSeconds    REAL    NOT NULL DEFAULT 0,
            TotalImageViews INTEGER NOT NULL DEFAULT 0,
            LastImagePath   TEXT,
            LastPosition    INTEGER
        );

        CREATE TABLE ImageViews (
            SourcePath    TEXT    NOT NULL,
            ImagePath     TEXT    NOT NULL,
            Views         INTEGER NOT NULL DEFAULT 0,
            LastViewedUtc TEXT    NOT NULL,
            PRIMARY KEY (SourcePath, ImagePath)
        );
        """,
    ];

    public SqliteConnection Open()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();

        using SqliteCommand pragma = connection.CreateCommand();

        // WAL keeps readers out of writers' way; foreign keys are off by default in SQLite and
        // the schema relies on them for cascade deletes.
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    public void Migrate()
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "PRAGMA user_version;";
        int applied = Convert.ToInt32(read.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        for (int version = applied; version < Migrations.Length; version++)
        {
            using SqliteCommand migrate = connection.CreateCommand();
            migrate.Transaction = transaction;
            migrate.CommandText = Migrations[version];
            migrate.ExecuteNonQuery();

            _logger?.Log(LogLevel.Information, $"Applied database migration {version + 1}.");
        }

        using SqliteCommand write = connection.CreateCommand();
        write.Transaction = transaction;

        // PRAGMA does not take parameters, and the value is an int from this array's length.
        write.CommandText = $"PRAGMA user_version = {Migrations.Length};";
        write.ExecuteNonQuery();

        transaction.Commit();
    }
}
