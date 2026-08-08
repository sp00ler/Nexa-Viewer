namespace ViewerPrn.Domain.Viewer;

/// <summary>
/// The helper counter rendered as <c>X(Y)/Z</c> (docs/VIEWER.md:21-27).
/// <para>
/// It is a stateful accumulator over viewed images, not a pure function of the physical
/// position: the cycle controls mutate <see cref="CyclePosition"/> while the physical
/// image count keeps advancing independently. The standard <see cref="StandardCounter"/>
/// is never affected by anything in this type (docs/VIEWER.md:66).
/// </para>
/// </summary>
public sealed class IntroCounter
{
    /// <summary>
    /// The warning starts this many cycle positions before the end of the cycle,
    /// measured in cycle positions, not physical images (docs/VIEWER.md:62).
    /// </summary>
    public const int WarningLead = 15;

    private const string SpecReference = "docs/VIEWER.md";

    public IntroCounter(CycleDefinition definition)
    {
        Definition = definition;
    }

    public static IntroCounter ForGallery(int totalImages) => new(CycleTable.Resolve(totalImages));

    public CycleDefinition Definition { get; }

    /// <summary>Physical images viewed so far in this gallery.</summary>
    public int ViewedCount { get; private set; }

    /// <summary>X in <c>X(Y)/Z</c>. Zero while the introductory block is still running.</summary>
    public int CyclePosition { get; private set; }

    public int ResetCount { get; private set; }

    /// <summary>
    /// True while the viewer is inside the introductory block. Introductory images are
    /// physically viewed but do not count toward the cycle position (docs/VIEWER.md:27).
    /// </summary>
    public bool IsIntroductory => CyclePosition == 0;

    /// <summary>
    /// The cycle position does not wrap at the end of the cycle: it keeps growing past
    /// <see cref="CycleDefinition.CycleLength"/>. Confirmed by the user for v1 — see DECISION-0002.
    /// </summary>
    public bool IsWarningActive => !IsIntroductory && CyclePosition >= Definition.CycleLength - WarningLead;

    public ResetSeverity ResetSeverity => ResetCount switch
    {
        0 => ResetSeverity.None,
        <= 3 => ResetSeverity.Normal,
        4 => ResetSeverity.Orange,
        5 => ResetSeverity.RedWithExclamation,

        // docs/VIEWER.md:77-79 defines 1-3, 4 and 5 only, and never says whether or when
        // the reset count clears. The sixth reset is undefined.
        _ => throw new BlockedRequirementException(
            "Reset-count presentation beyond 5 resets, and when the reset count clears",
            $"{SpecReference}:76-79"),
    };

    /// <summary>Records that one more physical image has been viewed.</summary>
    public void OnImageViewed()
    {
        ViewedCount++;
        if (ViewedCount > Definition.IntroCount)
        {
            CyclePosition++;
        }
    }

    /// <summary>Reset Cycle is enabled only at cycle positions 1-10 inclusive (docs/VIEWER.md:81).</summary>
    public bool CanReset => !IsIntroductory && CyclePosition <= 10;

    /// <summary>
    /// Resets the cycle to position 1 without restarting or recounting the introductory
    /// block (docs/VIEWER.md:69).
    /// </summary>
    public void Reset()
    {
        ThrowIfDisabled(CanReset, nameof(Reset));
        CyclePosition = 1;
        ResetCount++;
    }

    /// <summary>Minus 10 is enabled only after position 10 (docs/VIEWER.md:87).</summary>
    public bool CanMinus10 => !IsIntroductory && CyclePosition > 10;

    public void Minus10()
    {
        ThrowIfDisabled(CanMinus10, nameof(Minus10));
        CyclePosition = Math.Max(1, CyclePosition - 10);
    }

    /// <summary>
    /// Minus 1 availability depends on gallery size (docs/VIEWER.md:91-92):
    /// totals up to 299 enable it from position 11, totals from 300 enable it from the
    /// warning threshold <c>cycleLength - 15</c>.
    /// </summary>
    public bool CanMinus1 => !IsIntroductory
        && (Definition.TotalImages <= 299
            ? CyclePosition > 10
            : CyclePosition >= Definition.CycleLength - WarningLead);

    public void Minus1()
    {
        ThrowIfDisabled(CanMinus1, nameof(Minus1));
        CyclePosition = Math.Max(1, CyclePosition - 1);
    }

    /// <summary>
    /// Stop / Do Not Count. docs/VIEWER.md:98-104 does not say whether pressing it skips
    /// exactly one image or turns counting off until pressed again, and the two readings
    /// produce different displays from the second image onward.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Becomes instance state once the Stop semantics are clarified.")]
    public void Stop() => throw new BlockedRequirementException(
        "Stop / Do Not Count: one-shot skip of the next image vs. a toggled mode",
        $"{SpecReference}:98-104");

    /// <summary>Renders <c>X(Y)/Z</c>.</summary>
    public string Format()
    {
        if (IsIntroductory)
        {
            // docs/VIEWER.md:32 calls physical images 1..Y the "introductory state" but
            // never gives the string shown during it.
            throw new BlockedRequirementException(
                "Helper-counter display during the introductory block",
                $"{SpecReference}:31-32,40");
        }

        return $"{CyclePosition}({Definition.IntroCount})/{Definition.CycleLength}";
    }

    private static void ThrowIfDisabled(bool enabled, string control)
    {
        if (!enabled)
        {
            throw new InvalidOperationException($"{control} is disabled in the current cycle state.");
        }
    }
}
