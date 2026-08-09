using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ViewerPrn.App;

/// <summary>
/// The small prompts the menus share.
/// <para>
/// Each takes the element the prompt belongs to and reads its <see cref="UIElement.XamlRoot"/>
/// when it shows. Capturing the root earlier does not work: it is null until the window's content
/// is in the tree, and a dialog built with a null root throws "This element does not have a
/// XamlRoot" at the moment the user asks for it.
/// </para>
/// </summary>
internal static class Dialogs
{
    /// <summary>Asks for a name. Null when cancelled or left blank.</summary>
    public static async Task<string?> AskForNameAsync(FrameworkElement owner, string title, string initial)
    {
        XamlRoot xamlRoot = owner.XamlRoot;
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

    /// <summary>Tells the user something. One button.</summary>
    public static async Task MessageAsync(FrameworkElement owner, string title, string body)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = owner.XamlRoot,
            Title = title,
            Content = body,
            CloseButtonText = Strings.Get("Dlg_OK"),
        };

        await dialog.ShowAsync();
    }

    /// <summary>Yes/no, defaulting to no.</summary>
    public static async Task<bool> ConfirmAsync(FrameworkElement owner, string title, string body, string confirmText)
    {
        XamlRoot xamlRoot = owner.XamlRoot;
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
    public static async Task<string?> PickAsync(FrameworkElement owner, string title, IReadOnlyList<string> names)
    {
        XamlRoot xamlRoot = owner.XamlRoot;
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
