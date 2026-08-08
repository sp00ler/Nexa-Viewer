namespace ViewerPrn.Domain.FileSystem;

public enum EntryKind
{
    Folder = 0,
    File = 1,
}

/// <summary>
/// One row in the Explorer list. Archives are ordinary files here; they become browsable
/// containers in Phase 6.
/// </summary>
/// <param name="Size">Bytes for files. Always 0 for folders — folder sizes are not computed.</param>
public sealed record FileSystemEntry(
    string Name,
    string FullPath,
    EntryKind Kind,
    long Size,
    DateTimeOffset Modified)
{
    public string Extension => Kind == EntryKind.Folder ? string.Empty : Path.GetExtension(Name);
}
