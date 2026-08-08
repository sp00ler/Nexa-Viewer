using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.Archives;
using ViewerPrn.Domain.FileSystem;
using ViewerPrn.Infrastructure.Archives;
using ViewerPrn.Infrastructure.Database;
using ViewerPrn.Infrastructure.FileSystem;
using ViewerPrn.Infrastructure.Images;
using ViewerPrn.Infrastructure.Statistics;

// The measurements behind docs/PERFORMANCE.md. Run it rather than trusting the numbers:
//     dotnet run --project tools/NexaViewer.Bench -c Release -- <scratch-directory> [sample-images]
//
// Everything the UI cannot be asked headlessly - startup, idle memory, 25 tabs - is measured by
// running the application itself and reading its own log.

string scratch = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "NexaViewer.Bench");
string? sampleImages = args.Length > 1 ? args[1] : null;

Directory.CreateDirectory(scratch);
Console.WriteLine($"scratch: {scratch}");
Console.WriteLine($"volume:  {Path.GetPathRoot(Path.GetFullPath(scratch))}");
Console.WriteLine();

await MeasureEnumerationAsync();
await MeasureArchiveAsync();
await MeasureImagesAsync();
await MeasureDatabaseAsync();

Console.WriteLine();
Console.WriteLine("done");

static double Milliseconds(long from) => Stopwatch.GetElapsedTime(from).TotalMilliseconds;

static void Row(string what, double milliseconds, string? extra = null) =>
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  {what,-44} {milliseconds,9:F1} ms{(extra is null ? string.Empty : "   " + extra)}"));

async Task MeasureEnumerationAsync()
{
    Console.WriteLine("Enumeration and sorting");
    WindowsFileSystemService files = new();

    foreach (int count in (int[])[1_000, 10_000, 100_000])
    {
        string folder = Path.Combine(scratch, $"folder{count}");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            for (int i = 0; i < count; i++)
            {
                File.WriteAllText(Path.Combine(folder, $"img{i}.jpg"), "x");
            }
        }

        await files.EnumerateAsync(folder);

        long start = Stopwatch.GetTimestamp();
        IReadOnlyList<FileSystemEntry> entries = await files.EnumerateAsync(folder);
        Row($"enumerate {count:N0} entries (warm)", Milliseconds(start));

        start = Stopwatch.GetTimestamp();
        _ = EntrySorter.Sort(entries, SortCriterion.Name, SortDirection.Ascending, NaturalStringComparer.Instance);
        Row($"natural sort {count:N0}", Milliseconds(start));

        start = Stopwatch.GetTimestamp();
        _ = EntrySorter.Sort(entries, SortCriterion.Random, SortDirection.Ascending, NaturalStringComparer.Instance, 1);
        Row($"random shuffle {count:N0}", Milliseconds(start));
    }

    Console.WriteLine();
}

async Task MeasureArchiveAsync()
{
    Console.WriteLine("Archives");

    string zip = Path.Combine(scratch, "bench.zip");
    if (!File.Exists(zip))
    {
        using FileStream file = File.Create(zip);
        using ZipArchive archive = new(file, ZipArchiveMode.Create);
        for (int i = 0; i < 2_000; i++)
        {
            using StreamWriter writer = new(archive.CreateEntry($"day{i % 20}/img{i}.jpg").Open());
            writer.Write(new string('x', 512));
        }
    }

    ArchiveService archives = new(Path.Combine(scratch, "cache"));

    // Warm-up: the first call pays for loading and initialising the decoder, which is a startup
    // cost, not the cost of listing an archive.
    await archives.ListAsync(new ArchiveLocation(zip, string.Empty));

    long start = Stopwatch.GetTimestamp();
    IReadOnlyList<FileSystemEntry> root = await archives.ListAsync(new ArchiveLocation(zip, string.Empty));
    Row("list 2,000-entry ZIP root", Milliseconds(start), $"{root.Count} shown");

    start = Stopwatch.GetTimestamp();
    _ = await archives.ListAsync(new ArchiveLocation(zip, "day3"));
    Row("list one folder inside the ZIP", Milliseconds(start));

    start = Stopwatch.GetTimestamp();
    string extracted = await archives.MaterialiseAsync(Path.Combine(zip, "day3", "img3.jpg"));
    Row("extract one entry (cold)", Milliseconds(start));

    start = Stopwatch.GetTimestamp();
    _ = await archives.MaterialiseAsync(Path.Combine(zip, "day3", "img3.jpg"));
    Row("extract one entry (cached)", Milliseconds(start));

    File.Delete(extracted);
    archives.ClearCache();
    Console.WriteLine();
}

async Task MeasureImagesAsync()
{
    if (sampleImages is null || !Directory.Exists(sampleImages))
    {
        Console.WriteLine("Images: skipped, no sample folder given");
        Console.WriteLine();
        return;
    }

    Console.WriteLine("Images");
    string[] samples = Directory.GetFiles(sampleImages);
    WicMetadataReader metadata = new();
    using ShellThumbnailProvider thumbnails = new();

    // Warm-up: the first StorageFile call initialises the WinRT storage stack. Timing it with
    // the batch made metadata look sixteen times more expensive than it is.
    _ = await metadata.ReadAsync(samples[0]);

    long start = Stopwatch.GetTimestamp();
    foreach (string sample in samples)
    {
        _ = await metadata.ReadAsync(sample);
    }

    Row($"metadata, {samples.Length} files", Milliseconds(start), $"{Milliseconds(start) / samples.Length:F1} ms each");

    start = Stopwatch.GetTimestamp();
    byte[]?[] first = await Task.WhenAll(samples.Select(s => thumbnails.GetThumbnailAsync(s, File.GetLastWriteTimeUtc(s), 48)));
    Row($"thumbnails, {samples.Length} files (first pass)", Milliseconds(start));

    start = Stopwatch.GetTimestamp();
    await Task.WhenAll(samples.Select(s => thumbnails.GetThumbnailAsync(s, File.GetLastWriteTimeUtc(s), 48)));
    Row($"thumbnails, {samples.Length} files (cached)", Milliseconds(start),
        $"{thumbnails.CachedBytes / 1024} KB for {first.Count(t => t is not null)}");

    Console.WriteLine();
}

async Task MeasureDatabaseAsync()
{
    Console.WriteLine("Database");

    string databaseFile = Path.Combine(scratch, "bench.db");
    if (File.Exists(databaseFile))
    {
        File.Delete(databaseFile);
    }

    NexaDatabase database = new(databaseFile);

    long start = Stopwatch.GetTimestamp();
    database.Migrate();
    Row("migrate a new database (cold, loads native SQLite)", Milliseconds(start));

    using SqliteViewStatisticsService statistics = new(database);

    start = Stopwatch.GetTimestamp();
    long session = await statistics.StartSessionAsync(@"E:\bench");
    for (int i = 0; i < 100_000; i++)
    {
        statistics.RecordImageView(session, $@"E:\bench\img{i}.jpg", i + 1);
    }

    await statistics.EndSessionAsync(session);
    Row("record 100,000 image views", Milliseconds(start));

    start = Stopwatch.GetTimestamp();
    ViewStatistics? stored = await statistics.GetAsync(@"E:\bench");
    Row("query aggregates", Milliseconds(start), $"{stored?.UniqueImages:N0} unique");

    long bytes = new FileInfo(databaseFile).Length
        + (File.Exists(databaseFile + "-wal") ? new FileInfo(databaseFile + "-wal").Length : 0);
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  {"database size after 100,000 views",-44} {bytes / 1024.0 / 1024.0,9:F1} MB"));
}
