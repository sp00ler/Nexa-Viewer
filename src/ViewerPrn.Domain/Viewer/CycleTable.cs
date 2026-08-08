namespace ViewerPrn.Domain.Viewer;

/// <summary>
/// Maps a gallery's total image count to its intro/cycle parameters.
/// Source of truth: docs/VIEWER.md "Established ranges" (lines 44-59).
/// Ranges the specification marks BLOCKED throw instead of guessing.
/// </summary>
public static class CycleTable
{
    private const string SpecReference = "docs/VIEWER.md:44-59,116";

    public static CycleDefinition Resolve(int totalImages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalImages, 1);

        return totalImages switch
        {
            // 1-50: intro 5, cycle is a "special `-` state" whose display format is
            // never specified. Undefined -> BLOCKED.
            <= 50 => throw new BlockedRequirementException(
                "Intro Counter behaviour for totals 1-50 (\"special `-` state\")",
                SpecReference),

            <= 77 => new CycleDefinition(totalImages, RoundUpDivide(totalImages, 10), 5),
            <= 127 => new CycleDefinition(totalImages, 10, 5),
            <= 177 => new CycleDefinition(totalImages, 15, 7),
            <= 227 => new CycleDefinition(totalImages, 20, 10),
            <= 299 => new CycleDefinition(totalImages, 10, 30),

            // 300-500: docs/VIEWER.md:116 forbids inventing this range. The formula below
            // is scoped to "N > 500" (docs/VIEWER.md:57), so it must not be extended here.
            <= 500 => throw new BlockedRequirementException(
                "Intro Counter behaviour for totals 300-500",
                SpecReference),

            <= 799 => new CycleDefinition(totalImages, 15, CycleLengthForLargeGallery(totalImages)),
            <= 1199 => new CycleDefinition(totalImages, 20, CycleLengthForLargeGallery(totalImages)),

            _ => throw new BlockedRequirementException(
                "Intro Counter behaviour for totals above 1199",
                SpecReference),
        };
    }

    /// <summary>
    /// Cycle length for galleries above 500 images: N/10 rounded up to the nearest ten,
    /// i.e. <c>ceil(N/100)*10</c>. Confirmed by the user against the specification's own
    /// examples (505 -> 60, 645 -> 70, 951 -> 100). The literal text <c>ceil(N/10)*10</c>
    /// in docs/VIEWER.md contradicts every one of those examples — see DECISION-0001.
    /// </summary>
    private static int CycleLengthForLargeGallery(int totalImages) => RoundUpDivide(totalImages, 100) * 10;

    private static int RoundUpDivide(int value, int divisor) => (value + divisor - 1) / divisor;
}
