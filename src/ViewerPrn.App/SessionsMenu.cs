using System.Text;
using Microsoft.UI.Xaml.Controls;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Application.Session;
using ViewerPrn.Domain.Archives;
using ViewerPrn.Infrastructure.Session;

namespace ViewerPrn.App;

/// <summary>
/// Saved tab states, in the manner of old Opera: the shell always starts on Documents, and a
/// state is opened by hand from this menu (DECISION-0036). Rebuilt from the file each time it is
/// shown, like the Favorites menu.
/// </summary>
public sealed class SessionsMenu
{
    private readonly JsonSessionLibraryStore _library;
    private readonly ISessionService _lastSession;
    private readonly MenuBarItem _menu;
    private readonly Func<SessionState> _capture;
    private readonly Func<SessionState, Task> _open;
    private readonly Func<Task<string?>> _pickTextFile;

    public SessionsMenu(
        JsonSessionLibraryStore library,
        ISessionService lastSession,
        MenuBarItem menu,
        Func<SessionState> capture,
        Func<SessionState, Task> open,
        Func<Task<string?>> pickTextFile)
    {
        _pickTextFile = pickTextFile;
        _library = library;
        _lastSession = lastSession;
        _menu = menu;
        _capture = capture;
        _open = open;

        _menu.Title = Strings.Get("Menu_Sessions");
    }

    public async Task RefreshAsync()
    {
        SessionLibrary library = await _library.LoadAsync();

        _menu.Items.Clear();

        MenuFlyoutItem save = new() { Text = Strings.Get("Sess_Save") };
        save.Click += async (_, _) => await SaveAsync(library);
        _menu.Items.Add(save);

        MenuFlyoutItem import = new() { Text = Strings.Get("Sess_Import") };
        import.Click += async (_, _) => await ImportAsync(library);
        _menu.Items.Add(import);

        if (library.Sessions.Count > 0)
        {
            MenuFlyoutItem delete = new() { Text = Strings.Get("Sess_Delete") };
            delete.Click += async (_, _) => await DeleteAsync(library);
            _menu.Items.Add(delete);
        }

        _menu.Items.Add(new MenuFlyoutSeparator());

        // The tabs from the last run, still written on the way out. Kept here so closing without
        // saving is recoverable, which is the one thing the old automatic restore was good for.
        SessionState last = await _lastSession.LoadAsync();
        MenuFlyoutItem lastItem = new()
        {
            Text = Strings.Format("Sess_LastRun", last.Tabs.Count),
            IsEnabled = last.Tabs.Count > 0,
        };

        lastItem.Click += async (_, _) => await _open(last);
        _menu.Items.Add(lastItem);

        if (library.Sessions.Count == 0)
        {
            _menu.Items.Add(new MenuFlyoutItem { Text = Strings.Get("Sess_Empty"), IsEnabled = false });
            return;
        }

        _menu.Items.Add(new MenuFlyoutSeparator());

        foreach (SavedSession session in library.Sessions)
        {
            MenuFlyoutItem entry = new()
            {
                Text = Strings.Format("Sess_Entry", session.Name, session.State.Tabs.Count),
            };

            entry.Click += async (_, _) => await _open(session.State);
            _menu.Items.Add(entry);
        }
    }

    /// <summary>Saves the tabs as they are now. An existing name is replaced, once confirmed.</summary>
    private async Task SaveAsync(SessionLibrary library)
    {
        if (await Dialogs.AskForNameAsync(_menu, Strings.Get("Sess_Save"), string.Empty) is not { } name)
        {
            return;
        }

        if (library.Find(name) is not null
            && !await Dialogs.ConfirmAsync(
                _menu,
                Strings.Get("Sess_Overwrite"),
                Strings.Format("Sess_OverwriteBody", name),
                Strings.Get("Dlg_Apply")))
        {
            return;
        }

        await _library.SaveAsync(library.With(name, _capture()));
        await RefreshAsync();
    }

    /// <summary>
    /// Builds a state from a text file of folder paths, one per line, written the way Explorer
    /// shows them. Blank lines and lines starting with # are skipped; a path that does not exist
    /// still gets a tab, opened on Documents. The state is saved under the file's name and
    /// opened straight away.
    /// </summary>
    private async Task ImportAsync(SessionLibrary library)
    {
        if (await _pickTextFile() is not { } file)
        {
            return;
        }

        // Detects UTF-8 with or without a BOM; a file saved as ANSI still reads, which matters
        // because Notepad's "ANSI" is the default on a Russian Windows.
        string[] lines = await File.ReadAllLinesAsync(file, Encoding.UTF8);

        // A path that is not there still gets its tab, on Documents: the file's line count is
        // the tab count, so the list stays comparable with what opened (user, 2026-08-09).
        string fallback = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        List<string> paths = [];
        List<string> missing = [];
        foreach (string line in lines)
        {
            string path = line.Trim().Trim('"');
            if (path.Length == 0 || path.StartsWith('#'))
            {
                continue;
            }

            bool exists = Directory.Exists(path)
                || (ArchiveLocation.TryParse(path, out ArchiveLocation? location) && File.Exists(location.ArchiveFilePath));

            if (!exists)
            {
                missing.Add(path);
            }

            paths.Add(exists ? path : fallback);
        }

        // Said before anything opens: the paths themselves are what the user needs to see.
        if (missing.Count > 0)
        {
            await Dialogs.MessageAsync(
                _menu,
                Strings.Get("Sess_Import"),
                Strings.Format(
                    "Sess_ImportSkipped",
                    paths.Count - missing.Count,
                    missing.Count,
                    Environment.NewLine + string.Join(Environment.NewLine, missing.Take(10))));
        }

        if (paths.Count == 0)
        {
            return;
        }

        string name = Path.GetFileNameWithoutExtension(file);
        SessionState state = new()
        {
            ActiveIndex = 0,
            Tabs = [.. paths.Select(path => new TabState { Path = path })],
        };

        await _library.SaveAsync(library.With(name, state));
        await RefreshAsync();
        await _open(state);
    }

    private async Task DeleteAsync(SessionLibrary library)
    {
        IReadOnlyList<string> names = [.. library.Sessions.Select(session => session.Name)];
        if (await Dialogs.PickAsync(_menu, Strings.Get("Sess_Delete"), names) is not { } name)
        {
            return;
        }

        await _library.SaveAsync(library.Without(name));
        await RefreshAsync();
    }
}
