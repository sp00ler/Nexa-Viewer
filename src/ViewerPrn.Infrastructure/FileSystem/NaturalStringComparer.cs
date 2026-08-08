using System.Runtime.InteropServices;

namespace ViewerPrn.Infrastructure.FileSystem;

/// <summary>
/// The name order File Explorer uses: digits compare numerically, so <c>img2</c> sorts before
/// <c>img10</c>. This is the shell's own comparison function rather than an imitation of it.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y) => (x, y) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        _ => StrCmpLogicalW(x, y),
    };

    // ponytail: DllImport, not LibraryImport — the source generator would force
    // AllowUnsafeBlocks on the whole project for one two-string call.
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);
}
