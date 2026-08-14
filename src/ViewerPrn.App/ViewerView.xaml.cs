using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    /// Longest edge a picture is decoded to. Bounds what a very large photo costs in memory —
    /// 3840 across is about 33 MB against 200 MB for a 50-megapixel original — while staying
    /// larger than any display it will be shown on, so the fitting is the Viewbox's job.
    /// </summary>
    private const int MaxDecodeEdge = 3840;

    private readonly IImageMetadataReader _metadata;
    private readonly IArchiveService _archives;
    private readonly IViewStatisticsService _statistics;
    private long _sessionId;
    private IntroCounter? _cycle;
    private readonly ILoggingService _logger;
    private ViewerNavigator? _navigator;
    private CancellationTokenSource? _showCancellation;

    public ViewerView(
        IImageMetadataReader metadata,
        IArchiveService archives,
        IViewStatisticsService statistics,
        ILoggingService logger)
    {
        _metadata = metadata;
        _archives = archives;
        _statistics = statistics;
        _logger = logger;

        InitializeComponent();

        ResetCycleButton.Content = Strings.Get("Cycle_ResetCounted");
        PlainResetCycleButton.Content = Strings.Get("Cycle_Reset");
        Minus10Button.Content = Strings.Get("Cycle_Minus10");
        Minus1Button.Content = Strings.Get("Cycle_Minus1");
        StopButton.Content = Strings.Get("Cycle_Stop");
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

    public async Task OpenAsync(IReadOnlyList<string> images, int startIndex, ViewerMode mode)
    {
        _navigator = new ViewerNavigator(images, startIndex) { Mode = mode };

        // A fresh counter per visit: it belongs to this Viewer session and dies with it, which
        // is also when the reset count clears (docs/VIEWER.md, resolved rules).
        _cycle = images.Count <= CycleTable.MaxSupportedTotal ? IntroCounter.ForGallery(images.Count) : null;

        // One session per visit to a gallery, keyed by the folder the images came from.
        string? source = System.IO.Path.GetDirectoryName(images[startIndex]);
        _sessionId = source is null ? 0 : await _statistics.StartSessionAsync(source);

        _cycle?.OnImageViewed();
        Focus(FocusState.Programmatic);
        await ShowCurrentAsync();
    }

    public void Close()
    {
        _showCancellation?.Cancel();
        Picture.Source = null;
        _navigator = null;
        _cycle = null;

        if (_sessionId != 0)
        {
            // Ends the session and writes everything buffered for it. Not awaited: leaving the
            // Viewer must not wait for a disk write.
            long ending = _sessionId;
            _sessionId = 0;
            _ = _statistics.EndSessionAsync(ending);
        }
    }

    public void Dispose()
    {
        _showCancellation?.Cancel();
        _showCancellation?.Dispose();
        _showCancellation = null;
    }

    // ---- Keyboard ----

    /// <summary>
    /// Navigation keys, independent of who holds focus. The shell is the only caller: it routes
    /// every key here while the Viewer is up, because opening a menu or clicking one of the cycle
    /// buttons takes focus away from this control and it never comes back on its own.
    /// <para>
    /// This control must not subscribe to KeyDown itself. It did, and the two paths both ran:
    /// the handler is async, so it yields before setting Handled, the event carries on bubbling
    /// to the shell, and every key press moved and counted twice — the helper counter went
    /// 1/15, 3/15, 5/15.
    /// </para>
    /// </summary>
    public async Task<bool> HandleKeyAsync(VirtualKey key)
    {
        if (_navigator is null)
        {
            return false;
        }

        bool moved;
        bool wentBackwards = key is VirtualKey.Left or VirtualKey.Up or VirtualKey.PageUp or VirtualKey.Home or VirtualKey.Back;
        switch (key)
        {
            case VirtualKey.Escape:
            case VirtualKey.Enter:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                return true;

            case VirtualKey.F6:
                MinimizeRequested?.Invoke(this, EventArgs.Empty);
                return true;

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
                // Random viewing ends when the gallery has been seen through: Space then does
                // nothing at all, silently, and only the arrows, Backspace and Home/End still
                // move (DECISION-0042). The counters are untouched, because nothing moved.
                if (_navigator.Mode == ViewerMode.Random && _navigator.GalleryExhausted)
                {
                    return true;
                }

                // Space is "next". Only in random mode does that mean a random image; in
                // sequential mode it is the next one, and it stops at the end like the arrows.
                moved = _navigator.Mode == ViewerMode.Random
                    ? _navigator.MoveRandom()
                    : _navigator.MoveNext();
                break;

            case VirtualKey.Back:
                // Random mode walks its history; sequential has none, so it steps back.
                moved = _navigator.Mode == ViewerMode.Random
                    ? _navigator.MoveBack()
                    : _navigator.MovePrevious();
                break;

            default:
                return false;
        }

        if (moved)
        {
            // Backward steps walk the counter back along the path taken; forward steps advance it.
            if (wentBackwards)
            {
                _cycle?.OnImageUnviewed();
            }
            else
            {
                _cycle?.OnImageViewed();
            }

            await ShowCurrentAsync();
        }
        else
        {
            // Nothing moved: say which end was hit instead of wrapping silently (DECISION-0023).
            ShowEdge();
        }

        return true;
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
        UpdateCycleCounter();
        CurrentChanged?.Invoke(this, EventArgs.Empty);

        // Buffered in memory; the database is not touched per keystroke.
        _statistics.RecordImageView(_sessionId, path, _navigator.DisplayPosition);

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
        // Paging faster than the disk cancels this load; a newer one already owns the screen.
        // Swallowed here because there is nobody above to swallow it: the callers are async void
        // key handlers, so it reached the unhandled handler and wrote a crash log per page turn.
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
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
    private static BitmapImage LoadBitmap(string path, ImageMetadata metadata)
    {
        BitmapImage bitmap = new();
        PixelSize size = metadata.DisplaySize;

        // The cap is about memory, not about layout. Decoding to the size of the window was the
        // earlier mistake: the Viewbox will not enlarge what it is given, so a picture decoded to
        // the viewport of the moment ended up drawn smaller than the window. Give the Viewbox a
        // bitmap at least as large as the area it has, and let it do the fitting.
        if (!size.IsEmpty && Math.Max(size.Width, size.Height) > MaxDecodeEdge)
        {
            bitmap.DecodePixelType = DecodePixelType.Physical;

            // Only one dimension is set; WinUI keeps the aspect ratio from it.
            if (size.Width >= size.Height)
            {
                bitmap.DecodePixelWidth = MaxDecodeEdge;
            }
            else
            {
                bitmap.DecodePixelHeight = MaxDecodeEdge;
            }
        }

        bitmap.UriSource = new Uri(path);
        return bitmap;
    }

    // ---- The helper counter and its four controls ----

    private void UpdateCycleCounter()
    {
        if (_cycle is null)
        {
            CycleText.Text = string.Empty;
            ResetCountText.Text = string.Empty;
            ResetCycleButton.IsEnabled = PlainResetCycleButton.IsEnabled = false;
            Minus10Button.IsEnabled = Minus1Button.IsEnabled = false;
            return;
        }

        CycleText.Text = _cycle.Format();
        CycleText.Foreground = _cycle.IsWarningActive
            ? new SolidColorBrush(Microsoft.UI.Colors.Orange)
            : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"];

        ResetCountText.Text = _cycle.ResetCount == 0
            ? string.Empty
            : _cycle.ResetSeverity == ResetSeverity.RedWithExclamation
                ? $"{_cycle.ResetCount}!"
                : _cycle.ResetCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

        ResetCountText.Foreground = _cycle.ResetSeverity switch
        {
            ResetSeverity.Orange => new SolidColorBrush(Microsoft.UI.Colors.Orange),
            ResetSeverity.RedWithExclamation => new SolidColorBrush(Microsoft.UI.Colors.Red),
            _ => (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"],
        };

        ResetCycleButton.IsEnabled = PlainResetCycleButton.IsEnabled = _cycle.CanReset;
        Minus10Button.IsEnabled = _cycle.CanMinus10;
        Minus1Button.IsEnabled = _cycle.CanMinus1;
    }

    private void OnResetCycle(object sender, RoutedEventArgs e) => ResetCycle(counted: true);

    /// <summary>The same reset, without adding to the reset count beside the counter.</summary>
    private void OnPlainResetCycle(object sender, RoutedEventArgs e) => ResetCycle(counted: false);

    private void ResetCycle(bool counted)
    {
        Focus(FocusState.Programmatic);
        if (_cycle?.CanReset == true)
        {
            _cycle.Reset(counted);
            UpdateCycleCounter();
        }
    }

    private void OnMinus10(object sender, RoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        if (_cycle?.CanMinus10 == true)
        {
            _cycle.Minus10();
            UpdateCycleCounter();
        }
    }

    private void OnMinus1(object sender, RoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        if (_cycle?.CanMinus1 == true)
        {
            _cycle.Minus1();
            UpdateCycleCounter();
        }
    }

    /// <summary>Always available. Each press holds the counter still for one more image.</summary>
    private void OnStopCounting(object sender, RoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        _cycle?.Stop();
        UpdateCycleCounter();
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
