namespace ViewerPrn.Application.Abstractions;

/// <summary>What has been recorded about one source or gallery (docs/REQUIREMENTS.md:34).</summary>
public sealed record ViewStatistics
{
    public required string SourcePath { get; init; }

    public required DateTimeOffset FirstViewed { get; init; }

    public required DateTimeOffset LastViewed { get; init; }

    public required int Sessions { get; init; }

    public required TimeSpan TotalViewTime { get; init; }

    public required int TotalImageViews { get; init; }

    public required int UniqueImages { get; init; }

    public string? LastImagePath { get; init; }

    public int? LastPosition { get; init; }
}

/// <summary>
/// Local view history. Events are buffered rather than committed on every navigation
/// (docs/REQUIREMENTS.md:34); nothing is ever uploaded.
/// </summary>
public interface IViewStatisticsService
{
    /// <summary>Begins a session for one source. Returns its id.</summary>
    Task<long> StartSessionAsync(string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>Records one image view. Cheap: this only touches memory.</summary>
    void RecordImageView(long sessionId, string imagePath, int displayPosition);

    /// <summary>Ends the session and writes everything buffered for it.</summary>
    Task EndSessionAsync(long sessionId, CancellationToken cancellationToken = default);

    /// <summary>Writes out whatever is buffered without ending the session.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    Task<ViewStatistics?> GetAsync(string sourcePath, CancellationToken cancellationToken = default);
}
