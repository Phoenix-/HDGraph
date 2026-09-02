namespace HDGraph.Core;

public sealed class ScanOptions
{
    /// <summary>Skip directories that are junctions, symbolic links or mount points. Following them counts
    /// the same bytes twice and can loop; the original HDGraph exposed the same switch.</summary>
    public bool SkipReparsePoints { get; init; } = true;

    /// <summary>When the scanned path is a drive root, add a synthetic slice for the free space of the
    /// drive so the ring reads as "the whole drive".</summary>
    public bool IncludeFreeSpace { get; init; } = true;

    /// <summary>Directories this many levels below the root are scanned concurrently; deeper ones run
    /// sequentially inside their parent's task. Two levels is enough to keep an NVMe busy without
    /// spawning a task per leaf.</summary>
    public int ParallelDepth { get; init; } = 2;

    public int MaxDegreeOfParallelism { get; init; } = Math.Max(2, Environment.ProcessorCount);

    /// <summary>Minimum interval between progress reports.</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Errors beyond this count are counted but not kept.</summary>
    public int MaxRecordedErrors { get; init; } = 1000;
}

public readonly record struct ScanProgress(
    long DirectoriesScanned,
    long FilesScanned,
    long BytesFound,
    string? CurrentPath,
    TimeSpan Elapsed);

public readonly record struct ScanError(string Path, string Message);

public sealed class ScanResult
{
    public ScanResult(DirectoryNode root, TimeSpan elapsed, int errorCount, IReadOnlyList<ScanError> errors)
    {
        Root = root;
        Elapsed = elapsed;
        ErrorCount = errorCount;
        Errors = errors;
    }

    public DirectoryNode Root { get; }
    public TimeSpan Elapsed { get; }

    /// <summary>Total number of directories that could not be read, including those not in <see cref="Errors"/>.</summary>
    public int ErrorCount { get; }

    public IReadOnlyList<ScanError> Errors { get; }
}
