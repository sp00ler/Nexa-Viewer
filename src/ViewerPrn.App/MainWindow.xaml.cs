using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Application.Session;
using ViewerPrn.Application.Settings;
using ViewerPrn.Domain.FileSystem;
using ViewerPrn.Domain.Tabs;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace ViewerPrn.App;

/// <summary>
/// The application shell: menu bar, tab strip and status bar (Phase 1).
/// The tab strip is a view over <see cref="TabSet"/>, which owns the order, the active tab
/// and the 25-tab limit. Nothing is enumerated or decoded here — that starts in Phase 2.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly TabSet _tabs = new();
    private readonly ISettingsStore _settingsStore;
    private readonly ISessionService _sessionService;
    private readonly IFileSystemService _fileSystem;
    private readonly IThumbnailProvider _thumbnails;
    private readonly IImageMetadataReader _metadata;
    private readonly IArchiveService _archives;
    private readonly IFileOperationService _fileOperations;
    private readonly IFavoritesService _favorites;
    private readonly IViewStatisticsService _statistics;
    private FavoritesMenu? _favoritesMenu;
    private readonly ILoggingService _logger;
    private ViewerView? _viewer;
    private AppSettings _settings;
    private bool _suppressSelectionSync;

    public MainWindow(
        AppSettings settings,
        ISettingsStore settingsStore,
        ISessionService sessionService,
        IFileSystemService fileSystem,
        IThumbnailProvider thumbnails,
        IImageMetadataReader metadata,
        IArchiveService archives,
        IFileOperationService fileOperations,
        IFavoritesService favorites,
        IViewStatisticsService statistics,
        ILoggingService logger)
    {
        _metadata = metadata;
        _archives = archives;
        _fileOperations = fileOperations;
        _favorites = favorites;
        _statistics = statistics;
        _settings = settings;
        _settingsStore = settingsStore;
        _sessionService = sessionService;
        _fileSystem = fileSystem;
        _thumbnails = thumbnails;
        _logger = logger;

        InitializeComponent();

        TrySetWindowIcon();
        SetUpFavoritesMenu();
        ApplyStrings();
        ApplyTheme(_settings.Theme);
        CheckThemeMenuItem(_settings.Theme);
        CheckLanguageMenuItem(_settings.Language);
        CheckSortMenuItems(SortCriterion.Name, SortDirection.Ascending);
        UpdateStatusBar();
    }

    /// <summary>
    /// Puts the application icon in the title bar. <c>ApplicationIcon</c> in the project file
    /// already covers the executable and the taskbar; this makes the title bar follow when the
    /// file is present, and does nothing when it is not.
    /// </summary>
    private void TrySetWindowIcon()
    {
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }
    }

    // ---- Favorites ----

    /// <summary>
    /// The menu is rebuilt from the database each time it opens rather than cached, so a target
    /// that disappeared since the last look is shown as broken straight away.
    /// </summary>
    private void SetUpFavoritesMenu()
    {
        _favoritesMenu = new FavoritesMenu(
            _favorites,
            FavoritesMenu_Item,
            Content.XamlRoot,
            () => ActiveView?.CurrentPath,
            path => _ = NavigateActiveTabAsync(path));

        FavoritesMenu_Item.Loaded += async (_, _) => await _favoritesMenu.RefreshAsync();
        FavoritesMenu_Item.Tapped += async (_, _) => await _favoritesMenu.RefreshAsync();
    }

    private async Task NavigateActiveTabAsync(string path)
    {
        if (ActiveView is { } view)
        {
            OnNavigationRequested(view, path);
            await Task.CompletedTask;
        }
    }

    // ---- Copy, Move and paste ----

    /// <summary>
    /// Runs the operations the list cannot: they need a folder picker, which needs the window.
    /// </summary>
    private async void OnOperationRequested(object? sender, FileOperationRequest request)
    {
        if (sender is not FolderView view)
        {
            return;
        }

        try
        {
            (FileOperationKind kind, IReadOnlyList<FileSystemEntry> sources, string? destination) =
                await ResolveOperationAsync(request);

            if (destination is null || sources.Count == 0)
            {
                return;
            }

            FileOperationRunner runner = new(_fileOperations, _thumbnails, Content.XamlRoot);
            FileOperationResult result = await runner.RunAsync(kind, sources, destination);

            await view.LoadAsync(view.CurrentPath);
            await ReportResultAsync(result);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Error, "The file operation could not be started.", exception);
            await ShowMessageAsync(Strings.Get("Error_Title"), Strings.Get("Op_Failures"));
        }
    }

    private async Task<(FileOperationKind Kind, IReadOnlyList<FileSystemEntry> Sources, string? Destination)>
        ResolveOperationAsync(FileOperationRequest request)
    {
        if (request.Kind == FileOperationRequestKind.Paste)
        {
            return await ResolvePasteAsync(request);
        }

        if (request.Entries.Count == 0)
        {
            return (FileOperationKind.Copy, [], null);
        }

        FileOperationKind kind = request.Kind == FileOperationRequestKind.MoveTo
            ? FileOperationKind.Move
            : FileOperationKind.Copy;

        return (kind, request.Entries, await PickFolderAsync());
    }

    /// <summary>
    /// Paste reads the Windows clipboard, so items copied in File Explorer paste here. The
    /// clipboard's own requested operation decides copy versus move.
    /// </summary>
    private async Task<(FileOperationKind, IReadOnlyList<FileSystemEntry>, string?)> ResolvePasteAsync(
        FileOperationRequest request)
    {
        DataPackageView clipboard = Clipboard.GetContent();
        if (!clipboard.Contains(StandardDataFormats.StorageItems))
        {
            await ShowMessageAsync(Strings.Get("Error_Title"), Strings.Get("Op_NothingToPaste"));
            return (FileOperationKind.Copy, [], null);
        }

        IReadOnlyList<IStorageItem> items = await clipboard.GetStorageItemsAsync();
        List<FileSystemEntry> sources = [];

        foreach (IStorageItem item in items)
        {
            if (item is StorageFile)
            {
                FileInfo info = new(item.Path);
                sources.Add(new FileSystemEntry(info.Name, info.FullName, EntryKind.File, info.Length, info.LastWriteTime));
            }
            else
            {
                DirectoryInfo info = new(item.Path);
                sources.Add(new FileSystemEntry(info.Name, info.FullName, EntryKind.Folder, 0, info.LastWriteTime));
            }
        }

        FileOperationKind kind = clipboard.RequestedOperation.HasFlag(DataPackageOperation.Move)
            ? FileOperationKind.Move
            : FileOperationKind.Copy;

        return (kind, sources, request.CurrentPath);
    }

    private async Task<string?> PickFolderAsync()
    {
        FolderPicker picker = new() { CommitButtonText = Strings.Get("Op_PickDestination") };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task ReportResultAsync(FileOperationResult result)
    {
        if (result.Cancelled)
        {
            await ShowMessageAsync(Strings.Get("Op_Done_Title"), Strings.Get("Op_Cancelled"));
            return;
        }

        string body = Strings.Format("Op_Done_Body", result.Copied, result.Replaced, result.Renamed, result.Skipped);
        if (result.Failures.Count > 0)
        {
            body += Environment.NewLine + Strings.Format("Op_Failures", result.Failures.Count);
        }

        await ShowMessageAsync(Strings.Get("Op_Done_Title"), body);
    }

    // ---- Viewer ----

    private async void OnImageOpenRequested(object? sender, ViewerRequest request)
    {
        _viewer ??= CreateViewer();

        Tabs.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
        _viewer.Visibility = Visibility.Visible;

        await _viewer.OpenAsync(request.Images, request.StartIndex);
    }

    private ViewerView CreateViewer()
    {
        ViewerView viewer = new(_metadata, _archives, _statistics, _logger) { Visibility = Visibility.Collapsed };
        viewer.ExitRequested += OnViewerExit;
        viewer.CurrentChanged += (_, _) => UpdateWindowTitle();

        // F6 minimises, and only from the Viewer (docs/REQUIREMENTS.md:13).
        viewer.MinimizeRequested += (_, _) => (AppWindow.Presenter as OverlappedPresenter)?.Minimize();

        ContentHost.Children.Add(viewer);
        return viewer;
    }

    /// <summary>
    /// Leaving the Viewer restores the Explorer selection to exactly the image that was on
    /// screen (docs/VIEWER.md:19).
    /// </summary>
    private void OnViewerExit(object? sender, EventArgs e)
    {
        string? path = _viewer?.CurrentPath;

        _viewer?.Close();
        if (_viewer is not null)
        {
            _viewer.Visibility = Visibility.Collapsed;
        }

        Tabs.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;

        if (path is not null)
        {
            ActiveView?.SelectPath(path);
        }

        UpdateWindowTitle();
        SaveSession();
    }

    /// <summary>The Viewer shows the full source path in the title (docs/REQUIREMENTS.md:13).</summary>
    private void UpdateWindowTitle() =>
        Title = _viewer is { Visibility: Visibility.Visible, CurrentPath: { } path } ? path : "NexaViewer";

    // ---- Session ----

    /// <summary>
    /// Rebuilds the tabs from a restored session. Only the active tab is listed; the rest load
    /// when they are first shown (docs/REQUIREMENTS.md:10 — do not load all tabs eagerly).
    /// </summary>
    public async Task RestoreAsync(SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (TabState tab in state.Tabs)
        {
            AddTab(tab.Path, new FolderView(_fileSystem, _archives, _thumbnails, _logger, tab.Criterion, tab.Direction, tab.SelectedNames));
        }

        if (state.ActiveIndex >= 0)
        {
            _suppressSelectionSync = true;
            _tabs.Activate(state.ActiveIndex);
            Tabs.SelectedIndex = state.ActiveIndex;
            _suppressSelectionSync = false;

            await LoadActiveTabAsync();
        }

        _logger.Log(LogLevel.Information, $"Restored {state.Tabs.Count} tab(s).");
        UpdateStatusBar();
    }

    /// <summary>Current state, captured synchronously on the UI thread.</summary>
    public SessionState CaptureSession() => new()
    {
        ActiveIndex = _tabs.ActiveIndex,
        Tabs = [.. _tabs.Tabs.Select((tab, index) =>
        {
            FolderView? view = ViewAt(index);
            return new TabState
            {
                Path = tab.Path,
                Criterion = view?.Criterion ?? SortCriterion.Name,
                Direction = view?.Direction ?? SortDirection.Ascending,
                SelectedNames = view?.SelectedNames ?? [],
            };
        })],
    };

    /// <summary>
    /// Committed on every structural change rather than only at shutdown, so an abnormal
    /// termination restores the last committed state instead of nothing.
    /// </summary>
    private async void SaveSession()
    {
        try
        {
            await _sessionService.SaveAsync(CaptureSession());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Error, "Could not save the session.", exception);
        }
    }

    /// <summary>Source path of the active tab, for crash reports.</summary>
    public string? ActivePath => _tabs.Active?.Path;

    /// <summary>Active tab index, for crash reports. Null when no tab is open.</summary>
    public int? ActiveTabIndex => _tabs.ActiveIndex >= 0 ? _tabs.ActiveIndex : null;

    /// <summary>Shown in the status bar once the shell is up (docs/PERFORMANCE.md).</summary>
    public void ReportStartupDuration(TimeSpan duration)
    {
        StartupText.Text = Strings.Format("Status_Startup", duration.TotalMilliseconds);
    }

    private void ApplyStrings()
    {
        FileMenu.Title = Strings.Get("Menu_File");
        NewTabMenuItem.Text = Strings.Get("Menu_OpenFolderNewTab");
        CloseTabMenuItem.Text = Strings.Get("Menu_CloseTab");
        ExitMenuItem.Text = Strings.Get("Menu_Exit");

        ViewMenu.Title = Strings.Get("Menu_View");
        SortSubMenu.Text = Strings.Get("Menu_Sort");
        SortNameItem.Text = Strings.Get("Sort_Name");
        SortSizeItem.Text = Strings.Get("Sort_Size");
        SortTypeItem.Text = Strings.Get("Sort_Type");
        SortModifiedItem.Text = Strings.Get("Sort_Modified");
        SortAscendingItem.Text = Strings.Get("Sort_Ascending");
        SortDescendingItem.Text = Strings.Get("Sort_Descending");
        ThemeSubMenu.Text = Strings.Get("Menu_Theme");
        SystemThemeItem.Text = Strings.Get("Theme_System");
        LightThemeItem.Text = Strings.Get("Theme_Light");
        DarkThemeItem.Text = Strings.Get("Theme_Dark");
        LanguageSubMenu.Text = Strings.Get("Menu_Language");
        RussianLanguageItem.Text = Strings.Get("Lang_Russian");
        EnglishLanguageItem.Text = Strings.Get("Lang_English");
        AccentMenuItem.Text = Strings.Get("Menu_AccentColour");
        ResetAccentMenuItem.Text = Strings.Get("Menu_UseSystemAccent");

        HelpMenu.Title = Strings.Get("Menu_Help");
        AboutMenuItem.Text = Strings.Get("Menu_About");
    }

    // ---- Tabs ----

    private async void OnAddTabButtonClick(TabView sender, object args) => await OpenFolderInNewTabAsync();

    private async void OnOpenFolderInNewTab(object sender, RoutedEventArgs e) => await OpenFolderInNewTabAsync();

    private async Task OpenFolderInNewTabAsync()
    {
        if (!_tabs.CanOpen)
        {
            await ShowMessageAsync(
                Strings.Get("TabLimit_Title"),
                Strings.Format("TabLimit_Body", TabSet.MaxTabs));
            return;
        }

        FolderPicker picker = new();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        AddTab(folder.Path, new FolderView(_fileSystem, _archives, _thumbnails, _logger));

        _suppressSelectionSync = true;
        Tabs.SelectedIndex = _tabs.ActiveIndex;
        _suppressSelectionSync = false;

        await LoadActiveTabAsync();
        SaveSession();
        UpdateStatusBar();
    }

    private void AddTab(string path, FolderView view)
    {
        TabDescriptor tab = _tabs.Open(path, NameOf(path));
        _logger.Log(LogLevel.Information, $"Opened tab '{tab.Path}'.");

        view.NavigationRequested += OnNavigationRequested;
        view.ImageOpenRequested += OnImageOpenRequested;
        view.OperationRequested += OnOperationRequested;
        view.SelectionChanged += (_, _) => UpdateStatusBar();

        Tabs.TabItems.Add(new TabViewItem
        {
            Header = tab.Title,
            Tag = tab.Id,
            Content = view,
        });
    }

    /// <summary>
    /// Looks the view up by index rather than through <c>Tabs.SelectedItem</c>: right after
    /// <c>SelectedIndex</c> is assigned the TabView has not realised the item yet, so the
    /// selection-based lookup returns null and the load would silently depend on a later
    /// SelectionChanged event instead.
    /// </summary>
    private async Task LoadActiveTabAsync()
    {
        if (_tabs.Active is { } tab && ViewAt(_tabs.ActiveIndex) is { } view)
        {
            await view.EnsureLoadedAsync(tab.Path);
        }
    }

    private async void OnNavigationRequested(object? sender, string path)
    {
        if (sender is not FolderView view || Tabs.SelectedItem is not TabViewItem item)
        {
            return;
        }

        TabDescriptor updated = _tabs.UpdatePath(_tabs.ActiveIndex, path, NameOf(path));
        item.Header = updated.Title;

        await view.LoadAsync(path);
        SaveSession();
        UpdateStatusBar();
    }

    /// <summary>A drive root has no file name, so fall back to the path itself ("E:\").</summary>
    private static string NameOf(string path)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        return name.Length == 0 ? path : name;
    }

    private FolderView? ActiveView => (Tabs.SelectedItem as TabViewItem)?.Content as FolderView;

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        int index = Tabs.TabItems.IndexOf(args.Tab);
        if (index < 0)
        {
            return;
        }

        CloseTabAt(index);
    }

    private void OnCloseActiveTab(object sender, RoutedEventArgs e)
    {
        if (_tabs.ActiveIndex >= 0)
        {
            CloseTabAt(_tabs.ActiveIndex);
        }
    }

    private void CloseTabAt(int index)
    {
        if ((Tabs.TabItems[index] as TabViewItem)?.Content is FolderView view)
        {
            view.NavigationRequested -= OnNavigationRequested;
            view.ImageOpenRequested -= OnImageOpenRequested;
            view.OperationRequested -= OnOperationRequested;
            view.Dispose();
        }

        _tabs.Close(index);

        _suppressSelectionSync = true;
        Tabs.TabItems.RemoveAt(index);
        Tabs.SelectedIndex = _tabs.ActiveIndex;
        _suppressSelectionSync = false;

        SaveSession();
        UpdateStatusBar();
    }

    private async void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionSync || Tabs.SelectedIndex < 0)
        {
            return;
        }

        _tabs.Activate(Tabs.SelectedIndex);
        UpdateStatusBar();

        // A restored tab is listed the first time it is shown, not at startup.
        await LoadActiveTabAsync();
        UpdateStatusBar();
    }

    private FolderView? ViewAt(int index) =>
        index >= 0 && index < Tabs.TabItems.Count
            ? (Tabs.TabItems[index] as TabViewItem)?.Content as FolderView
            : null;

    private void UpdateStatusBar()
    {
        PathText.Text = _tabs.Active?.Path ?? Strings.Get("Status_NoFolder");
        TabCountText.Text = Strings.Format("Status_Tabs", _tabs.Count, TabSet.MaxTabs);
        CloseTabMenuItem.IsEnabled = _tabs.Count > 0;

        FolderView? view = ActiveView;
        ItemsText.Text = view is null
            ? string.Empty
            : view.SelectedCount > 0
                ? Strings.Format("Status_Selected", view.SelectedCount)
                : Strings.Format("Status_Items", view.ItemCount);

        if (view is not null)
        {
            CheckSortMenuItems(view.Criterion, view.Direction);
        }

        // The new-tab command stays enabled at the limit so that invoking it can explain why
        // nothing opened, instead of the command silently greying out.
    }

    // ---- Sorting ----

    private async void OnSortCriterionSelected(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string tag }
            && Enum.TryParse(tag, out SortCriterion criterion)
            && ActiveView is { } view)
        {
            await view.ApplySortAsync(criterion, view.Direction);
            SaveSession();
        }
    }

    private async void OnSortDirectionSelected(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string tag }
            && Enum.TryParse(tag, out SortDirection direction)
            && ActiveView is { } view)
        {
            await view.ApplySortAsync(view.Criterion, direction);
            SaveSession();
        }
    }

    private void CheckSortMenuItems(SortCriterion criterion, SortDirection direction)
    {
        SortNameItem.IsChecked = criterion == SortCriterion.Name;
        SortSizeItem.IsChecked = criterion == SortCriterion.Size;
        SortTypeItem.IsChecked = criterion == SortCriterion.Type;
        SortModifiedItem.IsChecked = criterion == SortCriterion.Modified;
        SortAscendingItem.IsChecked = direction == SortDirection.Ascending;
        SortDescendingItem.IsChecked = direction == SortDirection.Descending;
    }

    // ---- Theme, language and accent ----

    private async void OnThemeSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string tag } || !Enum.TryParse(tag, out ThemePreference theme))
        {
            return;
        }

        ApplyTheme(theme);
        _settings = _settings with { Theme = theme };
        await SaveSettingsAsync();
    }

    private void ApplyTheme(ThemePreference theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private void CheckThemeMenuItem(ThemePreference theme)
    {
        SystemThemeItem.IsChecked = theme == ThemePreference.System;
        LightThemeItem.IsChecked = theme == ThemePreference.Light;
        DarkThemeItem.IsChecked = theme == ThemePreference.Dark;
    }

    private void CheckLanguageMenuItem(LanguagePreference language)
    {
        RussianLanguageItem.IsChecked = language == LanguagePreference.Russian;
        EnglishLanguageItem.IsChecked = language == LanguagePreference.English;
    }

    private async void OnLanguageSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string tag } || !Enum.TryParse(tag, out LanguagePreference language))
        {
            return;
        }

        if (language == _settings.Language)
        {
            return;
        }

        _settings = _settings with { Language = language };
        await SaveSettingsAsync();
        await ShowMessageAsync(Strings.Get("LangSaved_Title"), Strings.Get("LangSaved_Body"));
    }

    private async void OnChooseAccent(object sender, RoutedEventArgs e)
    {
        ColorPicker picker = new()
        {
            IsAlphaEnabled = false,
            IsHexInputVisible = true,
            Color = AccentColors.ToColor(_settings.AccentColorArgb) ?? AccentColors.SystemAccent(),
        };

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = Strings.Get("Accent_Title"),
            Content = picker,
            PrimaryButtonText = Strings.Get("Dlg_Apply"),
            CloseButtonText = Strings.Get("Dlg_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _settings = _settings with { AccentColorArgb = AccentColors.ToArgb(picker.Color) };
        await SaveSettingsAsync();
        await ShowMessageAsync(Strings.Get("AccentSaved_Title"), Strings.Get("AccentSaved_Body"));
    }

    private async void OnResetAccent(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { AccentColorArgb = null };
        await SaveSettingsAsync();
        await ShowMessageAsync(Strings.Get("AccentReset_Title"), Strings.Get("AccentReset_Body"));
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (IOException exception)
        {
            _logger.Log(LogLevel.Error, "Could not save settings.", exception);
            await ShowMessageAsync(Strings.Get("Settings_NotSaved_Title"), Strings.Get("Settings_NotSaved_IO"));
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Log(LogLevel.Error, "Could not save settings.", exception);
            await ShowMessageAsync(Strings.Get("Settings_NotSaved_Title"), Strings.Get("Settings_NotSaved_Denied"));
        }
    }

    // ---- Misc ----

    private async void OnAbout(object sender, RoutedEventArgs e) => await ShowMessageAsync(
        Strings.Get("About_Title"),
        Strings.Format("About_Body", Infrastructure.PlatformInfo.RuntimeVersion, Infrastructure.PlatformInfo.OperatingSystem));

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private async Task ShowMessageAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = Strings.Get("Dlg_OK"),
        };

        await dialog.ShowAsync();
    }
}
