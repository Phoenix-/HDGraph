using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HDGraph.Core;

namespace HDGraph.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly FileSystemScanner _scanner = new();

    public MainWindowViewModel()
    {
        Drives = new ObservableCollection<DriveItem>(DriveItem.Enumerate());
        PathText = Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty;
    }

    public ObservableCollection<DriveItem> Drives { get; }

    /// <summary>Supplied by the window: shows the system folder picker and returns a local path or null.</summary>
    public Func<Task<string?>>? PickFolderAsync { get; set; }

    /// <summary>Supplied by the window: puts text on the clipboard.</summary>
    public Func<string, Task>? CopyTextAsync { get; set; }

    [ObservableProperty]
    public partial string PathText { get; set; }

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Pick a folder or a drive and press Scan.";

    [ObservableProperty]
    public partial int ErrorCount { get; set; }

    /// <summary>Root of the last scan.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoHomeCommand))]
    public partial DirectoryNode? ScanRoot { get; set; }

    /// <summary>Node currently in the centre of the chart; a descendant of <see cref="ScanRoot"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailNode), nameof(ViewRootPath))]
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

    public string ViewRootPath => ViewRoot?.FullPath ?? string.Empty;

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
            if (DetailNode is not { } node || ScanRoot is not { } scanRoot) return string.Empty;
            var ofScan = SizeFormatter.FormatPercent(node.FractionOf(scanRoot));
            if (ViewRoot is { } viewRoot && !ReferenceEquals(viewRoot, scanRoot) && node != viewRoot)
                return $"{SizeFormatter.FormatPercent(node.FractionOf(viewRoot))} of {viewRoot.Name}, {ofScan} of {scanRoot.Name}";
            return $"{ofScan} of {scanRoot.Name}";
        }
    }

    public string DetailContents
    {
        get
        {
            if (DetailNode is not { Kind: NodeKind.Directory } node) return string.Empty;
            var here = node.FileCount == 0
                ? "no files here"
                : $"{Plural(node.FileCount, "file")} here ({SizeFormatter.Format(node.FilesSize)})";
            return $"{Plural(node.TotalFileCount, "file")} in {Plural(node.TotalDirectoryCount, "folder")}, {here}";
        }
    }

    private static string Plural(long count, string noun) => count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";

    partial void OnHoveredNodeChanged(DirectoryNode? value) => NotifyDetailChanged();
    partial void OnViewRootChanged(DirectoryNode? value) => NotifyDetailChanged();
    partial void OnScanRootChanged(DirectoryNode? value) => NotifyDetailChanged();

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

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        var path = PathText.Trim();
        if (path.Length == 0)
        {
            StatusText = "Enter a folder or drive to scan.";
            return;
        }

        IsScanning = true;
        HoveredNode = null;
        ErrorCount = 0;
        StatusText = $"Scanning {path}…";

        // Progress<T> captures the UI SynchronizationContext here; the scanner reports from worker threads.
        var progress = new Progress<ScanProgress>(p =>
            StatusText = $"Scanning… {p.DirectoriesScanned:N0} folders, {p.FilesScanned:N0} files, {SizeFormatter.Format(p.BytesFound)}   {Shorten(p.CurrentPath)}");

        try
        {
            var result = await _scanner.ScanAsync(path, progress, cancellationToken);
            var root = result.Root;
            ScanRoot = root;
            ViewRoot = root;
            ErrorCount = result.ErrorCount;

            var summary = $"{root.Name}: {SizeFormatter.Format(root.TotalSize)} in {root.TotalFileCount:N0} files, {root.TotalDirectoryCount:N0} folders ({result.Elapsed.TotalSeconds:0.0} s)";
            StatusText = result.ErrorCount == 0 ? summary : $"{summary}; {result.ErrorCount:N0} folders could not be read";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private Task ScanDriveAsync(DriveItem drive)
    {
        PathText = drive.RootPath;
        return ScanCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (PickFolderAsync is null) return;
        var picked = await PickFolderAsync();
        if (picked is null) return;
        PathText = picked;
        await ScanCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void NavigateTo(DirectoryNode? node)
    {
        if (node is { Kind: NodeKind.Directory })
            ViewRoot = node;
    }

    private bool CanGoUp => ViewRoot?.Parent is not null;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp()
    {
        if (ViewRoot?.Parent is { } parent)
            ViewRoot = parent;
    }

    private bool CanGoHome => ScanRoot is not null;

    [RelayCommand(CanExecute = nameof(CanGoHome))]
    private void GoHome() => ViewRoot = ScanRoot;

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

    [RelayCommand]
    private Task RescanAsync(DirectoryNode? node)
    {
        if (node is not { Kind: NodeKind.Directory }) return Task.CompletedTask;
        PathText = node.FullPath;
        return ScanCommand.ExecuteAsync(null);
    }

    private static string Shorten(string? path, int max = 80)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= max) return path ?? string.Empty;
        return path[..8] + "…" + path[^(max - 9)..];
    }
}
