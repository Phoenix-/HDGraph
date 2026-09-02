using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using HDGraph.App.ViewModels;

namespace HDGraph.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() : this(startPath: null)
    {
    }

    public MainWindow(string? startPath)
    {
        InitializeComponent();
        var viewModel = new MainWindowViewModel
        {
            PickFolderAsync = PickFolderAsync,
            CopyTextAsync = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask,
        };
        DataContext = viewModel;

        if (startPath is not null)
        {
            viewModel.PathText = startPath;
            Opened += (_, _) => viewModel.ScanCommand.Execute(null);
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
