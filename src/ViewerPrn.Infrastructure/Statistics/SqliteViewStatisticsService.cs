using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Data.Sqlite;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Infrastructure.Database;

namespace ViewerPrn.Infrastructure.Statistics;

/// <summary>
/// Local view history (docs/REQUIREMENTS.md:34). Navigation only touches memory; the database is
/// written when the buffer fills, when a session ends, or when the application asks.
/// </summary>
// ponytail: flush at a fixed buffer size rather than on a timer. Ceiling is losing at most that
// many events to a crash, which is view counts - add a timer only if that ever matters.
public sealed class SqliteViewStatisticsService : IViewStatisticsService, IDisposable
{
    private const int FlushThreshold = 64;

    private readonly NexaDatabase _database;
    private readonly ConcurrentQueue<BufferedView> _buffer = new();
    private readonly ConcurrentDictionary<long, SessionState> _sessions = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private Task _backgroundFlush = Task.CompletedTask;

    public SqliteViewStatisticsService(NexaDatabase database) => _database = database;

    public Task<long> StartSessionAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return Task.Run(
            () =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;

                using SqliteConnection connection = _database.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO ViewSessions (SourcePath, StartedUtc) VALUES ($source, $started);
                    SELECT last_insert_rowid();
                    """;
                command.Parameters.AddWithValue("$source", sourcePath);
                command.Parameters.AddWithValue("$started", now.ToString("O", CultureInfo.InvariantCulture));

                long id = (long)command.ExecuteScalar()!;
                _sessions[id] = new SessionState(sourcePath, now);
                return id;
            },
            cancellationToken);
    }

    public void RecordImageView(long sessionId, string imagePath, int displayPosition)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !_sessions.TryGetValue(sessionId, out SessionState? session))
        {
            return;
        }

        session.Record(imagePath, displayPosition);
        _buffer.Enqueue(new BufferedView(session.SourcePath, imagePath, DateTimeOffset.UtcNow));

        if (_buffer.Count >= FlushThreshold)
        {
            // Started, not awaited: a navigation keystroke must never wait for a disk write.
            // The task is kept so that ending a session can wait for it — otherwise the last
            // batch is still in flight when the totals are read.
            _backgroundFlush = FlushAsync(CancellationToken.None);
        }
    }

    public async Task EndSessionAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryRemove(sessionId, out SessionState? session))
        {
            return;
        }

        await _backgroundFlush.ConfigureAwait(false);
        await FlushAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset ended = DateTimeOffset.UtcNow;
        TimeSpan duration = ended - session.Started;

        await Task.Run(
            () =>
            {
                using SqliteConnection connection = _database.Open();
                using SqliteTransaction transaction = connection.BeginTransaction();

                using (SqliteCommand close = connection.CreateCommand())
                {
                    close.Transaction = transaction;
                    close.CommandText =
                        """
                        UPDATE ViewSessions
                        SET EndedUtc = $ended, ImagesViewed = $viewed, LastPosition = $position
                        WHERE Id = $id;
                        """;
                    close.Parameters.AddWithValue("$ended", ended.ToString("O", CultureInfo.InvariantCulture));
                    close.Parameters.AddWithValue("$viewed", session.Views);
                    close.Parameters.AddWithValue("$position", (object?)session.LastPosition ?? DBNull.Value);
                    close.Parameters.AddWithValue("$id", sessionId);
                    close.ExecuteNonQuery();
                }

                using (SqliteCommand aggregate = connection.CreateCommand())
                {
                    aggregate.Transaction = transaction;
                    aggregate.CommandText =
                        """
                        INSERT INTO ViewAggregates
                            (SourcePath, FirstViewedUtc, LastViewedUtc, Sessions, TotalSeconds,
                             TotalImageViews, LastImagePath, LastPosition)
                        VALUES ($source, $now, $now, 1, $seconds, $views, $lastImage, $position)
                        ON CONFLICT (SourcePath) DO UPDATE SET
                            LastViewedUtc   = $now,
                            Sessions        = Sessions + 1,
                            TotalSeconds    = TotalSeconds + $seconds,
                            TotalImageViews = TotalImageViews + $views,
                            LastImagePath   = COALESCE($lastImage, LastImagePath),
                            LastPosition    = COALESCE($position, LastPosition);
                        """;
                    aggregate.Parameters.AddWithValue("$source", session.SourcePath);
                    aggregate.Parameters.AddWithValue("$now", ended.ToString("O", CultureInfo.InvariantCulture));
                    aggregate.Parameters.AddWithValue("$seconds", duration.TotalSeconds);
                    aggregate.Parameters.AddWithValue("$views", session.Views);
                    aggregate.Parameters.AddWithValue("$lastImage", (object?)session.LastImagePath ?? DBNull.Value);
                    aggregate.Parameters.AddWithValue("$position", (object?)session.LastPosition ?? DBNull.Value);
                    aggregate.ExecuteNonQuery();
                }

                transaction.Commit();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // One writer at a time: two overlapping flushes would each hold half the batch, and the
        // second could finish first.
        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<BufferedView> pending = [];
            while (_buffer.TryDequeue(out BufferedView view))
            {
                pending.Add(view);
            }

            if (pending.Count == 0)
            {
                return;
            }

            await Task.Run(
                () =>
                {
                using SqliteConnection connection = _database.Open();
                using SqliteTransaction transaction = connection.BeginTransaction();
                using SqliteCommand command = connection.CreateCommand();

                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO ImageViews (SourcePath, ImagePath, Views, LastViewedUtc)
                    VALUES ($source, $image, 1, $seen)
                    ON CONFLICT (SourcePath, ImagePath) DO UPDATE SET
                        Views = Views + 1,
                        LastViewedUtc = $seen;
                    """;

                SqliteParameter source = command.Parameters.Add("$source", SqliteType.Text);
                SqliteParameter image = command.Parameters.Add("$image", SqliteType.Text);
                SqliteParameter seen = command.Parameters.Add("$seen", SqliteType.Text);

                // One transaction for the whole batch: this is the reason for buffering.
                foreach (BufferedView view in pending)
                {
                    source.Value = view.SourcePath;
                    image.Value = view.ImagePath;
                    seen.Value = view.Seen.ToString("O", CultureInfo.InvariantCulture);
                    command.ExecuteNonQuery();
                }

                    transaction.Commit();
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    public Task<ViewStatistics?> GetAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return Task.Run<ViewStatistics?>(
            () =>
            {
                using SqliteConnection connection = _database.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT a.FirstViewedUtc, a.LastViewedUtc, a.Sessions, a.TotalSeconds,
                           a.TotalImageViews, a.LastImagePath, a.LastPosition,
                           (SELECT COUNT(*) FROM ImageViews i WHERE i.SourcePath = a.SourcePath)
                    FROM ViewAggregates a
                    WHERE a.SourcePath = $source;
                    """;
                command.Parameters.AddWithValue("$source", sourcePath);

                using SqliteDataReader reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }

                return new ViewStatistics
                {
                    SourcePath = sourcePath,
                    FirstViewed = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                    LastViewed = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                    Sessions = reader.GetInt32(2),
                    TotalViewTime = TimeSpan.FromSeconds(reader.GetDouble(3)),
                    TotalImageViews = reader.GetInt32(4),
                    LastImagePath = reader.IsDBNull(5) ? null : reader.GetString(5),
                    LastPosition = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    UniqueImages = reader.GetInt32(7),
                };
            },
            cancellationToken);
    }

    public void Dispose() => _flushGate.Dispose();

    private readonly record struct BufferedView(string SourcePath, string ImagePath, DateTimeOffset Seen);

    private sealed class SessionState(string sourcePath, DateTimeOffset started)
    {
        private int _views;

        public string SourcePath { get; } = sourcePath;

        public DateTimeOffset Started { get; } = started;

        public int Views => _views;

        public string? LastImagePath { get; private set; }

        public int? LastPosition { get; private set; }

        public void Record(string imagePath, int displayPosition)
        {
            Interlocked.Increment(ref _views);
            LastImagePath = imagePath;
            LastPosition = displayPosition;
        }
    }
}
