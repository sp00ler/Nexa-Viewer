using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using ViewerPrn.Application.Abstractions;

namespace ViewerPrn.App;

/// <summary>One folder in the tree. Children are filled the first time the node is expanded.</summary>
public sealed class FolderNode
{
    /// <summary>
    /// Placeholder child so the expander chevron appears before the folder has been read. It is
    /// replaced by the real children on first expand — cheaper and far more predictable than
    /// binding HasUnrealizedChildren.
    /// </summary>
    public static FolderNode Placeholder { get; } = new(string.Empty, "…", isDrive: false);

    public FolderNode(string path, string name, bool isDrive)
    {
        Path = path;
        Name = name;
        Glyph = isDrive ? "" : "";

        if (path.Length > 0)
        {
            Children.Add(Placeholder);
        }
    }

    public string Path { get; }

    public string Name { get; }

    public string Glyph { get; }

    public ObservableCollection<FolderNode> Children { get; } = [];

    public bool IsFilled { get; private set; }

    public void Fill(IEnumerable<FolderNode> children)
    {
        Children.Clear();
        foreach (FolderNode child in children)
        {
            Children.Add(child);
        }

        IsFilled = true;
    }
}

/// <summary>
/// The folder tree: drives at the root, subfolders read on demand. One tree serves every tab;
/// which nodes are expanded is remembered per tab and replayed on switch (DECISION-0032).
/// </summary>
public sealed partial class FolderTree : UserControl
{
    private readonly ILoggingService _logger;
    private readonly ObservableCollection<FolderNode> _roots = [];

    public FolderTree(ILoggingService logger)
    {
        _logger = logger;
        InitializeComponent();

        Tree.ItemsSource = _roots;
        LoadDrives();
    }

    /// <summary>Raised when the user picks a folder in the tree.</summary>
    public event EventHandler<string>? FolderSelected;

    /// <summary>Paths of every expanded node, for storing against the active tab.</summary>
    public IReadOnlyList<string> ExpandedPaths =>
        [.. Tree.RootNodes.SelectMany(Flatten)
            .Where(node => node.IsExpanded && node.Content is FolderNode { Path.Length: > 0 })
            .Select(node => ((FolderNode)node.Content).Path)];

    /// <summary>
    /// Rebuilds the visible expansion to match a tab: expands each remembered path and selects
    /// the current folder. Paths that no longer exist are skipped rather than reported — a tree
    /// is a view, and a stale one simply shows less.
    /// </summary>
    public async Task ApplyStateAsync(IReadOnlyList<string> expandedPaths, string? selectedPath)
    {
        foreach (string path in expandedPaths.OrderBy(path => path.Length))
        {
            await ExpandToAsync(path, select: false);
        }

        if (selectedPath is not null)
        {
            await ExpandToAsync(selectedPath, select: true);
        }
    }

    /// <summary>Expands the tree down to a path, so the current folder is visible after navigation.</summary>
    public async Task RevealAsync(string path)
    {
        if (Directory.Exists(path))
        {
            await ExpandToAsync(path, select: true);
        }
    }

    private void LoadDrives()
    {
        _roots.Clear();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

                _roots.Add(new FolderNode(drive.RootDirectory.FullName, label, isDrive: true));
            }
            catch (IOException)
            {
                // A drive that disappears between listing and querying is simply not shown.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private async void OnExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is FolderNode { IsFilled: false, Path.Length: > 0 } node)
        {
            node.Fill(await ReadChildrenAsync(node.Path));
        }
    }

    private void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FolderNode { Path.Length: > 0 } node)
        {
            FolderSelected?.Invoke(this, node.Path);
        }
    }

    private Task<List<FolderNode>> ReadChildrenAsync(string path) => Task.Run(() =>
    {
        List<FolderNode> children = [];

        try
        {
            foreach (DirectoryInfo directory in new DirectoryInfo(path).EnumerateDirectories())
            {
                if (directory.Attributes.HasFlag(FileAttributes.Hidden)
                    && directory.Attributes.HasFlag(FileAttributes.System))
                {
                    continue;
                }

                children.Add(new FolderNode(directory.FullName, directory.Name, isDrive: false));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder the user cannot read shows as empty rather than as an error: the tree is
            // for orientation, and the listing on the right reports the real problem.
            _logger.Log(LogLevel.Debug, $"Tree could not read '{path}': {exception.Message}");
        }

        return children;
    });

    /// <summary>
    /// Walks from the drive down to the path, filling and expanding each level. Returns quietly
    /// when a level is missing.
    /// </summary>
    private async Task ExpandToAsync(string path, bool select)
    {
        string? root = Path.GetPathRoot(path);
        if (root is null)
        {
            return;
        }

        TreeViewNode? current = Tree.RootNodes.FirstOrDefault(node =>
            node.Content is FolderNode folder
            && string.Equals(folder.Path, root, StringComparison.OrdinalIgnoreCase));

        if (current is null)
        {
            return;
        }

        foreach (string segment in RelativeSegments(path, root))
        {
            if (current.Content is FolderNode { IsFilled: false, Path.Length: > 0 } unfilled)
            {
                unfilled.Fill(await ReadChildrenAsync(unfilled.Path));
            }

            current.IsExpanded = true;

            TreeViewNode? next = current.Children.FirstOrDefault(node =>
                node.Content is FolderNode child
                && string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (next is null)
            {
                return;
            }

            current = next;
        }

        if (select)
        {
            Tree.SelectedNode = current;
        }
        else
        {
            current.IsExpanded = true;
        }
    }

    private static string[] RelativeSegments(string path, string root) =>
        path.Length <= root.Length
            ? []
            : path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<TreeViewNode> Flatten(TreeViewNode node) =>
        [node, .. node.Children.SelectMany(Flatten)];
}
