namespace ViewerPrn.Application.Session;

/// <summary>
/// The text-file form of a set of tabs: one path per line, the way Explorer shows it. Written by
/// the Sessions menu's export and read back by its import, so the two cannot drift apart.
/// </summary>
public static class SessionPathsText
{
    public static IReadOnlyList<string> ToLines(SessionState state) =>
        [.. state.Tabs.Select(tab => tab.Path)];

    /// <summary>Blank lines and lines starting with # are skipped; surrounding quotes are dropped.</summary>
    public static IReadOnlyList<string> ParseLines(IEnumerable<string> lines) =>
        [.. lines
            .Select(line => line.Trim().Trim('"'))
            .Where(path => path.Length > 0 && !path.StartsWith('#'))];
}
