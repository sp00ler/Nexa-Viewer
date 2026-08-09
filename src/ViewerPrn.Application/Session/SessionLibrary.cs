namespace ViewerPrn.Application.Session;

/// <summary>One named set of tabs the user saved by hand.</summary>
public sealed record SavedSession
{
    public required string Name { get; init; }

    public SessionState State { get; init; } = SessionState.Empty;
}

/// <summary>
/// Every saved state, in the order they were saved. Kept beside the automatic session file and
/// in the same shape, so a state is a session with a name on it (DECISION-0036).
/// </summary>
public sealed record SessionLibrary
{
    public IReadOnlyList<SavedSession> Sessions { get; init; } = [];

    public static SessionLibrary Empty { get; } = new();

    /// <summary>
    /// Drops blank names, sanitises each state, and keeps the last of any duplicate name — a
    /// hand-edited file must not put two identical entries in the menu.
    /// </summary>
    public SessionLibrary Sanitised()
    {
        Dictionary<string, SavedSession> byName = new(StringComparer.OrdinalIgnoreCase);

        foreach (SavedSession session in Sessions)
        {
            if (string.IsNullOrWhiteSpace(session.Name))
            {
                continue;
            }

            byName[session.Name.Trim()] = session with
            {
                Name = session.Name.Trim(),
                State = session.State.Sanitised(),
            };
        }

        return this with { Sessions = [.. byName.Values] };
    }

    /// <summary>Adds a state, or replaces the one already under that name.</summary>
    public SessionLibrary With(string name, SessionState state) =>
        (this with { Sessions = [.. Sessions, new SavedSession { Name = name, State = state }] }).Sanitised();

    public SessionLibrary Without(string name) => this with
    {
        Sessions = [.. Sessions.Where(session => !string.Equals(session.Name, name, StringComparison.OrdinalIgnoreCase))],
    };

    public SavedSession? Find(string name) =>
        Sessions.FirstOrDefault(session => string.Equals(session.Name, name, StringComparison.OrdinalIgnoreCase));
}
