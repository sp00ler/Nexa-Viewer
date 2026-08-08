using ViewerPrn.Application.Session;

namespace ViewerPrn.Application.Abstractions;

/// <summary>
/// Persists the open tabs across restarts. Writes must be atomic: a torn session file must
/// never cost the user their open tabs.
/// </summary>
public interface ISessionService
{
    Task<SessionState> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(SessionState state, CancellationToken cancellationToken = default);
}
