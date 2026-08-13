using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.FileSystem;
using ViewerPrn.Domain.Images;
using Windows.Storage.Streams;

namespace ViewerPrn.App;

/// <summary>
/// Runs a Copy or Move and owns the two dialogs the specification asks for: a progress dialog
/// with the current item, counts, throughput and cancellation, and a conflict dialog that shows
/// both sides before anything is overwritten (docs/FILE_OPERATIONS.md).
/// </summary>
public sealed class FileOperationRunner
{
    private readonly IFileOperationService _operations;
    private readonly IThumbnailProvider _thumbnails;
    private readonly XamlRoot _xamlRoot;

    /// <summary>
    /// The progress dialog while one is up. Windows allows exactly one ContentDialog at a time,
    /// and a conflict arrives while this one is showing, so it steps aside for the question and
    /// comes back after.
    /// </summary>
    private ContentDialog? _progressDialog;

    public FileOperationRunner(IFileOperationService operations, IThumbnailProvider thumbnails, XamlRoot xamlRoot)
    {
        _operations = operations;
        _thumbnails = thumbnails;
        _xamlRoot = xamlRoot;
    }

    public async Task<FileOperationResult> RunAsync(
        FileOperationKind kind,
        IReadOnlyList<FileSystemEntry> sources,
        string destinationDirectory)
    {
        using CancellationTokenSource cancellation = new();

        ProgressBar bar = new() { Minimum = 0, Maximum = 100, Width = 380 };
        TextBlock currentItem = new() { TextTrimming = TextTrimming.CharacterEllipsis };
        TextBlock detail = new() { Opacity = 0.7, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["CaptionTextBlockStyle"] };

        ContentDialog progressDialog = new()
        {
            XamlRoot = _xamlRoot,
            Title = Strings.Get(kind == FileOperationKind.Copy ? "Op_Copying" : "Op_Moving"),
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { currentItem, bar, detail },
            },
            CloseButtonText = Strings.Get("Dlg_Cancel"),
        };

        progressDialog.CloseButtonClick += (_, _) => cancellation.Cancel();

        Progress<FileOperationProgress> progress = new(report =>
        {
            bar.Value = report.Percent;
            currentItem.Text = report.CurrentItem;
            detail.Text = Describe(report);
        });

        // Shown without awaiting: the dialog stays up while the operation runs behind it.
        _progressDialog = progressDialog;
        _ = progressDialog.ShowAsync();

        try
        {
            return await _operations.ExecuteAsync(
                kind, sources, destinationDirectory, AskAboutConflictAsync, progress, cancellation.Token);
        }
        finally
        {
            _progressDialog = null;
            progressDialog.Hide();
        }
    }

    private static string Describe(FileOperationProgress report)
    {
        List<string> parts =
        [
            Strings.Format("Op_ItemsProgress", report.ItemsDone, report.ItemsTotal),
            $"{EntryRow.FormatSize(report.BytesDone)} / {EntryRow.FormatSize(report.BytesTotal)}",
        ];

        if (report.BytesPerSecond is { } speed)
        {
            parts.Add(Strings.Format("Op_Speed", EntryRow.FormatSize((long)speed)));
        }

        if (report.Remaining is { } remaining)
        {
            parts.Add(Strings.Format("Op_Remaining", remaining.ToString(@"m\:ss", CultureInfo.CurrentCulture)));
        }

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// The conflict dialog. Shows a preview of each side where one is available, plus the facts
    /// worth comparing, and offers Replace, Rename, Skip and Cancel with "apply to all"
    /// (docs/FILE_OPERATIONS.md:11-20).
    /// </summary>
    private async Task<ConflictChoice> AskAboutConflictAsync(FileConflict conflict)
    {
        CheckBox applyToAll = new() { Content = Strings.Get("Conflict_ApplyToAll") };

        StackPanel content = new()
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = Strings.Format("Conflict_Body", conflict.Source.Name),
                    TextWrapping = TextWrapping.Wrap,
                },
                await SideBySideAsync(conflict),
                applyToAll,
            },
        };

        ContentDialog dialog = new()
        {
            XamlRoot = _xamlRoot,
            Title = Strings.Get("Conflict_Title"),
            Content = content,
            PrimaryButtonText = Strings.Get("Conflict_Replace"),
            SecondaryButtonText = Strings.Get("Conflict_Rename"),
            CloseButtonText = Strings.Get("Conflict_Skip"),
            DefaultButton = ContentDialogButton.Close,
        };

        // Progress steps aside: two dialogs at once throw, and this one is the question.
        _progressDialog?.Hide();
        ContentDialogResult result = await dialog.ShowAsync();
        if (_progressDialog is { } progress)
        {
            _ = progress.ShowAsync();
        }

        ConflictResolution resolution = result switch
        {
            ContentDialogResult.Primary => ConflictResolution.Replace,
            ContentDialogResult.Secondary => ConflictResolution.Rename,
            _ => ConflictResolution.Skip,
        };

        return new ConflictChoice(resolution, applyToAll.IsChecked == true);
    }

    private async Task<Grid> SideBySideAsync(FileConflict conflict)
    {
        Grid grid = new() { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        FrameworkElement source = await SideAsync(Strings.Get("Conflict_Source"), conflict.Source);
        FrameworkElement destination = await SideAsync(Strings.Get("Conflict_Destination"), conflict.Destination);

        Grid.SetColumn(source, 0);
        Grid.SetColumn(destination, 1);
        grid.Children.Add(source);
        grid.Children.Add(destination);
        return grid;
    }

    private async Task<FrameworkElement> SideAsync(string heading, FileSystemEntry entry)
    {
        StackPanel panel = new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = heading, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
            },
        };

        if (ImageFormats.IsImage(entry.Name))
        {
            byte[]? bytes = await _thumbnails.GetThumbnailAsync(entry.FullPath, entry.Modified, 96);
            if (bytes is not null)
            {
                panel.Children.Add(new Image
                {
                    Source = await ToImageAsync(bytes),
                    Width = 96,
                    Height = 96,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                });
            }
        }

        panel.Children.Add(Caption(entry.Kind == EntryKind.Folder
            ? Strings.Get("Conflict_IsFolder")
            : EntryRow.FormatSize(entry.Size)));
        panel.Children.Add(Caption(entry.Modified.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)));

        return panel;
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Opacity = 0.7,
        Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["CaptionTextBlockStyle"],
    };

    private static async Task<BitmapImage> ToImageAsync(byte[] bytes)
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
}
