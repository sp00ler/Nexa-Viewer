namespace ViewerPrn.Domain.Viewer;

/// <summary>
/// The single conversion point between internal zero-based indexing and user-visible
/// 1-based positions (CLAUDE.md "Critical 1-based rule"). UI must never show 0.
/// </summary>
public static class DisplayPosition
{
    public static int FromIndex(int internalIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(internalIndex);
        return internalIndex + 1;
    }

    public static int ToIndex(int displayPosition)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(displayPosition, 1);
        return displayPosition - 1;
    }
}
