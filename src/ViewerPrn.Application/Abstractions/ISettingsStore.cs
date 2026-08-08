using ViewerPrn.Application.Settings;

namespace ViewerPrn.Application.Abstractions;

/// <summary>
/// Reads and writes <see cref="AppSettings"/>. Writes must be atomic: a torn settings file
/// must never be able to lose the user's configuration.
/// </summary>
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
