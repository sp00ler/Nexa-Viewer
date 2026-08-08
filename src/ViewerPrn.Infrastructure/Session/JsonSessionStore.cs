using ViewerPrn.Application.Abstractions;
using ViewerPrn.Application.Session;
using ViewerPrn.Infrastructure.Storage;

namespace ViewerPrn.Infrastructure.Session;

/// <summary>
/// Session state as JSON, written atomically and kept in its own file rather than in the
/// database: restoring tabs has to work even when the database is locked or corrupt
/// (DECISION-0008).
/// </summary>
public sealed class JsonSessionStore : ISessionService
{
    private readonly string _path;
    private readonly ILoggingService? _logger;

    public JsonSessionStore(string sessionFilePath, ILoggingService? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionFilePath);
        _path = sessionFilePath;
        _logger = logger;
    }

    public async Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
    {
        SessionState state = await AtomicJson
            .ReadAsync(_path, SessionState.Empty, _logger, cancellationToken)
            .ConfigureAwait(false);

        return state.Sanitised();
    }

    public Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return AtomicJson.WriteAsync(_path, state.Sanitised(), cancellationToken);
    }
}
