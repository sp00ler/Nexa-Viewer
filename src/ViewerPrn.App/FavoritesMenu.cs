using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.Archives;

namespace ViewerPrn.App;

/// <summary>
/// Builds the Favorites menu from the database each time it is opened, so it always reflects
/// what is stored and whether each target still exists (docs/REQUIREMENTS.md:31).
/// </summary>
public sealed class FavoritesMenu
{
    private readonly IFavoritesService _favorites;
    private readonly MenuBarItem _menu;
    private readonly XamlRoot _xamlRoot;
    private readonly Func<string?> _currentPath;
    private readonly Action<string> _navigate;

    public FavoritesMenu(
        IFavoritesService favorites,
        MenuBarItem menu,
        XamlRoot xamlRoot,
        Func<string?> currentPath,
        Action<string> navigate)
    {
        _favorites = favorites;
        _menu = menu;
        _xamlRoot = xamlRoot;
        _currentPath = currentPath;
        _navigate = navigate;

        _menu.Title = Strings.Get("Menu_Favorites");
        _menu.Items.Add(new MenuFlyoutItem { Text = Strings.Get("Fav_Empty"), IsEnabled = false });
    }

    /// <summary>Rebuilds the menu. Called before it is shown.</summary>
    public async Task RefreshAsync()
    {
        IReadOnlyList<FavoriteGroup> groups = await _favorites.GetGroupsAsync();

        _menu.Items.Clear();

        MenuFlyoutItem add = new() { Text = Strings.Get("Fav_AddCurrent") };
        add.Click += async (_, _) => await AddCurrentAsync(groups);
        add.IsEnabled = _currentPath() is not null;
        _menu.Items.Add(add);

        MenuFlyoutItem newGroup = new() { Text = Strings.Get("Fav_NewGroup") };
        newGroup.Click += async (_, _) => await CreateGroupAsync();
        _menu.Items.Add(newGroup);

        if (groups.Count == 0)
        {
            return;
        }

        _menu.Items.Add(new MenuFlyoutSeparator());

        foreach (FavoriteGroup group in groups)
        {
            _menu.Items.Add(BuildGroup(group));
        }
    }

    private MenuFlyoutSubItem BuildGroup(FavoriteGroup group)
    {
        MenuFlyoutSubItem item = new() { Text = group.Name };

        foreach (Favorite favorite in group.Items)
        {
            // A broken target is shown, not hidden: the user is the one who decides whether to
            // repair or remove it.
            MenuFlyoutItem entry = new()
            {
                Text = favorite.Exists
                    ? favorite.Path
                    : Strings.Format("Fav_Broken", favorite.Path),
            };

            entry.Click += async (_, _) =>
            {
                if (favorite.Exists)
                {
                    _navigate(favorite.Path);
                }
                else
                {
                    await OfferRepairAsync(favorite);
                }
            };

            item.Items.Add(entry);
        }

        if (group.Items.Count > 0)
        {
            item.Items.Add(new MenuFlyoutSeparator());
        }

        MenuFlyoutItem rename = new() { Text = Strings.Get("Fav_RenameGroup") };
        rename.Click += async (_, _) => await RenameGroupAsync(group);
        item.Items.Add(rename);

        MenuFlyoutItem delete = new() { Text = Strings.Get("Fav_DeleteGroup") };
        delete.Click += async (_, _) => await DeleteGroupAsync(group);
        item.Items.Add(delete);

        return item;
    }

    private async Task AddCurrentAsync(IReadOnlyList<FavoriteGroup> groups)
    {
        if (_currentPath() is not { } path)
        {
            return;
        }

        long groupId;
        if (groups.Count == 0)
        {
            if (await AskForNameAsync(Strings.Get("Fav_NewGroup"), string.Empty) is not { } name)
            {
                return;
            }

            groupId = await _favorites.CreateGroupAsync(name);
        }
        else
        {
            ComboBox picker = new()
            {
                ItemsSource = groups.Select(group => group.Name).ToList(),
                SelectedIndex = 0,
                MinWidth = 240,
            };

            ContentDialog dialog = new()
            {
                XamlRoot = _xamlRoot,
                Title = Strings.Get("Fav_AddCurrent"),
                Content = picker,
                PrimaryButtonText = Strings.Get("Dlg_Apply"),
                CloseButtonText = Strings.Get("Dlg_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary || picker.SelectedIndex < 0)
            {
                return;
            }

            groupId = groups[picker.SelectedIndex].Id;
        }

        await _favorites.AddAsync(groupId, path, KindOf(path));
        await RefreshAsync();
    }

    private static FavoriteKind KindOf(string path) =>
        ArchiveFormats.IsArchive(path) ? FavoriteKind.Archive
        : Directory.Exists(path) ? FavoriteKind.Folder
        : FavoriteKind.Image;

    private async Task CreateGroupAsync()
    {
        if (await AskForNameAsync(Strings.Get("Fav_NewGroup"), string.Empty) is not { } name)
        {
            return;
        }

        await _favorites.CreateGroupAsync(name);
        await RefreshAsync();
    }

    private async Task RenameGroupAsync(FavoriteGroup group)
    {
        if (await AskForNameAsync(Strings.Get("Fav_RenameGroup"), group.Name) is not { } name)
        {
            return;
        }

        await _favorites.RenameGroupAsync(group.Id, name);
        await RefreshAsync();
    }

    private async Task DeleteGroupAsync(FavoriteGroup group)
    {
        ContentDialog confirm = new()
        {
            XamlRoot = _xamlRoot,
            Title = Strings.Get("Fav_DeleteGroup"),
            Content = Strings.Format("Fav_DeleteGroupBody", group.Name),
            PrimaryButtonText = Strings.Get("Cmd_Delete"),
            CloseButtonText = Strings.Get("Dlg_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await _favorites.DeleteGroupAsync(group.Id);
            await RefreshAsync();
        }
    }

    /// <summary>Broken targets can be repaired or removed (docs/REQUIREMENTS.md:31).</summary>
    private async Task OfferRepairAsync(Favorite favorite)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = _xamlRoot,
            Title = Strings.Get("Fav_BrokenTitle"),
            Content = Strings.Format("Fav_BrokenBody", favorite.Path),
            PrimaryButtonText = Strings.Get("Fav_Repair"),
            SecondaryButtonText = Strings.Get("Cmd_Delete"),
            CloseButtonText = Strings.Get("Dlg_Cancel"),
        };

        switch (await dialog.ShowAsync())
        {
            case ContentDialogResult.Primary:
                if (await AskForNameAsync(Strings.Get("Fav_Repair"), favorite.Path) is { } replacement)
                {
                    await _favorites.RepairAsync(favorite.Id, replacement);
                    await RefreshAsync();
                }

                break;

            case ContentDialogResult.Secondary:
                await _favorites.RemoveAsync(favorite.Id);
                await RefreshAsync();
                break;

            default:
                break;
        }
    }

    private Task<string?> AskForNameAsync(string title, string initial) =>
        Dialogs.AskForNameAsync(_xamlRoot, title, initial);
}
