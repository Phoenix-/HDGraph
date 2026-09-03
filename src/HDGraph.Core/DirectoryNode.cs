namespace HDGraph.Core;

/// <summary>What a node stands for. Only directories are scanned; the other kinds are synthetic slices
/// added so the picture adds up.</summary>
public enum NodeKind
{
    Directory,

    /// <summary>Free space of a drive, so the ring of a drive root reads as "the whole drive".</summary>
    FreeSpace,

    /// <summary>What a running scan has found so far, in a stand-in for the directory being scanned.</summary>
    Scanning,
}

/// <summary>One directory of the scanned tree. Files are not kept individually: a directory carries the
/// byte total and count of the files directly inside it, which is all the chart needs and what keeps a
/// million-directory drive in memory.</summary>
public sealed class DirectoryNode
{
    private static readonly IReadOnlyList<DirectoryNode> NoChildren = [];
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    private IReadOnlyList<DirectoryNode> _children = NoChildren;

    public DirectoryNode(string name, string fullPath, DirectoryNode? parent, NodeKind kind = NodeKind.Directory)
    {
        Name = name;
        FullPath = fullPath;
        Parent = parent;
        Kind = kind;
    }

    public string Name { get; }
    public string FullPath { get; }

    /// <summary>Null for the root of a tree. The scanner sets it when it attaches an already scanned
    /// subtree under a newly scanned parent.</summary>
    public DirectoryNode? Parent { get; internal set; }

    public NodeKind Kind { get; }

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

    /// <summary>Whether <paramref name="ancestor"/> is this node or one of its ancestors.</summary>
    public bool IsUnder(DirectoryNode ancestor)
    {
        for (var node = this; node is not null; node = node.Parent)
            if (ReferenceEquals(node, ancestor)) return true;
        return false;
    }

    /// <summary>The node for a normalized path within this subtree: this node, a descendant, or null when
    /// the path lies outside the subtree or was never scanned (a skipped reparse point, a folder created
    /// since the scan).</summary>
    public DirectoryNode? FindByPath(string fullPath)
    {
        if (string.Equals(fullPath, FullPath, FilePaths.Comparison)) return this;
        if (!FilePaths.IsStrictAncestor(FullPath, fullPath)) return null;

        var node = this;
        var rest = fullPath.AsSpan(FullPath.Length);
        foreach (var range in rest.SplitAny(Separators))
        {
            var segment = rest[range];
            if (segment.IsEmpty) continue;

            DirectoryNode? next = null;
            foreach (var child in node.Children)
            {
                if (child.Kind != NodeKind.Directory || !segment.Equals(child.Name, FilePaths.Comparison)) continue;
                next = child;
                break;
            }

            if (next is null) return null;
            node = next;
        }

        return node;
    }

    /// <summary>A stand-in for a directory whose scan is still running: the subtrees already known under it,
    /// then a <see cref="NodeKind.Scanning"/> slice for what the scan has found so far, then the free space of
    /// the drive if given. Children keep that order, so nothing jumps as the slice grows, and the known ones
    /// are not re-parented: the scanner does that when the real node is ready. Each call returns a fresh
    /// instance, so a view that caches by identity notices the change.</summary>
    public static DirectoryNode CreatePending(string fullPath, IReadOnlyList<DirectoryNode> known, long bytesFoundSoFar, long freeSpace = 0)
    {
        ArgumentNullException.ThrowIfNull(known);
        bytesFoundSoFar = Math.Max(0, bytesFoundSoFar);
        freeSpace = Math.Max(0, freeSpace);

        var node = new DirectoryNode(FilePaths.DisplayName(fullPath), fullPath, parent: null);
        var children = new List<DirectoryNode>(known.Count + 2);
        long total = bytesFoundSoFar + freeSpace, files = 0, directories = 0;
        foreach (var child in known)
        {
            children.Add(child);
            total += child.TotalSize;
            files += child.TotalFileCount;
            directories += 1 + child.TotalDirectoryCount;
        }

        children.Add(new DirectoryNode("Scanning…", fullPath, node, NodeKind.Scanning) { TotalSize = bytesFoundSoFar });
        if (freeSpace > 0)
            children.Add(new DirectoryNode("Free space", fullPath, node, NodeKind.FreeSpace) { TotalSize = freeSpace });

        node.TotalSize = total;
        node.TotalFileCount = files;
        node.TotalDirectoryCount = directories;
        node.SetChildren(children, sort: false);
        return node;
    }

    internal void SetChildren(List<DirectoryNode> children, bool sort = true)
    {
        if (sort) children.Sort(static (a, b) => b.TotalSize.CompareTo(a.TotalSize));
        _children = children;
    }

    public override string ToString() => $"{FullPath} ({TotalSize} B)";
}
