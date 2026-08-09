namespace ViewerPrn.Domain.Viewer;

/// <summary>
/// The standard Viewer counter, written <c>CURRENT/TOTAL</c> — the sixth image of 155 reads
/// <c>6/155</c>. The specification originally said total first; the user corrected that on
/// 2026-08-08. This counter always tracks the position in the gallery and is never altered by
/// the Intro Counter controls.
/// </summary>
public readonly record struct StandardCounter(int Total, int CurrentDisplayPosition)
{
    public static StandardCounter FromIndex(int total, int internalIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(internalIndex, total);
        return new StandardCounter(total, DisplayPosition.FromIndex(internalIndex));
    }

    public override string ToString() => $"{CurrentDisplayPosition}/{Total}";
}
