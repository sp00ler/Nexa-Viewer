using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ViewerPrn.App;

/// <summary>The small prompts the menus share.</summary>
internal static class Dialogs
{
    /// <summary>Asks for a name. Null when cancelled or left blank.</summary>
    public static async Task<string?> AskForNameAsync(XamlRoot xamlRoot, string title, string initial)
    {
        TextBox input = new() { Text = initial, MinWidth = 320 };
        ContentDialog dialog = new()
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = input,
            PrimaryButtonText = Strings.Get("Dlg_Apply"),
            CloseButtonText = Strings.Get("Dlg_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        string name = input.Text.Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>Yes/no, defaulting to no.</summary>
    public static async Task<bool> ConfirmAsync(XamlRoot xamlRoot, string title, string body, string confirmText)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = body,
            PrimaryButtonText = confirmText,
            CloseButtonText = Strings.Get("Dlg_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Picks one of the given names. Null when cancelled.</summary>
    public static async Task<string?> PickAsync(XamlRoot xamlRoot, string title, IReadOnlyList<string> names)
    {
        ComboBox picker = new() { ItemsSource = names, SelectedIndex = 0, MinWidth = 320 };
        ContentDialog dialog = new()
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = picker,
            PrimaryButtonText = Strings.Get("Dlg_Apply"),
            CloseButtonText = Strings.Get("Dlg_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary && picker.SelectedIndex >= 0
            ? names[picker.SelectedIndex]
            : null;
    }
}
