namespace ViewerPrn.Domain.Viewer;

public enum ViewerMode
{
    Sequential = 0,
    Random = 1,
}

/// <summary>Which end of the list the last move ran into, so the UI can say so.</summary>
public enum ViewerEdge
{
    None = 0,
    Start = 1,
    End = 2,
}

/// <summary>
/// Position and movement inside one gallery (docs/VIEWER.md).
/// <para>
/// Sequential navigation stops at both ends and reports which one it hit — never a silent
/// wrap (DECISION-0023). Random navigation is a history, not repeated random generation:
/// Backspace walks back through what was actually seen.
/// </para>
/// </summary>
public sealed class ViewerNavigator
{
    private readonly IReadOnlyList<string> _images;
    private readonly Func<int, int> _pickIndex;
    private readonly List<int> _history = [];
    private readonly List<int> _forward = [];
    private readonly HashSet<int> _seen = [];
    private int _current;

    /// <param name="pickIndex">
    /// Chooses the next random index given the count. Injected so the history behaviour can be
    /// tested without depending on what a random number generator happens to produce.
    /// </param>
    public ViewerNavigator(IReadOnlyList<string> images, int startIndex, Func<int, int>? pickIndex = null)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("A gallery must contain at least one image.", nameof(images));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startIndex, images.Count);

        _images = images;
        _pickIndex = pickIndex ?? (count => Random.Shared.Next(count));
        CurrentIndex = startIndex;
    }

    public ViewerMode Mode { get; set; } = ViewerMode.Sequential;

    /// <summary>Zero-based. Everything the user sees goes through <see cref="DisplayPosition"/>.</summary>
    public int CurrentIndex
    {
        get => _current;
        private set
        {
            _current = value;
            _seen.Add(value);
        }
    }

    /// <summary>
    /// True once every image in the gallery has been on screen. Random viewing stops there: a
    /// further random draw could only repeat something already seen (DECISION-0042).
    /// </summary>
    public bool GalleryExhausted => _seen.Count >= Total;

    public int DisplayPosition => Domain.Viewer.DisplayPosition.FromIndex(CurrentIndex);

    public int Total => _images.Count;

    public string Current => _images[CurrentIndex];

    /// <summary>Standard counter, formatted as docs/VIEWER.md:4 specifies: total first.</summary>
    public StandardCounter Counter => new(Total, DisplayPosition);

    /// <summary>Set by the last move that could not go anywhere; cleared by any successful move.</summary>
    public ViewerEdge Edge { get; private set; } = ViewerEdge.None;

    public bool CanMoveNext => CurrentIndex < Total - 1;

    public bool CanMovePrevious => CurrentIndex > 0;

    public bool CanMoveBack => _history.Count > 0;

    /// <summary>True when going forward would retrace rather than draw a new random image.</summary>
    public bool CanRetraceForward => _forward.Count > 0;

    public bool MoveNext()
    {
        if (!CanMoveNext)
        {
            Edge = ViewerEdge.End;
            return false;
        }

        CurrentIndex++;
        Edge = ViewerEdge.None;
        return true;
    }

    public bool MovePrevious()
    {
        if (!CanMovePrevious)
        {
            Edge = ViewerEdge.Start;
            return false;
        }

        CurrentIndex--;
        Edge = ViewerEdge.None;
        return true;
    }

    /// <summary>
    /// Moves forward in random mode. After going back, this retraces what was actually seen
    /// rather than drawing a new image — browser-like, as docs/VIEWER.md requires. Only forward
    /// past the end of the history draws a new one.
    /// </summary>
    public bool MoveRandom()
    {
        if (Total == 1)
        {
            Edge = ViewerEdge.End;
            return false;
        }

        if (_forward.Count > 0)
        {
            _history.Add(CurrentIndex);
            CurrentIndex = _forward[^1];
            _forward.RemoveAt(_forward.Count - 1);
            Edge = ViewerEdge.None;
            return true;
        }

        int next = CurrentIndex;
        for (int attempt = 0; attempt < 8 && next == CurrentIndex; attempt++)
        {
            next = _pickIndex(Total);
        }

        // ponytail: after eight tries take whatever came out. Landing on the same image is a
        // cosmetic annoyance, not a bug, and a rejection loop on a two-image gallery is worse.
        _history.Add(CurrentIndex);
        CurrentIndex = next;
        Edge = ViewerEdge.None;
        return true;
    }

    /// <summary>Walks back through the images actually visited in random mode.</summary>
    public bool MoveBack()
    {
        if (_history.Count == 0)
        {
            Edge = ViewerEdge.Start;
            return false;
        }

        // Remembered so that going forward again lands on the same image.
        _forward.Add(CurrentIndex);
        CurrentIndex = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        Edge = ViewerEdge.None;
        return true;
    }
}
