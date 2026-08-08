namespace ViewerPrn.Domain.Navigation;

/// <summary>
/// Back and forward for one tab, with the same rules a browser uses: going somewhere new from
/// the middle of the history discards what was ahead.
/// </summary>
public sealed class NavigationHistory
{
    private readonly List<string> _entries = [];
    private int _position = -1;

    public string? Current => _position >= 0 ? _entries[_position] : null;

    public bool CanGoBack => _position > 0;

    public bool CanGoForward => _position >= 0 && _position < _entries.Count - 1;

    /// <summary>Records a new location. Navigating to where we already are changes nothing.</summary>
    public void Visit(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (string.Equals(Current, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Anything ahead of the current position is no longer reachable.
        if (CanGoForward)
        {
            _entries.RemoveRange(_position + 1, _entries.Count - _position - 1);
        }

        _entries.Add(path);
        _position = _entries.Count - 1;
    }

    public string? GoBack() => CanGoBack ? _entries[--_position] : null;

    public string? GoForward() => CanGoForward ? _entries[++_position] : null;
}
