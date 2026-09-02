using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HDGraph.App.Views;

namespace HDGraph.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // "hdgraph <path>" scans that path right away: what an Explorer context-menu entry or a shortcut passes.
            var startPath = desktop.Args is { Length: > 0 } args && Directory.Exists(args[0]) ? Path.GetFullPath(args[0]) : null;
            desktop.MainWindow = new MainWindow(startPath);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
