using ViewerPrn.Application.Abstractions;
using ViewerPrn.Application.Settings;
using ViewerPrn.Infrastructure.Storage;

namespace ViewerPrn.Infrastructure.Settings;

/// <summary>Stores settings as JSON, written atomically. See <see cref="AtomicJson"/>.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly ILoggingService? _logger;

    public JsonSettingsStore(string settingsFilePath, ILoggingService? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        _path = settingsFilePath;
        _logger = logger;
    }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        AtomicJson.ReadAsync(_path, AppSettings.Default, _logger, cancellationToken);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return AtomicJson.WriteAsync(_path, settings, cancellationToken);
    }
}
