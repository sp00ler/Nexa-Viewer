namespace ViewerPrn.Domain.Viewer;

/// <summary>
/// The standard Viewer counter. docs/VIEWER.md:4 specifies the format as
/// <c>TOTAL/CURRENT</c> — total first, current second — e.g. <c>469/69</c>.
/// This counter always tracks the actual physical image and is never altered by
/// the Intro Counter controls (docs/VIEWER.md:66).
/// </summary>
public readonly record struct StandardCounter(int Total, int CurrentDisplayPosition)
{
    public static StandardCounter FromIndex(int total, int internalIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(internalIndex, total);
        return new StandardCounter(total, DisplayPosition.FromIndex(internalIndex));
    }

    public override string ToString() => $"{Total}/{CurrentDisplayPosition}";
}
