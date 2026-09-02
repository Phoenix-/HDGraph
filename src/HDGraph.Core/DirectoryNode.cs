namespace HDGraph.Core;

/// <summary>What a node stands for. Only directories are scanned; the other kinds are synthetic slices
/// added so the picture of a drive adds up to the drive.</summary>
public enum NodeKind
{
    Directory,
    FreeSpace,
}

/// <summary>One directory of the scanned tree. Files are not kept individually: a directory carries the
/// byte total and count of the files directly inside it, which is all the chart needs and what keeps a
/// million-directory drive in memory.</summary>
public sealed class DirectoryNode
{
    private static readonly IReadOnlyList<DirectoryNode> NoChildren = [];
    private IReadOnlyList<DirectoryNode> _children = NoChildren;

    public DirectoryNode(string name, string fullPath, DirectoryNode? parent, NodeKind kind = NodeKind.Directory)
    {
        Name = name;
        FullPath = fullPath;
        Parent = parent;
        Kind = kind;
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    public string Name { get; }
    public string FullPath { get; }
    public DirectoryNode? Parent { get; }
    public NodeKind Kind { get; }
    public int Depth { get; }

    /// <summary>Bytes of the files directly in this directory (not in subdirectories).</summary>
    public long FilesSize { get; internal set; }

    /// <summary>Number of files directly in this directory.</summary>
    public int FileCount { get; internal set; }

    /// <summary>Bytes of everything under this directory, files here included.</summary>
    public long TotalSize { get; internal set; }

    /// <summary>Number of files under this directory, files here included.</summary>
    public long TotalFileCount { get; internal set; }

    /// <summary>Number of directories under this one, at any depth.</summary>
    public long TotalDirectoryCount { get; internal set; }

    /// <summary>Why this directory could not be read, or null. Its subtree is then unknown, not empty.</summary>
    public string? Error { get; internal set; }

    /// <summary>Subdirectories, largest first.</summary>
    public IReadOnlyList<DirectoryNode> Children => _children;

    public DirectoryNode Root
    {
        get
        {
            var node = this;
            while (node.Parent is not null) node = node.Parent;
            return node;
        }
    }

    /// <summary>Share of this node in an ancestor, 0..1. Returns 0 when the ancestor is empty.</summary>
    public double FractionOf(DirectoryNode ancestor) =>
        ancestor.TotalSize == 0 ? 0 : (double)TotalSize / ancestor.TotalSize;

    internal void SetChildren(List<DirectoryNode> children)
    {
        children.Sort(static (a, b) => b.TotalSize.CompareTo(a.TotalSize));
        _children = children;
    }

    public override string ToString() => $"{FullPath} ({TotalSize} B)";
}
