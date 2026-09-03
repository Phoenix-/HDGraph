using HDGraph.Core;

namespace HDGraph.Tests;

public sealed class FilePathsTests
{
    private static readonly string Root = Path.GetPathRoot(Path.GetTempPath())!;
    private static readonly char Sep = Path.DirectorySeparatorChar;

    [Fact]
    public void NormalizeTrimsTrailingSeparatorExceptOnARoot()
    {
        var folder = Path.Combine(Root, "Program Files");
        Assert.Equal(folder, FilePaths.Normalize(folder + Sep));
        Assert.Equal(folder, FilePaths.Normalize(folder));
        Assert.Equal(Root, FilePaths.Normalize(Root));
    }

    [Fact]
    public void RootIsRecognised()
    {
        Assert.True(FilePaths.IsRoot(Root));
        Assert.False(FilePaths.IsRoot(Path.Combine(Root, "x")));
    }

    [Fact]
    public void ParentStopsAtTheRoot()
    {
        var a = Path.Combine(Root, "a");
        Assert.Equal(a, FilePaths.GetParent(Path.Combine(a, "b")));
        Assert.Equal(Root, FilePaths.GetParent(a));
        Assert.Null(FilePaths.GetParent(Root));
    }

    [Fact]
    public void StrictAncestorNeedsAWholeSegment()
    {
        var a = Path.Combine(Root, "a");
        var ab = Path.Combine(a, "b");

        Assert.True(FilePaths.IsStrictAncestor(Root, a));
        Assert.True(FilePaths.IsStrictAncestor(Root, ab));
        Assert.True(FilePaths.IsStrictAncestor(a, ab));

        Assert.False(FilePaths.IsStrictAncestor(a, a));
        Assert.False(FilePaths.IsStrictAncestor(ab, a));
        Assert.False(FilePaths.IsStrictAncestor(Path.Combine(Root, "Program"), Path.Combine(Root, "Program Files")));

        if (OperatingSystem.IsWindows())
            Assert.True(FilePaths.IsStrictAncestor(a.ToUpperInvariant(), ab.ToLowerInvariant()));
    }

    [Fact]
    public void DisplayNameIsTheLastSegmentOrTheDriveWithoutItsSeparator()
    {
        // "C:\" shows as "C:"; a bare "/" has nothing left after trimming and stays "/".
        var expectedRoot = Root.TrimEnd(Sep, Path.AltDirectorySeparatorChar);
        Assert.Equal(expectedRoot.Length == 0 ? Root : expectedRoot, FilePaths.DisplayName(Root));
        Assert.Equal("b", FilePaths.DisplayName(Path.Combine(Root, "a", "b")));
    }
}
