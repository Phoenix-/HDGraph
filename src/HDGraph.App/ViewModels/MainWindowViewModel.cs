using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HDGraph.Core;

namespace HDGraph.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>How often the stand-in for the folder being scanned is rebuilt while the scan runs. Each
    /// rebuild is a new root for the chart, hence a fresh layout, so this is deliberately slower than the
    /// scanner's own progress interval.</summary>
    private static readonly TimeSpan PendingRefreshInterval = TimeSpan.FromMilliseconds(250);

    private readonly FileSystemScanner _scanner = new();
    private ScanJob? _job;
    private int _requestId;

    public MainWindowViewModel()
    {
        Drives = new ObservableCollection<DriveItem>(DriveItem.Enumerate());
        PathText = Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty;
        IsEditingPath = true;
    }

    public ObservableCollection<DriveItem> Drives { get; }

    /// <summary>Supplied by the window: shows the system folder picker and returns a local path or null.</summary>
    public Func<Task<string?>>? PickFolderAsync { get; set; }

    /// <summary>Supplied by the window: puts text on the clipboard.</summary>
    public Func<string, Task>? CopyTextAsync { get; set; }

    /// <summary>Text of the path editor; only meaningful while <see cref="IsEditingPath"/>.</summary>
    [ObservableProperty]
    public partial string PathText { get; set; }

    /// <summary>The path bar shows an editor instead of the segments.</summary>
    [ObservableProperty]
    public partial bool IsEditingPath { get; set; }

    /// <summary>Segments of the path bar for <see cref="ViewRoot"/>; replaced as a whole when they change.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<PathCrumb> Crumbs { get; set; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Pick a folder or a drive to scan.";

    [ObservableProperty]
    public partial int ErrorCount { get; set; }

    /// <summary>Root of the scanned tree. Grows upward when the scan is extended to a parent folder.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand))]
    public partial DirectoryNode? ScanRoot { get; set; }

    /// <summary>Node in the centre of the chart: a node of the scanned tree, or the stand-in for the folder
    /// being scanned while a scan runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailNode))]
    [NotifyCanExecuteChangedFor(nameof(GoUpCommand))]
    public partial DirectoryNode? ViewRoot { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailNode))]
    public partial DirectoryNode? HoveredNode { get; set; }

    /// <summary>Node under the pointer when the context menu was requested.</summary>
    [ObservableProperty]
    public partial DirectoryNode? ContextNode { get; set; }

    [ObservableProperty]
    public partial int Rings { get; set; } = 5;

    [ObservableProperty]
    public partial double Rotation { get; set; }

    [ObservableProperty]
    public partial bool ShowSizes { get; set; } = true;

    /// <summary>What the details pane describes: the hovered node, else the centre node.</summary>
    public DirectoryNode? DetailNode => HoveredNode ?? ViewRoot;

    public bool HasDetail => DetailNode is not null;
    public string DetailName => DetailNode?.Name ?? string.Empty;
    public string DetailPath => DetailNode is { Kind: NodeKind.Directory } node ? node.FullPath : string.Empty;
    public string DetailSize => DetailNode is { } node ? SizeFormatter.Format(node.TotalSize) : string.Empty;
    public string DetailError => DetailNode?.Error ?? string.Empty;
    public bool HasDetailError => DetailNode?.Error is not null;

    public string DetailShare
    {
        get
        {
            if (DetailNode is not { } node || ScanRoot is not { } scanRoot || !node.IsUnder(scanRoot)) return string.Empty;
            var ofScan = SizeFormatter.FormatPercent(node.FractionOf(scanRoot));
            if (ViewRoot is { } viewRoot && !ReferenceEquals(viewRoot, scanRoot) && node != viewRoot && node.IsUnder(viewRoot))
                return $"{SizeFormatter.FormatPercent(node.FractionOf(viewRoot))} of {viewRoot.Name}, {ofScan} of {scanRoot.Name}";
            return $"{ofScan} of {scanRoot.Name}";
        }
    }

    public string DetailContents
    {
        get
        {
            if (DetailNode is not { } node) return string.Empty;
            if (node.Kind == NodeKind.Scanning) return "What the running scan has found so far.";
            if (node.Kind != NodeKind.Directory) return string.Empty;
            if (IsPending(node))
                return $"Scan in progress: {Plural(node.TotalFileCount, "file")} in {Plural(node.TotalDirectoryCount, "folder")} known so far";

            var here = node.FileCount == 0
                ? "no files here"
                : $"{Plural(node.FileCount, "file")} here ({SizeFormatter.Format(node.FilesSize)})";
            return $"{Plural(node.TotalFileCount, "file")} in {Plural(node.TotalDirectoryCount, "folder")}, {here}";
        }
    }

    private static string Plural(long count, string noun) => count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";

    private bool IsPending(DirectoryNode node) => _job is { } job && ReferenceEquals(node, job.Pending);

    partial void OnHoveredNodeChanged(DirectoryNode? value) => NotifyDetailChanged();
    partial void OnScanRootChanged(DirectoryNode? value) => NotifyDetailChanged();

    partial void OnViewRootChanged(DirectoryNode? value)
    {
        NotifyDetailChanged();
        UpdateCrumbs();
    }

    private void NotifyDetailChanged()
    {
        OnPropertyChanged(nameof(HasDetail));
        OnPropertyChanged(nameof(DetailName));
        OnPropertyChanged(nameof(DetailPath));
        OnPropertyChanged(nameof(DetailSize));
        OnPropertyChanged(nameof(DetailShare));
        OnPropertyChanged(nameof(DetailContents));
        OnPropertyChanged(nameof(DetailError));
        OnPropertyChanged(nameof(HasDetailError));
    }

    /// <summary>After the tree changed shape under an unchanged <see cref="ViewRoot"/> (a parent appeared above it).</summary>
    private void OnTreeChanged()
    {
        GoUpCommand.NotifyCanExecuteChanged();
        NotifyDetailChanged();
        UpdateCrumbs();
    }

    // ---- Path bar ----

    [RelayCommand]
    private void BeginEditPath()
    {
        if (ViewRoot is { } view) PathText = view.FullPath;
        IsEditingPath = true;
    }

    [RelayCommand]
    private void CancelEditPath()
    {
        if (ViewRoot is not null) IsEditingPath = false;
    }

    [RelayCommand]
    private Task CommitPathAsync() => NavigateToPathAsync(PathText);

    [RelayCommand]
    private Task NavigateToCrumbAsync(PathCrumb? crumb) => crumb is null ? Task.CompletedTask : NavigateToPathAsync(crumb.Path);

    /// <summary>Goes to a path however it relates to what is scanned: inside the tree, the chart just
    /// centres on it; above the tree, the scan is extended up to it; anywhere else, a new scan starts.</summary>
    [RelayCommand]
    private async Task NavigateToPathAsync(string? path)
    {
        string full;
        try
        {
            full = FilePaths.Normalize((path ?? string.Empty).Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            StatusText = string.IsNullOrWhiteSpace(path) ? "Enter a folder or drive to scan." : $"Not a valid path: {path}";
            IsEditingPath = true;
            return;
        }

        if (_job is { IsDone: false } job && job.Covers(full))
        {
            // The running scan will produce this folder; show its stand-in meanwhile.
            IsEditingPath = false;
            ViewRoot = job.Pending;
            if (!FilePaths.PathsEqual(job.Path, full))
                StatusText = $"{full} is being read as part of the scan of {job.Path}…";
            return;
        }

        if (ScanRoot is { } scanRoot)
        {
            if (scanRoot.FindByPath(full) is { } node)
            {
                IsEditingPath = false;
                ViewRoot = node;
                return;
            }

            if (FilePaths.IsStrictAncestor(full, scanRoot.FullPath))
            {
                await StartScanAsync(full, graft: scanRoot);
                return;
            }
        }

        await StartScanAsync(full, graft: null);
    }

    [RelayCommand]
    private Task ScanDriveAsync(DriveItem drive) => NavigateToPathAsync(drive.RootPath);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (PickFolderAsync is null) return;
        var picked = await PickFolderAsync();
        if (picked is null) return;
        await NavigateToPathAsync(picked);
    }

    private bool CanRescan => ScanRoot is not null;

    /// <summary>Reads the scanned tree again from its root.</summary>
    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task RescanAsync() => ScanRoot is { } root ? StartScanAsync(root.FullPath, graft: null) : Task.CompletedTask;

    [RelayCommand]
    private void CancelScan() => _job?.Cancellation.Cancel();

    // ---- Chart navigation ----

    [RelayCommand]
    private void NavigateTo(DirectoryNode? node)
    {
        if (node is { Kind: NodeKind.Directory })
            ViewRoot = node;
    }

    private bool CanGoUp => ViewRoot is { } view && (view.Parent is not null || FilePaths.GetParent(view.FullPath) is not null);

    /// <summary>Centres on the parent: within the tree at once, above the tree by extending the scan.</summary>
    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private Task GoUpAsync()
    {
        if (ViewRoot is not { } view) return Task.CompletedTask;
        if (view.Parent is { } parent)
        {
            ViewRoot = parent;
            return Task.CompletedTask;
        }

        return FilePaths.GetParent(view.FullPath) is { } parentPath ? NavigateToPathAsync(parentPath) : Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenInExplorer(DirectoryNode? node)
    {
        if (node is not { Kind: NodeKind.Directory }) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{node.FullPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task CopyPathAsync(DirectoryNode? node)
    {
        if (node is not { Kind: NodeKind.Directory } || CopyTextAsync is null) return;
        await CopyTextAsync(node.FullPath);
        StatusText = $"Copied: {node.FullPath}";
    }

    /// <summary>Starts a new scan with this folder as its root.</summary>
    [RelayCommand]
    private Task RescanFromAsync(DirectoryNode? node) =>
        node is { Kind: NodeKind.Directory } ? StartScanAsync(node.FullPath, graft: null) : Task.CompletedTask;

    // ---- Scanning ----

    /// <summary>One scan at a time: a new request cancels the running one and waits for it to wind down.</summary>
    private async Task StartScanAsync(string fullPath, DirectoryNode? graft)
    {
        var request = ++_requestId;
        if (_job is { } running)
        {
            running.Cancellation.Cancel();
            await running.Task;
            if (request != _requestId) return;
        }

        var job = new ScanJob(fullPath, graft);
        _job = job;
        job.Task = RunJobAsync(job);
        await job.Task;
    }

    private async Task RunJobAsync(ScanJob job)
    {
        IsScanning = true;
        IsEditingPath = false;
        HoveredNode = null;
        StatusText = $"Scanning {job.Path}…";

        IReadOnlyList<DirectoryNode> known = job.Graft is { } graft ? [graft] : [];
        var freeSpace = FilePaths.IsRoot(job.Path) ? FileSystemScanner.FreeSpaceOf(job.Path) : 0;
        job.Pending = DirectoryNode.CreatePending(job.Path, known, bytesFoundSoFar: 0, freeSpace);
        job.PreviousView = ViewRoot;
        ViewRoot = job.Pending;
        UpdateCrumbs();

        var lastRefresh = Stopwatch.GetTimestamp();
        // Progress<T> captures the UI SynchronizationContext here; the scanner reports from worker threads.
        var progress = new Progress<ScanProgress>(p =>
        {
            if (job.IsDone) return;
            StatusText = $"Scanning… {p.DirectoriesScanned:N0} folders, {p.FilesScanned:N0} files, {SizeFormatter.Format(p.BytesFound)}   {Shorten(p.CurrentPath)}";
            if (Stopwatch.GetElapsedTime(lastRefresh) < PendingRefreshInterval) return;
            lastRefresh = Stopwatch.GetTimestamp();

            var fresh = DirectoryNode.CreatePending(job.Path, known, p.BytesFound, freeSpace);
            var showing = ReferenceEquals(ViewRoot, job.Pending);
            job.Pending = fresh;
            if (showing) ViewRoot = fresh;
        });

        try
        {
            var result = await _scanner.ScanAsync(job.Path, progress, job.Cancellation.Token, job.Graft);
            job.IsDone = true;
            AdoptResult(job, result);
        }
        catch (OperationCanceledException)
        {
            job.IsDone = true;
            RestoreView(job);
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            job.IsDone = true;
            RestoreView(job);
            StatusText = ex.Message;
        }
        finally
        {
            job.IsDone = true;
            job.Cancellation.Dispose();
            if (ReferenceEquals(_job, job))
            {
                _job = null;
                IsScanning = false;
            }

            UpdateCrumbs();
        }
    }

    private void AdoptResult(ScanJob job, ScanResult result)
    {
        var root = result.Root;
        var previousView = ViewRoot;
        var extended = job.Graft is { Parent: not null };

        ErrorCount = extended ? ErrorCount + result.ErrorCount : result.ErrorCount;
        ScanRoot = root;

        // Stay where the user was if that folder is in the new tree; a grafted subtree is the same instance,
        // so an extension leaves the chart untouched and only the path bar grows.
        ViewRoot = previousView is null || ReferenceEquals(previousView, job.Pending)
            ? root
            : root.FindByPath(previousView.FullPath) ?? root;
        OnTreeChanged();

        var summary = $"Scanned {root.FullPath} in {result.Elapsed.TotalSeconds:0.0} s: {SizeFormatter.Format(root.TotalSize)}, {root.TotalFileCount:N0} files, {root.TotalDirectoryCount:N0} folders";
        StatusText = ErrorCount == 0 ? summary : $"{summary}; {ErrorCount:N0} folders could not be read";
    }

    /// <summary>A scan that did not produce a tree leaves the chart where it was before the scan started.</summary>
    private void RestoreView(ScanJob job)
    {
        if (ReferenceEquals(ViewRoot, job.Pending))
        {
            var previous = job.PreviousView;
            ViewRoot = ScanRoot is { } scanRoot && previous is not null && previous.IsUnder(scanRoot) ? previous : ScanRoot;
        }

        if (ViewRoot is null)
            IsEditingPath = true;
    }

    private void UpdateCrumbs()
    {
        var crumbs = BuildCrumbs();
        if (!crumbs.SequenceEqual(Crumbs))
            Crumbs = crumbs;
    }

    private List<PathCrumb> BuildCrumbs()
    {
        var crumbs = new List<PathCrumb>();
        if (ViewRoot is not { } view) return crumbs;

        var scanRoot = ScanRoot;
        var job = _job is { IsDone: false } running ? running : null;
        for (var path = view.FullPath; path is not null; path = FilePaths.GetParent(path))
        {
            var node = scanRoot?.FindByPath(path);
            var state = node is not null ? CrumbState.Scanned
                : job is not null && job.Covers(path) ? CrumbState.Scanning
                : CrumbState.Unscanned;
            crumbs.Add(new PathCrumb(path, node?.Name ?? FilePaths.DisplayName(path), state));
        }

        crumbs.Reverse();
        return crumbs;
    }

    private static string Shorten(string? path, int max = 80)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= max) return path ?? string.Empty;
        return path[..8] + "…" + path[^(max - 9)..];
    }

    private sealed class ScanJob(string path, DirectoryNode? graft)
    {
        public string Path { get; } = path;

        /// <summary>The tree this scan grows upward from, or null for a fresh scan.</summary>
        public DirectoryNode? Graft { get; } = graft;

        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>Stand-in shown in the chart while the scan runs; replaced as bytes come in.</summary>
        public DirectoryNode Pending { get; set; } = null!;

        /// <summary>What the chart showed when the scan started, to go back to if it produces nothing.</summary>
        public DirectoryNode? PreviousView { get; set; }

        public Task Task { get; set; } = Task.CompletedTask;

        /// <summary>Set once the result is in or the scan failed; late progress reports are then ignored.</summary>
        public bool IsDone { get; set; }

        /// <summary>Whether this scan reads the folder at <paramref name="fullPath"/>.</summary>
        public bool Covers(string fullPath) => FilePaths.PathsEqual(Path, fullPath) || FilePaths.IsStrictAncestor(Path, fullPath);
    }
}
