using System.Collections.Concurrent;
using HDGraph.Core;
using HDGraph.Tests.TestSupport;

namespace HDGraph.Tests;

public sealed class FileSystemScannerTests
{
    private const long ExpectedRootTotalSize =
        ScannedTestTree.RootFileSize + ScannedTestTree.AFile1Size + ScannedTestTree.AFile2Size +
        ScannedTestTree.SubFileSize + ScannedTestTree.BFileSize;

    private const long ExpectedATotalSize =
        ScannedTestTree.AFile1Size + ScannedTestTree.AFile2Size + ScannedTestTree.SubFileSize;

    [Fact]
    public async Task StandardTreeIsSummedAndSortedCorrectly()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var root = tree.Root;

        Assert.Equal(ExpectedRootTotalSize, root.TotalSize);
        Assert.Equal(ScannedTestTree.RootFileSize, root.FilesSize);
        Assert.Equal(1, root.FileCount);
        Assert.Equal(5, root.TotalFileCount);
        Assert.Equal(4, root.TotalDirectoryCount);
        Assert.Null(root.Error);
        Assert.Null(root.Parent);
        Assert.Equal(0, tree.Result.ErrorCount);
        Assert.Equal(Path.GetFileName(tree.RootPath), root.Name);

        Assert.Equal(3, root.Children.Count);
        Assert.Equal(["a", "b", "c"], root.Children.Select(static c => c.Name));

        var a = root.Children[0];
        var b = root.Children[1];
        var c = root.Children[2];

        Assert.Equal(ExpectedATotalSize, a.TotalSize);
        Assert.Single(a.Children);
        Assert.Equal("sub", a.Children[0].Name);
        Assert.Equal(ScannedTestTree.SubFileSize, a.Children[0].TotalSize);

        Assert.Equal(ScannedTestTree.BFileSize, b.TotalSize);

        Assert.Equal(0, c.TotalSize);
        Assert.Empty(c.Children);

        AssertParentsAreConsistent(root);
    }

    [Fact]
    public async Task HiddenAndSystemFilesAreCounted()
    {
        var rootPath = ScannedTestTree.CreateTempDirectory();
        try
        {
            const int hiddenFileSize = 321;
            var filePath = Path.Combine(rootPath, "hidden.bin");
            File.WriteAllBytes(filePath, new byte[hiddenFileSize]);
            File.SetAttributes(filePath, FileAttributes.Hidden | FileAttributes.System);

            var scanner = new FileSystemScanner();
            var result = await scanner.ScanAsync(rootPath);

            Assert.Equal(hiddenFileSize, result.Root.TotalSize);
            Assert.Equal(1, result.Root.FileCount);
        }
        finally
        {
            ScannedTestTree.TryDelete(rootPath);
        }
    }

    [Fact]
    public async Task ProgressIsReportedAndFinalSnapshotMatchesTotals()
    {
        var progress = new RecordingProgress();
        var options = new ScanOptions { ProgressInterval = TimeSpan.Zero };
        await using var tree = await ScannedTestTree.CreateAsync(options, progress);

        var reports = progress.Reports;
        Assert.NotEmpty(reports);

        var final = reports[^1];
        Assert.Equal(5, final.DirectoriesScanned);
        Assert.Equal(5, final.FilesScanned);
        Assert.Equal(ExpectedRootTotalSize, final.BytesFound);
    }

    [Fact]
    public async Task PreCancelledTokenThrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var scanner = new FileSystemScanner();
        var path = Path.Combine(Path.GetTempPath(), "hdgraph-tests-" + Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.ScanAsync(path, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task NonExistentPathThrows()
    {
        var scanner = new FileSystemScanner();
        var path = Path.Combine(Path.GetTempPath(), "hdgraph-tests-missing-" + Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => scanner.ScanAsync(path));
    }

    [Fact]
    public async Task ReparsePointsAreSkippedOrFollowedPerOption()
    {
        if (!OperatingSystem.IsWindows()) return;

        var rootPath = ScannedTestTree.CreateTempDirectory();
        try
        {
            var targetPath = Path.Combine(rootPath, "target");
            Directory.CreateDirectory(targetPath);
            File.WriteAllBytes(Path.Combine(targetPath, "file.bin"), new byte[123]);

            var linkPath = Path.Combine(rootPath, "link");
            try
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Creating a symbolic link requires elevation or Developer Mode; skip silently.
                return;
            }

            var skippingScanner = new FileSystemScanner(new ScanOptions { SkipReparsePoints = true });
            var skippingResult = await skippingScanner.ScanAsync(rootPath);
            Assert.DoesNotContain(skippingResult.Root.Children, static c => c.Name == "link");

            var followingScanner = new FileSystemScanner(new ScanOptions { SkipReparsePoints = false });
            var followingResult = await followingScanner.ScanAsync(rootPath);
            Assert.Contains(followingResult.Root.Children, static c => c.Name == "link");
        }
        finally
        {
            ScannedTestTree.TryDelete(rootPath);
        }
    }

    [Fact]
    public async Task GraftIsAttachedWhereTheScanReachesIt()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var scanner = new FileSystemScanner(new ScanOptions { ProgressInterval = TimeSpan.Zero });
        var a = (await scanner.ScanAsync(Path.Combine(tree.RootPath, "a"))).Root;
        Assert.Null(a.Parent);

        var progress = new RecordingProgress();
        var extended = await scanner.ScanAsync(tree.RootPath, progress, graft: a);
        var root = extended.Root;

        Assert.Same(a, root.Children[0]);
        Assert.Same(root, a.Parent);
        Assert.Same(root, a.Children[0].Root);
        Assert.Equal(ExpectedRootTotalSize, root.TotalSize);
        Assert.Equal(5, root.TotalFileCount);
        Assert.Equal(4, root.TotalDirectoryCount);
        Assert.Same(a.Children[0], root.FindByPath(Path.Combine(tree.RootPath, "a", "sub")));
        AssertParentsAreConsistent(root);

        // The grafted part is not read again: progress counts only what this scan did.
        var final = progress.Reports[^1];
        Assert.Equal(3, final.DirectoriesScanned);
        Assert.Equal(ExpectedRootTotalSize - ExpectedATotalSize, final.BytesFound);
    }

    [Fact]
    public async Task GraftTwoLevelsDownIsReachedThroughItsScannedParent()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var scanner = new FileSystemScanner();
        var sub = (await scanner.ScanAsync(Path.Combine(tree.RootPath, "a", "sub"))).Root;

        var extended = await scanner.ScanAsync(tree.RootPath, graft: sub);
        var a = extended.Root.Children[0];

        Assert.Equal("a", a.Name);
        Assert.Same(sub, Assert.Single(a.Children));
        Assert.Same(a, sub.Parent);
        Assert.Equal(ExpectedATotalSize, a.TotalSize);
        Assert.Equal(ExpectedRootTotalSize, extended.Root.TotalSize);
    }

    [Fact]
    public async Task GraftOutsideTheScannedPathIsRejected()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var scanner = new FileSystemScanner();
        var b = (await scanner.ScanAsync(Path.Combine(tree.RootPath, "b"))).Root;

        await Assert.ThrowsAsync<ArgumentException>(() => scanner.ScanAsync(Path.Combine(tree.RootPath, "a"), graft: b));
        await Assert.ThrowsAsync<ArgumentException>(() => scanner.ScanAsync(Path.Combine(tree.RootPath, "b"), graft: b));
        Assert.Null(b.Parent);

        // A node inside a tree cannot be grafted: its old tree would be left pointing at it.
        await Assert.ThrowsAsync<ArgumentException>(() => scanner.ScanAsync(tree.RootPath, graft: tree.Root.Children[0]));
    }

    [Fact]
    public async Task CancelledScanLeavesTheGraftDetached()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var scanner = new FileSystemScanner();
        var a = (await scanner.ScanAsync(Path.Combine(tree.RootPath, "a"))).Root;

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.ScanAsync(tree.RootPath, cancellationToken: cts.Token, graft: a));

        Assert.Null(a.Parent);
    }

    [Fact]
    public async Task GraftWhoseDirectoryIsGoneStaysDetached()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var scanner = new FileSystemScanner();
        var a = (await scanner.ScanAsync(Path.Combine(tree.RootPath, "a"))).Root;
        Directory.Delete(Path.Combine(tree.RootPath, "a"), recursive: true);

        var extended = await scanner.ScanAsync(tree.RootPath, graft: a);

        Assert.Null(a.Parent);
        Assert.DoesNotContain(extended.Root.Children, static c => c.Name == "a");
        Assert.Equal(ScannedTestTree.RootFileSize + ScannedTestTree.BFileSize, extended.Root.TotalSize);
    }

    private static void AssertParentsAreConsistent(DirectoryNode node)
    {
        foreach (var child in node.Children)
        {
            Assert.Same(node, child.Parent);
            AssertParentsAreConsistent(child);
        }
    }

    private sealed class RecordingProgress : IProgress<ScanProgress>
    {
        private readonly ConcurrentQueue<ScanProgress> _reports = new();

        public IReadOnlyList<ScanProgress> Reports => [.. _reports];

        public void Report(ScanProgress value) => _reports.Enqueue(value);
    }
}
