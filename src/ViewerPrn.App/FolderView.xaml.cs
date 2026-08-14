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
using ViewerPrn.Domain.Navigation;
using ViewerPrn.Infrastructure.FileSystem;
using ViewerPrn.Infrastructure.Images;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

namespace ViewerPrn.App;

public enum FileOperationRequestKind
{
    Paste = 0,
    CopyTo = 1,
    MoveTo = 2,
}

/// <summary>An operation the list cannot complete on its own because it needs a folder picker.</summary>
public sealed record FileOperationRequest(
    FileOperationRequestKind Kind,
    IReadOnlyList<FileSystemEntry> Entries,
    string CurrentPath);

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
    /// <summary>Requested thumbnail edge, matched to the size the current mode draws.</summary>
    private int ThumbnailEdge => ViewMode == ExplorerViewMode.Thumbnails ? 160 : 20;

    /// <summary>Whichever of the two controls is currently showing.</summary>
    private ListViewBase Items =>
        ViewMode == ExplorerViewMode.Thumbnails ? Tiles : Entries;

    private readonly IFileSystemService _fileSystem;
    private readonly IArchiveService _archives;
    private readonly IThumbnailProvider _thumbnails;
    private readonly FileTypeIcons _typeIcons;
    private readonly ILoggingService _logger;
    private IReadOnlyList<FileSystemEntry> _entries = [];
    private CancellationTokenSource? _loadCancellation;
    private IReadOnlyList<string>? _pendingSelection;
    private readonly NavigationHistory _history = new();
    private int _randomSeed = Environment.TickCount;
    private bool _loaded;

    public FolderView(
        IFileSystemService fileSystem,
        IArchiveService archives,
        IThumbnailProvider thumbnails,
        FileTypeIcons typeIcons,
        ILoggingService logger,
        SortCriterion criterion = SortCriterion.Name,
        SortDirection direction = SortDirection.Ascending,
        ExplorerViewMode viewMode = ExplorerViewMode.Details,
        IReadOnlyList<string>? initialSelection = null)
    {
        _fileSystem = fileSystem;
        _archives = archives;
        _thumbnails = thumbnails;
        _typeIcons = typeIcons;
        _logger = logger;
        Criterion = criterion;
        Direction = direction;
        ViewMode = viewMode;
        _pendingSelection = initialSelection;

        InitializeComponent();
        ApplyViewMode();

        CopyMenuItem.Text = Strings.Get("Cmd_Copy");
        CutMenuItem.Text = Strings.Get("Cmd_Cut");
        PasteMenuItem.Text = Strings.Get("Cmd_Paste");
        CopyToMenuItem.Text = Strings.Get("Cmd_CopyTo");
        MoveToMenuItem.Text = Strings.Get("Cmd_MoveTo");
        RenameMenuItem.Text = Strings.Get("Cmd_Rename");
        DeleteMenuItem.Text = Strings.Get("Cmd_Delete");
        UpdateColumnHeader();
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

    public ExplorerViewMode ViewMode { get; private set; } = ExplorerViewMode.Details;

    /// <summary>
    /// Switches between the list and the grid. Both are bound to the same rows, so the change is
    /// a template swap and a visibility flip — the folder is not read again.
    /// </summary>
    public void SetViewMode(ExplorerViewMode mode)
    {
        if (mode == ViewMode)
        {
            return;
        }

        IReadOnlyList<string> selected = SelectedNames;
        ViewMode = mode;
        ApplyViewMode();
        RestoreSelection(selected);
    }

    private void ApplyViewMode()
    {
        bool grid = ViewMode == ExplorerViewMode.Thumbnails;

        Entries.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        Tiles.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;

        Entries.ItemTemplate = (DataTemplate)Resources[
            ViewMode == ExplorerViewMode.List ? "ListTemplate" : "DetailsTemplate"];

        // The header belongs to the columns, and only Details has them.
        ColumnHeader.Visibility = ViewMode == ExplorerViewMode.Details
            ? Visibility.Visible
            : Visibility.Collapsed;

        Items.Focus(FocusState.Programmatic);
    }

    /// <summary>Raised when a column header changed the sort, so the menu can follow.</summary>
    public event EventHandler<(SortCriterion Criterion, SortDirection Direction)>? SortChanged;

    /// <summary>
    /// Tapping a header sorts by that column; tapping the column already sorted reverses it —
    /// the Explorer behaviour. The arrow marks which column is in force.
    /// </summary>
    private async void OnHeaderTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse(tag, out SortCriterion criterion))
        {
            return;
        }

        SortDirection direction = criterion == Criterion && Direction == SortDirection.Ascending
            ? SortDirection.Descending
            : SortDirection.Ascending;

        await ApplySortAsync(criterion, direction);
        SortChanged?.Invoke(this, (criterion, direction));
    }

    private void UpdateColumnHeader()
    {
        string arrow = Direction == SortDirection.Ascending ? " ▲" : " ▼";

        NameHeader.Text = Strings.Get("Column_Name") + (Criterion == SortCriterion.Name ? arrow : string.Empty);
        SizeHeader.Text = Strings.Get("Column_Size") + (Criterion == SortCriterion.Size ? arrow : string.Empty);
        ModifiedHeader.Text = Strings.Get("Column_Modified") + (Criterion == SortCriterion.Modified ? arrow : string.Empty);
    }

    public int ItemCount => _entries.Count;

    public int FolderCount => _entries.Count(entry => entry.Kind == EntryKind.Folder);

    public int FileCount => _entries.Count(entry => entry.Kind == EntryKind.File);

    public int SelectedCount => Items.SelectedItems.Count;

    public IReadOnlyList<string> SelectedNames =>
        [.. Items.SelectedItems.OfType<EntryRow>().Select(row => row.Name)];

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

    /// <summary>
    /// Which tree nodes this tab has expanded. Held here rather than in the tree itself, so each
    /// tab keeps its own view of the same control (DECISION-0032).
    /// </summary>
    public IReadOnlyList<string> ExpandedTreePaths { get; set; } = [];

    public bool CanGoBack => _history.CanGoBack;

    public bool CanGoForward => _history.CanGoForward;

    public bool CanGoUp => Path.GetDirectoryName(CurrentPath) is { Length: > 0 };

    public async Task<string?> GoBackAsync()
    {
        string? path = _history.GoBack();
        if (path is not null)
        {
            await LoadAsync(path, recordHistory: false);
        }

        return path;
    }

    public async Task<string?> GoForwardAsync()
    {
        string? path = _history.GoForward();
        if (path is not null)
        {
            await LoadAsync(path, recordHistory: false);
        }

        return path;
    }

    public async Task<string?> GoUpAsync()
    {
        string? parent = Path.GetDirectoryName(CurrentPath);
        if (string.IsNullOrEmpty(parent))
        {
            return null;
        }

        await LoadAsync(parent);
        return parent;
    }

    public async Task LoadAsync(string path) => await LoadAsync(path, recordHistory: true);

    private async Task LoadAsync(string path, bool recordHistory)
    {
        // Stepping up out of a folder selects the folder just left, so leaving a gallery lands
        // the cursor where it came from rather than at the top of the list.
        if (_pendingSelection is null
            && CurrentPath.Length > 0
            && string.Equals(Path.GetDirectoryName(CurrentPath), path, StringComparison.OrdinalIgnoreCase))
        {
            _pendingSelection = [Path.GetFileName(CurrentPath)];
        }

        _loaded = true;

        // Back and forward move within the history rather than adding to it.
        if (recordHistory)
        {
            _history.Visit(path);
        }

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
        // Choosing Random again reshuffles; any other criterion restores the normal order, which
        // is what makes Random Explorer reversible (docs/REQUIREMENTS.md:7).
        if (criterion == SortCriterion.Random)
        {
            _randomSeed = Environment.TickCount;
        }

        Criterion = criterion;
        Direction = direction;
        UpdateColumnHeader();
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

        int seed = _randomSeed;
        List<EntryRow> rows = await Task.Run(() =>
            EntrySorter.Sort(entries, criterion, direction, NaturalStringComparer.Instance, seed)
                .Select(entry => new EntryRow(entry))
                .ToList());

        Entries.ItemsSource = rows;
        Tiles.ItemsSource = rows;
        RestorePendingSelection(rows);
    }

    private void RestorePendingSelection(List<EntryRow> rows)
    {
        // Cleared whether or not there was anything in it: a tab opened with an empty selection
        // used to leave an empty list here for ever, and that blocked the "select the folder just
        // left" rule in LoadAsync, which only arms itself when nothing else is pending.
        IReadOnlyList<string>? pending = _pendingSelection;
        _pendingSelection = null;

        if (pending is { Count: > 0 })
        {
            RestoreSelection(pending);
        }
    }

    /// <summary>Re-selects by name, so a view switch keeps the selection.</summary>
    private void RestoreSelection(IReadOnlyList<string> names)
    {
        if (names.Count == 0 || Items.ItemsSource is not IEnumerable<EntryRow> rows)
        {
            return;
        }

        HashSet<string> wanted = new(names, StringComparer.OrdinalIgnoreCase);
        Items.SelectedItems.Clear();

        EntryRow? first = null;
        foreach (EntryRow row in rows.Where(row => wanted.Contains(row.Name)))
        {
            Items.SelectedItems.Add(row);
            first ??= row;
        }

        if (first is not null)
        {
            BringIntoMiddle(first);
        }
    }

    /// <summary>
    /// Scrolls an entry to the middle of the list rather than to whichever edge is closest, and
    /// leaves the keyboard on it, so it reads as the cursor's position.
    /// </summary>
    private void BringIntoMiddle(EntryRow row)
    {
        Items.ScrollIntoView(row, ScrollIntoViewAlignment.Leading);
        Items.Focus(FocusState.Programmatic);

        // Queued: the container the offset is measured from does not exist until layout runs.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (FindScrollViewer(Items) is not { } scroll)
            {
                return;
            }

            double itemHeight = Items.ContainerFromItem(row) is FrameworkElement container
                ? container.ActualHeight
                : 0;

            double centred = scroll.VerticalOffset - ((scroll.ViewportHeight - itemHeight) / 2);
            scroll.ChangeView(null, Math.Max(0, centred), null, disableAnimation: true);
        });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer found)
            {
                return found;
            }

            if (FindScrollViewer(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
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
        Tiles.ItemsSource = null;
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

        // The grid shows the picture itself; the list modes show the file-type icon, which is
        // one shell call per extension however many rows there are.
        if (args.Item is EntryRow { Thumbnail: null })
        {
            // Deferred to a later phase so the row shows its text immediately and the picture
            // catches up.
            args.RegisterUpdateCallback(LoadThumbnailAsync);
        }

        args.Handled = true;
    }

    private async void LoadThumbnailAsync(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not EntryRow { Thumbnail: null } row)
        {
            return;
        }

        CancellationToken token = _loadCancellation?.Token ?? CancellationToken.None;

        try
        {
            byte[]? bytes;

            if (ViewMode == ExplorerViewMode.Thumbnails && row.IsImage)
            {
                // Entries inside an archive are extracted first; ordinary paths pass through.
                string real = await _archives.MaterialiseAsync(row.Entry.FullPath, token);
                bytes = await _thumbnails.GetThumbnailAsync(real, row.Entry.Modified, ThumbnailEdge, token);
            }
            else
            {
                bytes = await _typeIcons.GetAsync(
                    row.Entry.Extension, row.Entry.Kind == EntryKind.Folder, ThumbnailEdge, token);
            }

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
        catch (Exception exception)
        {
            // Runs once per row while scrolling, from an async void the framework calls: an
            // unreadable file, a broken archive or a decoder that dislikes the bytes would
            // otherwise take the whole application down. A row without a picture is enough.
            _logger.Log(LogLevel.Debug, $"No thumbnail for '{row.Entry.FullPath}': {exception.Message}");
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
        switch (Items.SelectedItem)
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
        // Random Explorer shuffles what is on screen but must not shuffle the gallery itself
        // (docs/REQUIREMENTS.md:7), so the Viewer always gets the normal order.
        IEnumerable<FileSystemEntry> gallery = Criterion == SortCriterion.Random
            ? EntrySorter.Sort(_entries, SortCriterion.Name, SortDirection.Ascending, NaturalStringComparer.Instance)
            : (Items.ItemsSource as IEnumerable<EntryRow>)?.Select(row => row.Entry) ?? [];

        List<string> images = [.. gallery.Where(entry => ImageFormats.IsImage(entry.Name)).Select(entry => entry.FullPath)];

        int index = images.IndexOf(selected.Entry.FullPath);
        if (index >= 0)
        {
            ImageOpenRequested?.Invoke(this, new ViewerRequest(images, index));
        }
    }

    /// <summary>Restores the list selection to one path and scrolls it into view.</summary>
    public void SelectPath(string fullPath)
    {
        if (Items.ItemsSource is not IEnumerable<EntryRow> rows)
        {
            return;
        }

        EntryRow? row = rows.FirstOrDefault(r => string.Equals(r.Entry.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return;
        }

        Items.SelectedItem = row;
        BringIntoMiddle(row);
    }

    private void NavigateUp()
    {
        if (Path.GetDirectoryName(CurrentPath) is { Length: > 0 } parent)
        {
            NavigationRequested?.Invoke(this, parent);
        }
    }

    // ---- Rename and delete ----

    private async void OnRenameClick(object sender, RoutedEventArgs e) => await RenameSelectedAsync();

    private async void OnDeleteClick(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    public async Task RenameSelectedAsync()
    {
        if (Items.SelectedItem is not EntryRow row)
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
        List<FileSystemEntry> selected = [.. Items.SelectedItems.OfType<EntryRow>().Select(row => row.Entry)];
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

    // ---- Clipboard and folder-to-folder operations ----

    /// <summary>Raised for operations that need a folder picker, which lives on the window.</summary>
    public event EventHandler<FileOperationRequest>? OperationRequested;

    public IReadOnlyList<FileSystemEntry> SelectedEntries =>
        [.. Items.SelectedItems.OfType<EntryRow>().Select(row => row.Entry)];

    private void OnCopyClick(object sender, RoutedEventArgs e) => PutOnClipboard(DataPackageOperation.Copy);

    private void OnCutClick(object sender, RoutedEventArgs e) => PutOnClipboard(DataPackageOperation.Move);

    private void OnPasteClick(object sender, RoutedEventArgs e) =>
        OperationRequested?.Invoke(this, new FileOperationRequest(FileOperationRequestKind.Paste, SelectedEntries, CurrentPath));

    private void OnCopyToClick(object sender, RoutedEventArgs e) =>
        OperationRequested?.Invoke(this, new FileOperationRequest(FileOperationRequestKind.CopyTo, SelectedEntries, CurrentPath));

    private void OnMoveToClick(object sender, RoutedEventArgs e) =>
        OperationRequested?.Invoke(this, new FileOperationRequest(FileOperationRequestKind.MoveTo, SelectedEntries, CurrentPath));

    /// <summary>
    /// Uses the Windows clipboard rather than a private one, so Copy here pastes into File
    /// Explorer and vice versa.
    /// </summary>
    private async void PutOnClipboard(DataPackageOperation operation)
    {
        IReadOnlyList<FileSystemEntry> selected = SelectedEntries;
        if (selected.Count == 0 || await RefuseInsideArchiveAsync())
        {
            return;
        }

        DataPackage package = new() { RequestedOperation = operation };
        List<IStorageItem> items = [];

        try
        {
            foreach (FileSystemEntry entry in selected)
            {
                items.Add(entry.Kind == EntryKind.Folder
                    ? await StorageFolder.GetFolderFromPathAsync(entry.FullPath)
                    : await StorageFile.GetFileFromPathAsync(entry.FullPath));
            }

            package.SetStorageItems(items);
            Clipboard.SetContent(package);
        }
        catch (Exception exception)
        {
            // An entry deleted since the listing, or a clipboard another process is holding open.
            // Nothing here is worth an unhandled exception out of an async void.
            _logger.Log(LogLevel.Warning, "Could not put the selection on the clipboard.", exception);
            await ShowErrorAsync(Strings.Get("Clipboard_Failed"));
        }
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
