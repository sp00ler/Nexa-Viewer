namespace ViewerPrn.Domain.Viewer;

/// <summary>
/// Presentation severity of the reset count shown beside the helper counter
/// (docs/VIEWER.md:76-79): 1-3 normal, 4 orange, 5 red followed by <c>!</c>.
/// </summary>
public enum ResetSeverity
{
    None = 0,
    Normal = 1,
    Orange = 2,
    RedWithExclamation = 3,
}
