namespace ViewerPrn.Domain.Viewer;

/// <summary>
/// A manual click counter beside the Viewer's cycle controls — the "cum" and "ANALize" buttons
/// (specs/viewer-counter-controls.md). Counts its own presses and nothing else.
/// <para>
/// The display is 1-based by construction: zero renders as an empty string, never as "0", per
/// the project-wide rule that nothing user-visible starts at zero (CLAUDE.md).
/// </para>
/// <para>
/// One counter per Viewer session per button; discarded when the Viewer is left for the
/// Explorer, together with the reset count.
/// </para>
/// </summary>
public sealed class TallyCounter
{
    public int Count { get; private set; }

    /// <summary>
    /// The red-with-"!" state. Once lit it stays lit for the life of the counter, even if the
    /// gallery changes and the new bracket value carries no threshold.
    /// </summary>
    public bool IsHot { get; private set; }

    /// <summary>
    /// Counts one press. Thresholds exist only for bracket values of 5, 7 and 10 — reaching one
    /// lights the counter red with "!". Any other bracket value counts green without limit.
    /// </summary>
    public void Click(int bracketValue)
    {
        Count++;

        if (bracketValue is 5 or 7 or 10 && Count >= bracketValue)
        {
            IsHot = true;
        }
    }

    /// <summary>
    /// The special case: the fifth counted reset lights "5!" here. A count already at five or
    /// above is never lowered by this — only the format turns red.
    /// </summary>
    public void Ignite()
    {
        Count = Math.Max(Count, 5);
        IsHot = true;
    }

    public string Format() => Count == 0 ? "" : IsHot ? $"{Count}!" : $"{Count}";
}
