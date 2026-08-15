namespace ViewerPrn.Domain.Viewer;

/// <summary>
/// The helper counter rendered as <c>X(Y)/Z</c> (docs/VIEWER.md).
/// <para>
/// It is a stateful accumulator over images *viewed*, not a function of the position in the
/// gallery: in random mode the two diverge, and the standard <see cref="StandardCounter"/> is the
/// one that shows the position. Nothing here ever affects that counter.
/// </para>
/// <para>
/// One counter per tab. It is discarded when the Viewer is closed, which is also when the reset
/// count clears.
/// </para>
/// </summary>
public sealed class IntroCounter
{
    /// <summary>
    /// The warning starts this many cycle positions before the end of the cycle, measured in
    /// cycle positions, not images seen (docs/VIEWER.md).
    /// </summary>
    public const int WarningLead = 15;

    /// <summary>Reset count stops changing appearance here; a sixth reset looks like the fifth.</summary>
    public const int MaxDistinctResetCount = 5;

    public IntroCounter(CycleDefinition definition)
    {
        Definition = definition;
    }

    public static IntroCounter ForGallery(int totalImages) => new(CycleTable.Resolve(totalImages));

    public CycleDefinition Definition { get; }

    /// <summary>Images viewed so far in this Viewer session.</summary>
    public int ViewedCount { get; private set; }

    /// <summary>X in <c>X(Y)/Z</c>. Zero while the introductory block is still running.</summary>
    public int CyclePosition { get; private set; }

    public int ResetCount { get; private set; }

    /// <summary>
    /// True while the viewer is inside the introductory block. Introductory images are viewed but
    /// do not count toward the cycle position.
    /// </summary>
    public bool IsIntroductory => CyclePosition == 0;

    /// <summary>Galleries of 1–50 images have no cycle; the counter shows <c>-(5)/-</c>.</summary>
    public bool HasCycle => Definition.CycleLength != CycleTable.NoCycle;

    /// <summary>
    /// True when the next advance will be swallowed. Stop is a flag on the image in front of the
    /// user, not a queue: pressing it again while it is already set changes nothing.
    /// </summary>
    public bool SkipNext { get; private set; }

    /// <summary>
    /// The cycle position does not wrap at the end of the cycle; it keeps growing
    /// (DECISION-0002), so once the warning starts it stays on.
    /// </summary>
    public bool IsWarningActive =>
        HasCycle && !IsIntroductory && CyclePosition >= Definition.CycleLength - WarningLead;

    public ResetSeverity ResetSeverity => ResetCount switch
    {
        0 => ResetSeverity.None,
        <= 3 => ResetSeverity.Normal,
        4 => ResetSeverity.Orange,

        // Fifth and beyond look the same: red with an exclamation mark.
        _ => ResetSeverity.RedWithExclamation,
    };

    /// <summary>Records that one more image has been viewed.</summary>
    public void OnImageViewed()
    {
        ViewedCount++;

        if (!HasCycle)
        {
            return;
        }

        // Stop was pressed: this advance is swallowed and the counter stands still.
        if (SkipNext)
        {
            SkipNext = false;
            return;
        }

        if (ViewedCount > Definition.IntroCount)
        {
            CyclePosition++;
        }
    }

    /// <summary>
    /// Records a step backwards through images already seen. The position follows the path taken
    /// rather than jumping, and stops at the introductory block.
    /// </summary>
    public void OnImageUnviewed()
    {
        if (ViewedCount > 0)
        {
            ViewedCount--;
        }

        if (HasCycle && CyclePosition > 0)
        {
            CyclePosition--;
        }
    }

    /// <summary>Reset Cycle is enabled only at cycle positions 1–10 inclusive.</summary>
    public bool CanReset => HasCycle && !IsIntroductory && CyclePosition <= 10;

    /// <summary>
    /// Resets the cycle to position 1 without restarting or recounting the intro block.
    /// <para>
    /// <paramref name="counted"/> false is the plain reset: same position change, but the reset
    /// count beside the counter does not move, so it stays a record of the resets worth counting
    /// (user, 2026-08-09). Two buttons, one method.
    /// </para>
    /// </summary>
    public void Reset(bool counted)
    {
        ThrowIfDisabled(CanReset, nameof(Reset));
        CyclePosition = 1;

        // The user who pressed reset is still looking at the current image; the next advance
        // must land on 1(x)/y, not 2(x)/y. Same one-advance hold as Stop, same flag, so several
        // of these on one image still cost a single advance (specs/viewer-counter-controls.md).
        SkipNext = true;

        if (counted)
        {
            ResetCount++;
        }
    }

    /// <summary>Minus 10 is enabled only after position 10.</summary>
    public bool CanMinus10 => HasCycle && !IsIntroductory && CyclePosition > 10;

    public void Minus10()
    {
        ThrowIfDisabled(CanMinus10, nameof(Minus10));
        CyclePosition = Math.Max(1, CyclePosition - 10);
        SkipNext = true; // Same one-advance hold as Reset.
    }

    /// <summary>
    /// Minus 1 availability depends on gallery size: totals up to 299 enable it from position 11,
    /// totals from 300 from the warning threshold <c>cycleLength - 15</c>.
    /// </summary>
    public bool CanMinus1 => HasCycle
        && !IsIntroductory
        && (Definition.TotalImages <= 299
            ? CyclePosition > 10
            : CyclePosition >= Definition.CycleLength - WarningLead);

    public void Minus1()
    {
        ThrowIfDisabled(CanMinus1, nameof(Minus1));
        CyclePosition = Math.Max(1, CyclePosition - 1);
        SkipNext = true; // Same one-advance hold as Reset.
    }

    /// <summary>
    /// The number currently shown in the brackets of <see cref="Format"/>: the cycle length for
    /// small galleries, the intro count for large ones and for galleries with no cycle. The
    /// tally buttons' thresholds are read off this (specs/viewer-counter-controls.md, R7).
    /// </summary>
    public int BracketValue => HasCycle && Definition.TotalImages < CycleTable.SmallGalleryLimit
        ? Definition.CycleLength
        : Definition.IntroCount;

    /// <summary>
    /// Stop / Do Not Count. Always available. Marks the current image as not counted, so the next
    /// advance is swallowed. Idempotent: a double-click, or a mouse that sends one click twice,
    /// must not cost three images (user, 2026-08-09). The standard counter is unaffected.
    /// </summary>
    public void Stop() => SkipNext = true;

/// <summary>
    /// The counter runs in two phases (docs/VIEWER.md, resolved rules).
    /// <para>
    /// While the introductory block runs it counts that block: <c>6/15</c>. The step after
    /// <c>15/15</c> starts the cycle, and it counts that instead: <c>1(15)/80</c>.
    /// </para>
    /// <para>
    /// Galleries below <see cref="CycleTable.SmallGalleryLimit"/> are written the other way
    /// round: the first phase omits the cycle, and the second phase puts the cycle in the
    /// brackets — <c>1/15</c>, then <c>1(7)/15</c>. Confirmed by the user on 2026-08-09.
    /// </para>
    /// </summary>
    public string Format()
    {
        if (!HasCycle)
        {
            return $"-({Definition.IntroCount})/-";
        }

        bool small = Definition.TotalImages < CycleTable.SmallGalleryLimit;

        if (IsIntroductory)
        {
            return small
                ? $"{ViewedCount}/{Definition.IntroCount}"
                : $"{ViewedCount}/{Definition.IntroCount}({Definition.CycleLength})";
        }

        return small
            ? $"{CyclePosition}({Definition.CycleLength})/{Definition.IntroCount}"
            : $"{CyclePosition}({Definition.IntroCount})/{Definition.CycleLength}";
    }

    private static void ThrowIfDisabled(bool enabled, string control)
    {
        if (!enabled)
        {
            throw new InvalidOperationException($"{control} is disabled in the current cycle state.");
        }
    }
}
