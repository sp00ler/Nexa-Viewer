using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.Archives;
using ViewerPrn.Domain.FileSystem;
using ViewerPrn.Domain.Images;
using ViewerPrn.Infrastructure.FileSystem;
using Windows.Storage.Streams;
using Windows.System;

namespace ViewerPrn.App;

/// <summary>Everything the Viewer needs to open: the gallery, and where in it to start.</summary>
public sealed record ViewerRequest(IReadOnlyList<string> Images, int StartIndex);

/// <summary>One row of the Explorer list, with its display text already formatted.</summary>
public sealed class EntryRow : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;

    public EntryRow(FileSystemEntry entry)
    {
        Entry = entry;
        IsImage = entry.Kind == EntryKind.File && ImageFormats.IsImage(entry.Name);
        IsArchive = entry.Kind == EntryKind.File && ArchiveFormats.IsArchive(entry.Name);
        Glyph = entry.Kind == EntryKind.Folder ? "" : "";
        SizeText = entry.Kind == EntryKind.Folder ? string.Empty : FormatSize(entry.Size);
        ModifiedText = entry.Modified.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FileSystemEntry Entry { get; }

    public string Name => Entry.Name;

    public bool IsImage { get; }

    /// <summary>Archives are browsable containers, not files to hand to the Viewer.</summary>
    public bool IsArchive { get; }

    /// <summary>Set once the thumbnail arrives; the placeholder glyph hides itself when it does.</summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GlyphVisibility)));
        }
    }

    public Visibility GlyphVisibility => _thumbnail is null ? Visibility.Visible : Visibility.Collapsed;

    public string Glyph { get; }

    public string SizeText { get; }

    public string ModifiedText { get; }

    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{value:0.#} {Strings.Get("Unit_" + units[unit])}");
    }
}

/// <summary>
/// The Explorer list for one tab: enumeration, sorting, selection, rename and delete.
/// Archives are listed as ordinary files; browsing into them is Phase 6.
/// </summary>
public sealed partial class FolderView : UserControl, IDisposable
{
    /// <summary>Requested thumbnail edge in pixels; matches the 40px row image with room to spare.</summary>
    private const int ThumbnailEdge = 48;

    private readonly IFileSystemService _fileSystem;
    private readonly IArchiveService _archives;
    private readonly IThumbnailProvider _thumbnails;
    private readonly ILoggingService _logger;
    private IReadOnlyList<FileSystemEntry> _entries = [];
    private CancellationTokenSource? _loadCancellation;
    private IReadOnlyList<string>? _pendingSelection;
    private bool _loaded;

    public FolderView(
        IFileSystemService fileSystem,
        IArchiveService archives,
        IThumbnailProvider thumbnails,
        ILoggingService logger,
        SortCriterion criterion = SortCriterion.Name,
        SortDirection direction = SortDirection.Ascending,
        IReadOnlyList<string>? initialSelection = null)
    {
        _fileSystem = fileSystem;
        _archives = archives;
        _thumbnails = thumbnails;
        _logger = logger;
        Criterion = criterion;
        Direction = direction;
        _pendingSelection = initialSelection;

        InitializeComponent();

        RenameMenuItem.Text = Strings.Get("Cmd_Rename");
        DeleteMenuItem.Text = Strings.Get("Cmd_Delete");
    }

    /// <summary>Raised when the user navigates into a folder or up out of one.</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>Raised when the selection changes, so the shell can update the status bar.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when an image is opened, with every image in the folder and where to start.</summary>
    public event EventHandler<ViewerRequest>? ImageOpenRequested;

    public string CurrentPath { get; private set; } = string.Empty;

    public SortCriterion Criterion { get; private set; } = SortCriterion.Name;

    public SortDirection Direction { get; private set; } = SortDirection.Ascending;

    public int ItemCount => _entries.Count;

    public int SelectedCount => Entries.SelectedItems.Count;

    public IReadOnlyList<string> SelectedNames =>
        [.. Entries.SelectedItems.OfType<EntryRow>().Select(row => row.Name)];

    /// <summary>
    /// Loads the folder the first time the tab is actually shown. Restored tabs stay empty
    /// until then — docs/REQUIREMENTS.md:10 forbids loading all tabs eagerly.
    /// </summary>
    public async Task EnsureLoadedAsync(string path)
    {
        if (!_loaded)
        {
            await LoadAsync(path);
        }
    }

    public async Task LoadAsync(string path)
    {
        _loaded = true;
        // Switching folders while a listing is still running abandons the old one rather than
        // letting two results race into the same list.
        await CancelPendingLoadAsync();
        _loadCancellation = new CancellationTokenSource();
        CancellationToken token = _loadCancellation.Token;

        CurrentPath = path;
        Busy.Visibility = Visibility.Visible;
        ShowMessage(null);

        try
        {
            long startedAt = Stopwatch.GetTimestamp();

            // Archives are browsed as if they were folders (docs/REQUIREMENTS.md:4); everything
            // downstream sees the same FileSystemEntry list either way.
            _entries = ArchiveLocation.TryParse(path, out ArchiveLocation? location)
                ? await _archives.ListAsync(location, token)
                : await _fileSystem.EnumerateAsync(path, token);
            await ApplyCurrentSortAsync();
            _logger.Log(
                LogLevel.Information,
                $"Listed {_entries.Count} entries in '{path}' in {Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F0} ms.");

            if (_entries.Count == 0)
            {
                ShowMessage(Strings.Get("Folder_Empty"));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (ArchiveLocation.TryParse(path, out _))
        {
            // A damaged, truncated or password-protected archive can fail in any number of ways
            // inside the decoder; none of them should take the tab down with it.
            _logger.Log(LogLevel.Warning, $"Could not read the archive at '{path}'.", exception);
            ShowFailure(Strings.Get("Archive_Unreadable"));
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Log(LogLevel.Warning, $"Access denied listing '{path}'.", exception);
            ShowFailure(Strings.Get("Error_AccessDenied"));
        }
        catch (IOException exception)
        {
            _logger.Log(LogLevel.Warning, $"Could not list '{path}'.", exception);
            ShowFailure(Strings.Get("Error_FolderUnavailable"));
        }
        finally
        {
            Busy.Visibility = Visibility.Collapsed;
        }
    }

    public async Task ApplySortAsync(SortCriterion criterion, SortDirection direction)
    {
        Criterion = criterion;
        Direction = direction;
        await ApplyCurrentSortAsync();
    }

    /// <summary>
    /// Sorting and row construction run on a worker thread. Measured at 100 000 entries the
    /// natural-order sort alone takes ~370 ms, which is far too long to spend on the UI thread.
    /// </summary>
    private async Task ApplyCurrentSortAsync()
    {
        IReadOnlyList<FileSystemEntry> entries = _entries;
        SortCriterion criterion = Criterion;
        SortDirection direction = Direction;

        List<EntryRow> rows = await Task.Run(() =>
            EntrySorter.Sort(entries, criterion, direction, NaturalStringComparer.Instance)
                .Select(entry => new EntryRow(entry))
                .ToList());

        Entries.ItemsSource = rows;
        RestorePendingSelection(rows);
    }

    private void RestorePendingSelection(List<EntryRow> rows)
    {
        if (_pendingSelection is not { Count: > 0 })
        {
            return;
        }

        HashSet<string> wanted = new(_pendingSelection, StringComparer.OrdinalIgnoreCase);
        _pendingSelection = null;

        foreach (EntryRow row in rows.Where(row => wanted.Contains(row.Name)))
        {
            Entries.SelectedItems.Add(row);
        }
    }

    private void ShowMessage(string? message)
    {
        MessageText.Text = message ?? string.Empty;
        MessageText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowFailure(string message)
    {
        _entries = [];
        Entries.ItemsSource = null;
        ShowMessage(message);
    }

    private async Task CancelPendingLoadAsync()
    {
        if (_loadCancellation is null)
        {
            return;
        }

        await _loadCancellation.CancelAsync();
        _loadCancellation.Dispose();
        _loadCancellation = null;
    }

    // ---- Thumbnails ----

    /// <summary>
    /// ContainerContentChanging is the virtualisation hook: it fires only for rows the ListView
    /// actually realises, so a folder of 100 000 images requests 20-odd thumbnails, not 100 000.
    /// </summary>
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Phase != 0)
        {
            return;
        }

        if (args.Item is EntryRow { IsImage: true, Thumbnail: null })
        {
            // Deferred to a later phase so the row shows its text immediately and the picture
            // catches up.
            args.RegisterUpdateCallback(LoadThumbnailAsync);
        }

        args.Handled = true;
    }

    private async void LoadThumbnailAsync(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not EntryRow { IsImage: true, Thumbnail: null } row)
        {
            return;
        }

        CancellationToken token = _loadCancellation?.Token ?? CancellationToken.None;

        try
        {
            // Entries inside an archive are extracted first; ordinary paths pass straight through.
            string real = await _archives.MaterialiseAsync(row.Entry.FullPath, token);
            byte[]? bytes = await _thumbnails.GetThumbnailAsync(
                real, row.Entry.Modified, ThumbnailEdge, token);

            if (bytes is null || token.IsCancellationRequested)
            {
                return;
            }

            row.Thumbnail = await ToImageSourceAsync(bytes);
        }
        catch (OperationCanceledException)
        {
            // The folder changed while the thumbnail was in flight.
        }
    }

    private static async Task<BitmapImage> ToImageSourceAsync(byte[] bytes)
    {
        InMemoryRandomAccessStream stream = new();
        using (DataWriter writer = new(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        BitmapImage image = new();
        await image.SetSourceAsync(stream);
        return image;
    }

    // ---- Navigation ----

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => OpenSelected();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectionChanged?.Invoke(this, EventArgs.Empty);

    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                OpenSelected();
                e.Handled = true;
                break;

            case VirtualKey.Back:
                NavigateUp();
                e.Handled = true;
                break;

            case VirtualKey.F2:
                e.Handled = true;
                await RenameSelectedAsync();
                break;

            case VirtualKey.Delete:
                e.Handled = true;
                await DeleteSelectedAsync();
                break;

            default:
                break;
        }
    }

    private void OpenSelected()
    {
        switch (Entries.SelectedItem)
        {
            case EntryRow { Entry.Kind: EntryKind.Folder } folder:
                NavigationRequested?.Invoke(this, folder.Entry.FullPath);
                break;

            // An archive opens like a folder rather than launching anything.
            case EntryRow { IsArchive: true } archive:
                NavigationRequested?.Invoke(this, archive.Entry.FullPath);
                break;

            case EntryRow { IsImage: true } image:
                RequestViewer(image);
                break;

            default:
                // Other files have no action yet; archives become browsable in Phase 6.
                break;
        }
    }

    /// <summary>
    /// Hands the Viewer every image in the folder in the order currently on screen, so its
    /// next/previous match what the user sees.
    /// </summary>
    private void RequestViewer(EntryRow selected)
    {
        List<string> images = [.. Entries.ItemsSource is IEnumerable<EntryRow> rows
            ? rows.Where(row => row.IsImage).Select(row => row.Entry.FullPath)
            : []];

        int index = images.IndexOf(selected.Entry.FullPath);
        if (index >= 0)
        {
            ImageOpenRequested?.Invoke(this, new ViewerRequest(images, index));
        }
    }

    /// <summary>Restores the list selection to one path and scrolls it into view.</summary>
    public void SelectPath(string fullPath)
    {
        if (Entries.ItemsSource is not IEnumerable<EntryRow> rows)
        {
            return;
        }

        EntryRow? row = rows.FirstOrDefault(r => string.Equals(r.Entry.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return;
        }

        Entries.SelectedItem = row;
        Entries.ScrollIntoView(row);
        Entries.Focus(FocusState.Programmatic);
    }

    private void NavigateUp()
    {
        string? parent = Path.GetDirectoryName(CurrentPath);
        if (!string.IsNullOrEmpty(parent))
        {
            NavigationRequested?.Invoke(this, parent);
        }
    }

    // ---- Rename and delete ----

    private async void OnRenameClick(object sender, RoutedEventArgs e) => await RenameSelectedAsync();

    private async void OnDeleteClick(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    public async Task RenameSelectedAsync()
    {
        if (Entries.SelectedItem is not EntryRow row)
        {
            return;
        }

        if (await RefuseInsideArchiveAsync())
        {
            return;
        }

        TextBox input = new() { Text = row.Name, SelectionStart = 0, SelectionLength = row.Name.Length };
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("Rename_Title"),
            Content = input,
            PrimaryButtonText = Strings.Get("Dlg_Apply"),
            CloseButtonText = Strings.Get("Dlg_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string newName = input.Text.Trim();
        if (newName.Length == 0 || newName == row.Name)
        {
            return;
        }

        try
        {
            await _fileSystem.RenameAsync(row.Entry, newName);
            await LoadAsync(CurrentPath);
        }
        catch (NameConflictException)
        {
            await ShowErrorAsync(Strings.Format("Rename_Conflict", newName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.Log(LogLevel.Error, $"Could not rename '{row.Entry.FullPath}'.", exception);
            await ShowErrorAsync(Strings.Get("Rename_Failed"));
        }
    }

    public async Task DeleteSelectedAsync()
    {
        List<FileSystemEntry> selected = [.. Entries.SelectedItems.OfType<EntryRow>().Select(row => row.Entry)];
        if (selected.Count == 0)
        {
            return;
        }

        if (await RefuseInsideArchiveAsync())
        {
            return;
        }

        ContentDialog confirm = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("Delete_Title"),
            Content = selected.Count == 1
                ? Strings.Format("Delete_ConfirmOne", selected[0].Name)
                : Strings.Format("Delete_ConfirmMany", selected.Count),
            PrimaryButtonText = Strings.Get("Dlg_Delete"),
            CloseButtonText = Strings.Get("Dlg_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _fileSystem.DeleteAsync(selected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Error, "Could not delete every selected entry.", exception);
            await ShowErrorAsync(Strings.Get("Delete_Failed"));
        }
        finally
        {
            // Reload either way: a partial failure still changed the folder.
            await LoadAsync(CurrentPath);
        }
    }

    /// <summary>Called when the tab closes, so a listing still in flight stops immediately.</summary>
    public void Dispose()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    /// <summary>
    /// Archives are read-only containers (docs/ARCHITECTURE.md:14): nothing inside one is
    /// renamed or deleted, and saying so beats failing halfway through.
    /// </summary>
    private async Task<bool> RefuseInsideArchiveAsync()
    {
        if (!ArchiveLocation.TryParse(CurrentPath, out _))
        {
            return false;
        }

        await ShowErrorAsync(Strings.Get("Archive_ReadOnly"));
        return true;
    }

    private async Task ShowErrorAsync(string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("Error_Title"),
            Content = message,
            CloseButtonText = Strings.Get("Dlg_OK"),
        };

        await dialog.ShowAsync();
    }
}
