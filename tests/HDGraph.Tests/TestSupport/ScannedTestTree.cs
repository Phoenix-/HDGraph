using HDGraph.Core;

namespace HDGraph.Tests.TestSupport;

/// <summary>Builds the standard test tree on disk under a unique temp directory, scans it with
/// <see cref="FileSystemScanner"/>, and deletes it on dispose. <see cref="DirectoryNode"/> setters are
/// internal, so this is the only way tests can get a populated tree to assert against.</summary>
/// <remarks>
/// Layout, sizes in bytes:
/// <code>
/// root/root.bin          (RootFileSize)
/// root/a/file1.bin        (AFile1Size)
/// root/a/file2.bin        (AFile2Size)
/// root/a/sub/file3.bin    (SubFileSize)
/// root/b/fileb.bin        (BFileSize)
/// root/c/                 (empty)
/// </code>
/// </remarks>
internal sealed class ScannedTestTree : IAsyncDisposable
{
    public const int RootFileSize = 7;
    public const int AFile1Size = 1000;
    public const int AFile2Size = 2000;
    public const int SubFileSize = 4096;
    public const int BFileSize = 10;

    private ScannedTestTree(string rootPath, ScanResult result)
    {
        RootPath = rootPath;
        Result = result;
    }

    public string RootPath { get; }
    public ScanResult Result { get; }
    public DirectoryNode Root => Result.Root;

    public static async Task<ScannedTestTree> CreateAsync(
        ScanOptions? options = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var rootPath = CreateTempDirectory();
        try
        {
            BuildLayout(rootPath);
            var scanner = new FileSystemScanner(options);
            var result = await scanner.ScanAsync(rootPath, progress, cancellationToken).ConfigureAwait(false);
            return new ScannedTestTree(rootPath, result);
        }
        catch
        {
            TryDelete(rootPath);
            throw;
        }
    }

    private static void BuildLayout(string rootPath)
    {
        File.WriteAllBytes(Path.Combine(rootPath, "root.bin"), new byte[RootFileSize]);

        var a = Directory.CreateDirectory(Path.Combine(rootPath, "a")).FullName;
        File.WriteAllBytes(Path.Combine(a, "file1.bin"), new byte[AFile1Size]);
        File.WriteAllBytes(Path.Combine(a, "file2.bin"), new byte[AFile2Size]);

        var sub = Directory.CreateDirectory(Path.Combine(a, "sub")).FullName;
        File.WriteAllBytes(Path.Combine(sub, "file3.bin"), new byte[SubFileSize]);

        var b = Directory.CreateDirectory(Path.Combine(rootPath, "b")).FullName;
        File.WriteAllBytes(Path.Combine(b, "fileb.bin"), new byte[BFileSize]);

        Directory.CreateDirectory(Path.Combine(rootPath, "c"));
    }

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hdgraph-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup; leaked temp folders don't fail the test run.
        }
    }

    public ValueTask DisposeAsync()
    {
        TryDelete(RootPath);
        return ValueTask.CompletedTask;
    }
}
