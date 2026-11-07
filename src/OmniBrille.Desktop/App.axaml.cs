using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace OmniBrille.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Program.OmniSorSeHandoffEndpoint is not null || Program.UseAdvancedExplorer
                ? new MainWindow(Program.StartupRoot, Program.StartupTheme, Program.OmniSorSeHandoffEndpoint)
                : new MyFilesWindow(Program.StartupRoot, Program.StartupTheme);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
