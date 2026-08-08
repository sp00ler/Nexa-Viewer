using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using ViewerPrn.Application.Abstractions;

namespace ViewerPrn.Infrastructure.Storage;

/// <summary>
/// Read/write JSON state so that a crash mid-write leaves either the old file or the new one,
/// never a truncated one: serialise to a temporary file, then swap it in with
/// <see cref="File.Replace(string, string, string?)"/>, keeping the previous version as a backup.
/// </summary>
internal static class AtomicJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,

        // Enums are stored by name. A numeric value would silently change meaning if the
        // enum were ever reordered, and the file is meant to be readable by a human.
        Converters = { new JsonStringEnumConverter() },
    };

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Unreadable state must never stop the application from starting; the failure is logged and the fallback is used.")]
    public static async Task<T> ReadAsync<T>(
        string path,
        T fallback,
        ILoggingService? logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback;
            }

            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false)
                ?? fallback;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The bad file is left in place: it is the only evidence of what went wrong.
            logger?.Log(LogLevel.Warning, $"Could not read '{path}'. Falling back to defaults.", exception);
            return fallback;
        }
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = path + ".tmp";

        await using (FileStream stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(path))
        {
            File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporary, path);
        }
    }
}
