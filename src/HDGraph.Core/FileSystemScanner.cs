using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;

namespace HDGraph.Core;

/// <summary>Builds a <see cref="DirectoryNode"/> tree for a path. One directory enumeration per directory,
/// sizes and attributes taken from the enumeration itself, so the cost is one syscall per directory and no
/// per-file stat. Runs on the thread pool: callers may await it from a UI thread.</summary>
public sealed class FileSystemScanner
{
    private readonly ScanOptions _options;

    public FileSystemScanner(ScanOptions? options = null)
    {
        _options = options ?? new ScanOptions();
    }

    public Task<ScanResult> ScanAsync(string path, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => Scan(path, progress, cancellationToken), cancellationToken);
    }

    private ScanResult Scan(string path, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(fullPath);
        var isDriveRoot = string.Equals(pathRoot, fullPath, StringComparison.OrdinalIgnoreCase);
        if (!isDriveRoot)
            fullPath = Path.TrimEndingDirectorySeparator(fullPath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Directory not found: {fullPath}");

        var displayName = isDriveRoot ? fullPath : Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(displayName)) displayName = fullPath;

        var root = new DirectoryNode(displayName, fullPath, parent: null);
        var run = new ScanRun(_options, progress, cancellationToken);
        run.ScanDirectory(root, depth: 0);

        if (isDriveRoot && _options.IncludeFreeSpace)
            AppendFreeSpace(root, fullPath);

        run.ReportFinal();
        return new ScanResult(root, run.Elapsed, run.ErrorCount, run.Errors);
    }

    private static void AppendFreeSpace(DirectoryNode root, string driveRoot)
    {
        long free;
        try
        {
            free = new DriveInfo(driveRoot).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }

        if (free <= 0) return;
        var node = new DirectoryNode("Free space", driveRoot, root, NodeKind.FreeSpace) { TotalSize = free };
        var children = new List<DirectoryNode>(root.Children.Count + 1);
        children.AddRange(root.Children);
        children.Add(node);
        root.SetChildren(children);
        root.TotalSize += free;
    }

    /// <summary>State of one scan: counters, throttled progress, recorded errors.</summary>
    private sealed class ScanRun
    {
        private readonly ScanOptions _options;
        private readonly IProgress<ScanProgress>? _progress;
        private readonly CancellationToken _cancellationToken;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly ConcurrentQueue<ScanError> _errors = new();
        private readonly long _progressIntervalTicks;
        private long _directories;
        private long _files;
        private long _bytes;
        private int _errorCount;
        private long _lastReportTicks;
        private string? _currentPath;

        public ScanRun(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        {
            _options = options;
            _progress = progress;
            _cancellationToken = cancellationToken;
            _progressIntervalTicks = (long)(options.ProgressInterval.TotalSeconds * Stopwatch.Frequency);
        }

        public TimeSpan Elapsed => _stopwatch.Elapsed;
        public int ErrorCount => _errorCount;
        public IReadOnlyList<ScanError> Errors => _errors.ToArray();

        public void ScanDirectory(DirectoryNode node, int depth)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _currentPath, node.FullPath);

            var subdirectories = new List<DirectoryNode>();
            long filesSize = 0;
            var fileCount = 0;

            try
            {
                var enumeration = new FileSystemEnumerable<Entry>(node.FullPath, Entry.From, EnumerationSettings);
                foreach (var entry in enumeration)
                {
                    if (entry.IsDirectory)
                    {
                        if (_options.SkipReparsePoints && entry.IsReparsePoint) continue;
                        subdirectories.Add(new DirectoryNode(entry.Name, Path.Join(node.FullPath, entry.Name), node));
                    }
                    else
                    {
                        filesSize += entry.Length;
                        fileCount++;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                node.Error = ex.Message;
                RecordError(node.FullPath, ex.Message);
            }

            node.FilesSize = filesSize;
            node.FileCount = fileCount;

            if (depth < _options.ParallelDepth && subdirectories.Count > 1)
            {
                var parallel = new ParallelOptions
                {
                    MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism,
                    CancellationToken = _cancellationToken,
                };
                Parallel.ForEach(subdirectories, parallel, child => ScanDirectory(child, depth + 1));
            }
            else
            {
                foreach (var child in subdirectories)
                    ScanDirectory(child, depth + 1);
            }

            long total = filesSize;
            long totalFiles = fileCount;
            long totalDirectories = subdirectories.Count;
            foreach (var child in subdirectories)
            {
                total += child.TotalSize;
                totalFiles += child.TotalFileCount;
                totalDirectories += child.TotalDirectoryCount;
            }

            node.TotalSize = total;
            node.TotalFileCount = totalFiles;
            node.TotalDirectoryCount = totalDirectories;
            node.SetChildren(subdirectories);

            Interlocked.Increment(ref _directories);
            Interlocked.Add(ref _files, fileCount);
            Interlocked.Add(ref _bytes, filesSize);
            ReportIfDue();
        }

        public void ReportFinal() => _progress?.Report(Snapshot());

        private void RecordError(string path, string message)
        {
            if (Interlocked.Increment(ref _errorCount) <= _options.MaxRecordedErrors)
                _errors.Enqueue(new ScanError(path, message));
        }

        private void ReportIfDue()
        {
            if (_progress is null) return;
            var now = _stopwatch.ElapsedTicks;
            var last = Volatile.Read(ref _lastReportTicks);
            if (now - last < _progressIntervalTicks) return;
            if (Interlocked.CompareExchange(ref _lastReportTicks, now, last) != last) return;
            _progress.Report(Snapshot());
        }

        private ScanProgress Snapshot() => new(
            Volatile.Read(ref _directories),
            Volatile.Read(ref _files),
            Volatile.Read(ref _bytes),
            Volatile.Read(ref _currentPath),
            _stopwatch.Elapsed);

        // AttributesToSkip defaults to Hidden|System, which would silently drop pagefile.sys, hiberfil.sys
        // and every hidden folder: exactly the things people look for when a drive is mysteriously full.
        private static readonly EnumerationOptions EnumerationSettings = new()
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        private readonly record struct Entry(string Name, bool IsDirectory, bool IsReparsePoint, long Length)
        {
            public static Entry From(ref FileSystemEntry entry)
            {
                var isDirectory = entry.IsDirectory;
                return new Entry(
                    entry.FileName.ToString(),
                    isDirectory,
                    (entry.Attributes & FileAttributes.ReparsePoint) != 0,
                    isDirectory ? 0 : entry.Length);
            }
        }
    }
}
