using HDGraph.Core;
using HDGraph.Tests.TestSupport;

namespace HDGraph.Tests;

public sealed class DirectoryNodeTests
{
    [Fact]
    public async Task FindByPathWalksDownTheTree()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var root = tree.Root;
        var a = root.Children[0];
        var sub = a.Children[0];

        Assert.Same(root, root.FindByPath(tree.RootPath));
        Assert.Same(a, root.FindByPath(Path.Combine(tree.RootPath, "a")));
        Assert.Same(sub, root.FindByPath(Path.Combine(tree.RootPath, "a", "sub")));
        Assert.Same(sub, a.FindByPath(Path.Combine(tree.RootPath, "a", "sub")));
        Assert.Same(a, root.FindByPath(Path.Combine(tree.RootPath, "a") + Path.DirectorySeparatorChar));

        Assert.Null(root.FindByPath(Path.Combine(tree.RootPath, "a", "su")));
        Assert.Null(root.FindByPath(Path.Combine(tree.RootPath, "ab")));
        Assert.Null(root.FindByPath(Path.Combine(tree.RootPath, "x")));
        Assert.Null(root.FindByPath(Path.GetDirectoryName(tree.RootPath)!));
        Assert.Null(a.FindByPath(tree.RootPath));

        if (OperatingSystem.IsWindows())
            Assert.Same(sub, root.FindByPath(Path.Combine(tree.RootPath, "A", "SUB")));
    }

    [Fact]
    public async Task IsUnderFollowsParents()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var root = tree.Root;
        var a = root.Children[0];
        var sub = a.Children[0];

        Assert.True(sub.IsUnder(root));
        Assert.True(sub.IsUnder(a));
        Assert.True(a.IsUnder(a));
        Assert.False(root.IsUnder(sub));
        Assert.False(root.Children[1].IsUnder(a));
    }

    [Fact]
    public async Task PendingNodeKeepsKnownChildrenFirstAndDoesNotReparentThem()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var root = tree.Root;
        var a = root.Children[0];

        var pending = DirectoryNode.CreatePending(tree.RootPath, [a], bytesFoundSoFar: 100, freeSpace: 50);

        Assert.Equal(root.Name, pending.Name);
        Assert.Equal(tree.RootPath, pending.FullPath);
        Assert.Null(pending.Parent);
        Assert.Same(root, a.Parent);

        Assert.Equal(3, pending.Children.Count);
        Assert.Same(a, pending.Children[0]);
        Assert.Equal(NodeKind.Scanning, pending.Children[1].Kind);
        Assert.Equal(100, pending.Children[1].TotalSize);
        Assert.Same(pending, pending.Children[1].Parent);
        Assert.Equal(NodeKind.FreeSpace, pending.Children[2].Kind);
        Assert.Equal(50, pending.Children[2].TotalSize);

        Assert.Equal(a.TotalSize + 150, pending.TotalSize);
        Assert.Equal(a.TotalFileCount, pending.TotalFileCount);
        Assert.Equal(a.TotalDirectoryCount + 1, pending.TotalDirectoryCount);

        // The known part stays reachable by path through the stand-in.
        Assert.Same(a.Children[0], pending.FindByPath(Path.Combine(tree.RootPath, "a", "sub")));
    }

    [Fact]
    public void PendingNodeWithNothingKnownIsJustTheScanningSlice()
    {
        var path = Path.Combine(Path.GetTempPath(), "nothing-known");
        var pending = DirectoryNode.CreatePending(path, [], bytesFoundSoFar: -5);

        var slice = Assert.Single(pending.Children);
        Assert.Equal(NodeKind.Scanning, slice.Kind);
        Assert.Equal(0, slice.TotalSize);
        Assert.Equal(0, pending.TotalSize);
        Assert.Equal("nothing-known", pending.Name);
    }
}
