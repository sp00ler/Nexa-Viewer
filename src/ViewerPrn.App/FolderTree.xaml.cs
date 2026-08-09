using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;
using ViewerPrn.Application.Abstractions;

namespace ViewerPrn.App;

/// <summary>One folder in the tree. Children are read once, the first time it is expanded.</summary>
public sealed class FolderNode : INotifyPropertyChanged
{
    private Task? _filling;
    private bool _isExpanded;

    public FolderNode(string path, string name, bool isDrive)
    {
        Path = path;
        Name = name;
        Glyph = isDrive ? "" : "";

        if (path.Length > 0)
        {
            // A placeholder child so the expander chevron appears before the folder has been
            // read. Its own path is empty, so it never gets a placeholder of its own — and it is
            // a fresh instance per node, never one object shared across the whole tree.
            Children.Add(new FolderNode(string.Empty, "…", isDrive: false));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }

    public string Name { get; }

    public string Glyph { get; }

    public ObservableCollection<FolderNode> Children { get; } = [];

    /// <summary>
    /// Two-way bound to the container, so expanding in code and expanding by clicking are the
    /// same thing and neither needs the node-based TreeView API.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                Notify();
            }
        }
    }

    /// <summary>
    /// Reads the children exactly once, however many callers ask. Both the Expanding event and
    /// the code that walks down to a path end up here, and letting them both rewrite Children
    /// while the TreeView is realising it corrupts the control.
    /// </summary>
    public Task FillAsync(Func<string, Task<List<FolderNode>>> read) => _filling ??= FillCoreAsync(read);

    private async Task FillCoreAsync(Func<string, Task<List<FolderNode>>> read)
    {
        List<FolderNode> children = await read(Path);

        Children.Clear();
        foreach (FolderNode child in children)
        {
            Children.Add(child);
        }
    }

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>
/// The folder tree: drives at the root, subfolders read on demand. One tree serves every tab;
/// which nodes are expanded is remembered per tab and replayed on switch (DECISION-0032).
/// </summary>
public sealed partial class FolderTree : UserControl
{
    private readonly ILoggingService _logger;
    private readonly ObservableCollection<FolderNode> _roots = [];
    private bool _walking;
    private bool _walkAgain;

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
    public IReadOnlyList<string> ExpandedPaths => [.. _roots.SelectMany(Expanded)];

    /// <summary>
    /// Rebuilds the visible expansion to match a tab: expands each remembered path and selects
    /// the current folder. Paths that no longer exist are skipped rather than reported — a tree
    /// is a view, and a stale one simply shows less.
    /// </summary>
    public Task ApplyStateAsync(IReadOnlyList<string> expandedPaths, string? selectedPath) =>
        WalkAsync(async () =>
        {
            foreach (string path in expandedPaths.OrderBy(path => path.Length))
            {
                await ExpandToAsync(path, select: false);
            }

            if (selectedPath is not null)
            {
                await ExpandToAsync(selectedPath, select: true);
            }
        });

    /// <summary>Expands down to a path, so the current folder is visible after navigation.</summary>
    public Task RevealAsync(string path) =>
        Directory.Exists(path) ? WalkAsync(() => ExpandToAsync(path, select: true)) : Task.CompletedTask;

    /// <summary>
    /// Only one walk of the tree at a time. Switching tabs quickly used to start a second one
    /// while the first was still awaiting a directory read, and two walks expanding and selecting
    /// in the same control do not survive contact with each other.
    /// </summary>
    private async Task WalkAsync(Func<Task> walk)
    {
        if (_walking)
        {
            // A newer request arrived mid-walk; the current one finishes and then repeats.
            _walkAgain = true;
            return;
        }

        _walking = true;
        try
        {
            do
            {
                _walkAgain = false;
                await walk();
            }
            while (_walkAgain);
        }
        finally
        {
            _walking = false;
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
        if (args.Item is FolderNode { Path.Length: > 0 } node)
        {
            await node.FillAsync(ReadChildrenAsync);
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
    /// when a level is missing. Everything here goes through the bound items, never the node API.
    /// </summary>
    private async Task ExpandToAsync(string path, bool select)
    {
        string? root = System.IO.Path.GetPathRoot(path);
        if (root is null)
        {
            return;
        }

        FolderNode? current = _roots.FirstOrDefault(node =>
            string.Equals(node.Path, root, StringComparison.OrdinalIgnoreCase));

        if (current is null)
        {
            return;
        }

        foreach (string segment in RelativeSegments(path, root))
        {
            await current.FillAsync(ReadChildrenAsync);
            current.IsExpanded = true;

            FolderNode? next = current.Children.FirstOrDefault(child =>
                string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (next is null)
            {
                return;
            }

            current = next;
        }

        if (select)
        {
            Tree.SelectedItem = current;
        }
        else
        {
            current.IsExpanded = true;
        }
    }

    private static string[] RelativeSegments(string path, string root) =>
        path.Length <= root.Length
            ? []
            : path[root.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<string> Expanded(FolderNode node) =>
        node.IsExpanded
            ? [node.Path, .. node.Children.SelectMany(Expanded)]
            : [];
}
