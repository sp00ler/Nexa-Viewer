using Microsoft.UI.Xaml.Controls;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Application.Session;
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

    public SessionsMenu(
        JsonSessionLibraryStore library,
        ISessionService lastSession,
        MenuBarItem menu,
        Func<SessionState> capture,
        Func<SessionState, Task> open)
    {
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
