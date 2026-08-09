using ViewerPrn.Application.Abstractions;
using ViewerPrn.Application.Session;
using ViewerPrn.Infrastructure.Storage;

namespace ViewerPrn.Infrastructure.Session;

/// <summary>
/// The saved states, in their own file beside the automatic session and written the same atomic
/// way: a torn file must never cost the user a saved state (DECISION-0008).
/// <para>
/// ponytail: no interface — there is one implementation and one caller. Introduce one when a
/// second store exists, per DECISION-0012.
/// </para>
/// </summary>
public sealed class JsonSessionLibraryStore
{
    private readonly string _path;
    private readonly ILoggingService? _logger;

    public JsonSessionLibraryStore(string libraryFilePath, ILoggingService? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryFilePath);
        _path = libraryFilePath;
        _logger = logger;
    }

    public async Task<SessionLibrary> LoadAsync(CancellationToken cancellationToken = default)
    {
        SessionLibrary library = await AtomicJson
            .ReadAsync(_path, SessionLibrary.Empty, _logger, cancellationToken)
            .ConfigureAwait(false);

        return library.Sanitised();
    }

    public Task SaveAsync(SessionLibrary library, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        return AtomicJson.WriteAsync(_path, library.Sanitised(), cancellationToken);
    }
}
