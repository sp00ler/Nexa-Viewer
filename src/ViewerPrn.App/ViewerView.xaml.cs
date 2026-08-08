using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.Images;
using ViewerPrn.Domain.Viewer;
using Windows.System;

namespace ViewerPrn.App;

/// <summary>
/// The Viewer (docs/VIEWER.md): one image at a time, sequential or random, with the standard
/// counter and the file's own details. The Intro Counter is Phase 11 and is not here.
/// </summary>
public sealed partial class ViewerView : UserControl, IDisposable
{
    /// <summary>
    /// Ceiling on decode size when the control has not been measured yet. Bounds the memory a
    /// very large photo can take: 3840 x 2160 x 4 bytes is about 33 MB, versus 200 MB for a
    /// 50-megapixel original.
    /// </summary>
    private const int FallbackDecodeWidth = 3840;

    private readonly IImageMetadataReader _metadata;
    private readonly IArchiveService _archives;
    private readonly ILoggingService _logger;
    private ViewerNavigator? _navigator;
    private CancellationTokenSource? _showCancellation;

    public ViewerView(IImageMetadataReader metadata, IArchiveService archives, ILoggingService logger)
    {
        _metadata = metadata;
        _archives = archives;
        _logger = logger;

        InitializeComponent();
    }

    /// <summary>Esc and Enter leave the Viewer (docs/VIEWER.md:19).</summary>
    public event EventHandler? ExitRequested;

    /// <summary>F6 minimises, and only from here (docs/REQUIREMENTS.md:13).</summary>
    public event EventHandler? MinimizeRequested;

    /// <summary>Raised whenever the shown image changes, so the shell can retitle the window.</summary>
    public event EventHandler? CurrentChanged;

    public string? CurrentPath => _navigator?.Current;

    public int CurrentIndex => _navigator?.CurrentIndex ?? -1;

    public ViewerMode Mode
    {
        get => _navigator?.Mode ?? ViewerMode.Sequential;
        set
        {
            if (_navigator is not null)
            {
                _navigator.Mode = value;
            }
        }
    }

    public async Task OpenAsync(IReadOnlyList<string> images, int startIndex)
    {
        _navigator = new ViewerNavigator(images, startIndex);
        Focus(FocusState.Programmatic);
        await ShowCurrentAsync();
    }

    public void Close()
    {
        _showCancellation?.Cancel();
        Picture.Source = null;
        _navigator = null;
    }

    public void Dispose()
    {
        _showCancellation?.Cancel();
        _showCancellation?.Dispose();
        _showCancellation = null;
    }

    // ---- Keyboard ----

    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_navigator is null)
        {
            return;
        }

        bool moved;
        switch (e.Key)
        {
            case VirtualKey.Escape:
            case VirtualKey.Enter:
                e.Handled = true;
                ExitRequested?.Invoke(this, EventArgs.Empty);
                return;

            case VirtualKey.F6:
                e.Handled = true;
                MinimizeRequested?.Invoke(this, EventArgs.Empty);
                return;

            case VirtualKey.Right:
            case VirtualKey.Down:
            case VirtualKey.PageDown:
                moved = _navigator.MoveNext();
                break;

            case VirtualKey.Left:
            case VirtualKey.Up:
            case VirtualKey.PageUp:
                moved = _navigator.MovePrevious();
                break;

            case VirtualKey.Home:
                moved = false;
                while (_navigator.MovePrevious())
                {
                    moved = true;
                }

                break;

            case VirtualKey.End:
                moved = false;
                while (_navigator.MoveNext())
                {
                    moved = true;
                }

                break;

            case VirtualKey.Space:
                moved = _navigator.MoveRandom();
                break;

            case VirtualKey.Back:
                // Random mode walks its history; sequential has none, so it steps back.
                moved = _navigator.Mode == ViewerMode.Random
                    ? _navigator.MoveBack()
                    : _navigator.MovePrevious();
                break;

            default:
                return;
        }

        e.Handled = true;

        if (moved)
        {
            await ShowCurrentAsync();
        }
        else
        {
            // Nothing moved: say which end was hit instead of wrapping silently (DECISION-0023).
            ShowEdge();
        }
    }

    // ---- Showing an image ----

    private async Task ShowCurrentAsync()
    {
        if (_navigator is null)
        {
            return;
        }

        if (_showCancellation is not null)
        {
            await _showCancellation.CancelAsync();
            _showCancellation.Dispose();
        }

        _showCancellation = new CancellationTokenSource();
        CancellationToken token = _showCancellation.Token;

        string path = _navigator.Current;
        CounterText.Text = _navigator.Counter.ToString();
        EdgeText.Text = string.Empty;
        CurrentChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            // Images inside archives are extracted first; ordinary paths pass straight through.
            string real = await _archives.MaterialiseAsync(path, token);
            ImageMetadata metadata = await _metadata.ReadAsync(real, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            LoadFailedText.Visibility = Visibility.Collapsed;
            ImageHost.Visibility = Visibility.Visible;
            Picture.Source = LoadBitmap(real, metadata);
            DetailsText.Text = DescribeDetails(real, metadata);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log(LogLevel.Warning, $"Could not show '{path}'.", exception);
            Picture.Source = null;
            ImageHost.Visibility = Visibility.Collapsed;
            LoadFailedText.Text = Strings.Get("Viewer_LoadFailed");
            LoadFailedText.Visibility = Visibility.Visible;
            DetailsText.Text = System.IO.Path.GetFileName(path);
        }
    }

    /// <summary>
    /// Decodes no larger than it will be shown. The Viewbox handles the visual fit; this caps
    /// what is actually held in memory, using the sizing rule from the domain.
    /// </summary>
    private BitmapImage LoadBitmap(string path, ImageMetadata metadata)
    {
        int hostWidth = ImageHost.ActualWidth > 0 ? (int)ImageHost.ActualWidth : FallbackDecodeWidth;
        int hostHeight = ImageHost.ActualHeight > 0 ? (int)ImageHost.ActualHeight : FallbackDecodeWidth;

        BitmapImage bitmap = new();

        if (!metadata.DisplaySize.IsEmpty)
        {
            PixelSize target = ImageScaling.FitDown(metadata.DisplaySize, new PixelSize(hostWidth, hostHeight));

            // Only the width is set: WinUI keeps the aspect ratio from it, and FitDown already
            // guarantees the matching height.
            bitmap.DecodePixelType = DecodePixelType.Logical;
            bitmap.DecodePixelWidth = target.Width;
        }

        bitmap.UriSource = new Uri(path);
        return bitmap;
    }

    private void ShowEdge()
    {
        EdgeText.Text = _navigator?.Edge switch
        {
            ViewerEdge.End => Strings.Get("Viewer_EndOfList"),
            ViewerEdge.Start => Strings.Get("Viewer_StartOfList"),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Size, dimensions, type and whatever EXIF the file carries. Empty fields are left out
    /// rather than shown blank (docs/VIEWER.md:7).
    /// </summary>
    private static string DescribeDetails(string path, ImageMetadata metadata)
    {
        FileInfo info = new(path);
        List<string> parts =
        [
            EntryRow.FormatSize(info.Length),
            $"{metadata.DisplaySize.Width}x{metadata.DisplaySize.Height}",
            Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
        ];

        if (metadata.DateTaken is { } taken)
        {
            parts.Add(taken.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
        }

        string camera = string.Join(' ', new[] { metadata.CameraMaker, metadata.CameraModel }.Where(x => x is not null));
        if (camera.Length > 0)
        {
            parts.Add(camera);
        }

        if (metadata.FocalLengthMm is { } focal)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{focal:0.#} mm"));
        }

        if (metadata.FNumber is { } aperture)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"f/{aperture:0.#}"));
        }

        if (metadata.ExposureTimeSeconds is { } exposure and > 0)
        {
            parts.Add(exposure < 1
                ? string.Create(CultureInfo.CurrentCulture, $"1/{Math.Round(1 / exposure):0} {Strings.Get("Unit_Seconds")}")
                : string.Create(CultureInfo.CurrentCulture, $"{exposure:0.#} {Strings.Get("Unit_Seconds")}"));
        }

        if (metadata.IsoSpeed is { } iso)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"ISO {iso}"));
        }

        return string.Join("  ·  ", parts);
    }
}
