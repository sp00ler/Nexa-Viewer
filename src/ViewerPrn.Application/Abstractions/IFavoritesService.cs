namespace ViewerPrn.Application.Abstractions;

public enum FavoriteKind
{
    Folder = 0,
    Archive = 1,
    Image = 2,
}

/// <param name="Exists">Whether the target is still there — false means the entry is broken.</param>
public sealed record Favorite(long Id, long GroupId, string Path, FavoriteKind Kind, DateTimeOffset Added, bool Exists);

public sealed record FavoriteGroup(long Id, string Name, IReadOnlyList<Favorite> Items);

/// <summary>
/// Named groups of folders, archives and images (docs/REQUIREMENTS.md:31). One target may belong
/// to several groups, and broken targets are reported rather than hidden so they can be repaired
/// or removed.
/// </summary>
public interface IFavoritesService
{
    Task<IReadOnlyList<FavoriteGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);

    Task<long> CreateGroupAsync(string name, CancellationToken cancellationToken = default);

    Task RenameGroupAsync(long groupId, string name, CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(long groupId, CancellationToken cancellationToken = default);

    /// <summary>Adding the same target to the same group twice is a no-op, not an error.</summary>
    Task AddAsync(long groupId, string path, FavoriteKind kind, CancellationToken cancellationToken = default);

    Task RemoveAsync(long favoriteId, CancellationToken cancellationToken = default);

    /// <summary>Points a broken favourite at a new location, keeping its place in the group.</summary>
    Task RepairAsync(long favoriteId, string newPath, CancellationToken cancellationToken = default);
}
