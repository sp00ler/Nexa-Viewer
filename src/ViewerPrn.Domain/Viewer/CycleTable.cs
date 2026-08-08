namespace ViewerPrn.Domain.Viewer;

/// <summary>
/// Maps a gallery's total image count to its intro/cycle parameters.
/// Source of truth: docs/VIEWER.md, "Resolved rules".
/// </summary>
public static class CycleTable
{
    /// <summary>
    /// Galleries of 1–50 have no cycle at all; the counter shows <c>-(5)/-</c>.
    /// </summary>
    public const int NoCycle = 0;

    /// <summary>The rule is implemented this far. Beyond it the input is not a photo gallery.</summary>
    public const int MaxSupportedTotal = 9999;

    /// <summary>Intro grows by this much per band above 800.</summary>
    private const int IntroStepPerBand = 5;

    /// <summary>Bands above 800 are this wide: 800–1199, 1200–1599, and so on.</summary>
    private const int BandWidth = 400;

    public static CycleDefinition Resolve(int totalImages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalImages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(totalImages, MaxSupportedTotal);

        return totalImages switch
        {
            <= 50 => new CycleDefinition(totalImages, 5, NoCycle),
            <= 77 => new CycleDefinition(totalImages, RoundUpDivide(totalImages, 10), 5),
            <= 127 => new CycleDefinition(totalImages, 10, 5),
            <= 177 => new CycleDefinition(totalImages, 15, 7),
            <= 227 => new CycleDefinition(totalImages, 20, 10),
            <= 299 => new CycleDefinition(totalImages, 10, 30),
            _ => new CycleDefinition(totalImages, IntroFor(totalImages), CycleLengthFor(totalImages)),
        };
    }

    /// <summary>
    /// N/10 rounded up to the nearest ten, i.e. <c>ceil(N/100)*10</c>. Confirmed against the
    /// specification's own examples: 469 -> 50, 505 -> 60, 645 -> 70, 951 -> 100, 1199 -> 120.
    /// Applies to every total from 300 upward (DECISION-0001).
    /// </summary>
    private static int CycleLengthFor(int totalImages) => RoundUpDivide(totalImages, 100) * 10;

    /// <summary>
    /// 15 for 300–799, then 20 for 800–1199 and five more for each further band of 400.
    /// <para>
    /// The bands exist only to set the intro — the cycle is a formula and needs none — so the
    /// step seen from 300 to 800 is continued upward. See docs/VIEWER.md, which flags this as
    /// the one assumption in the resolved rules.
    /// </para>
    /// </summary>
    private static int IntroFor(int totalImages) => totalImages < 800
        ? 15
        : 20 + (IntroStepPerBand * ((totalImages - 800) / BandWidth));

    private static int RoundUpDivide(int value, int divisor) => (value + divisor - 1) / divisor;
}
