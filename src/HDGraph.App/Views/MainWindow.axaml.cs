using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using HDGraph.App.ViewModels;

namespace HDGraph.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow() : this(startPath: null)
    {
    }

    public MainWindow(string? startPath)
    {
        InitializeComponent();
        Title = $"HDGraph {AppInfo.Version}";
        _viewModel = new MainWindowViewModel
        {
            PickFolderAsync = PickFolderAsync,
            CopyTextAsync = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask,
        };
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Crumbs.ItemClicked += OnCrumbClicked;
        CrumbsHost.PointerPressed += OnCrumbsHostPressed;
        PathEditor.KeyDown += OnPathEditorKeyDown;
        PathEditor.LostFocus += (_, _) => _viewModel.CancelEditPathCommand.Execute(null);

        if (startPath is not null)
            Opened += (_, _) => _viewModel.NavigateToPathCommand.Execute(startPath);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsEditingPath) || !_viewModel.IsEditingPath) return;
        // The editor becomes visible on this same property change; focus it once it is laid out.
        Dispatcher.UIThread.Post(() =>
        {
            PathEditor.Focus();
            PathEditor.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnCrumbClicked(FABreadcrumbBar sender, FABreadcrumbBarItemClickedEventArgs args) =>
        _viewModel.NavigateToCrumbCommand.Execute(args.Item as PathCrumb);

    /// <summary>A click on the bar itself (not on a segment, those handle their own clicks) opens the editor.</summary>
    private void OnCrumbsHostPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CrumbsHost).Properties.IsLeftButtonPressed) return;
        _viewModel.BeginEditPathCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPathEditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.CommitPathCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.CancelEditPathCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to scan",
            AllowMultiple = false,
        });
        return picked.Count == 0 ? null : picked[0].TryGetLocalPath();
    }
}
