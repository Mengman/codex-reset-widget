using System.IO;
using System.Windows;
using CodexResetWidget.Platform;
using CodexResetWidget.Platform.Clock;
using CodexResetWidget.Presentation.ViewModels;
using CodexResetWidget.Presentation.Views;

namespace CodexResetWidget;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var theme = new ThemeService();
        theme.Apply(AppThemeMode.System);
        var model = new MainViewModel(new SystemClock(), new SystemTimeZoneService());
        var window = new MainWindow(model, theme);
        MainWindow = window;
        // M1 is always an explicitly labelled demo; no production API or cache is accessed.
        var captureIndex = Array.IndexOf(e.Args, "--capture-dir");
        if (captureIndex >= 0 && captureIndex + 1 < e.Args.Length)
        {
            var directory = Path.GetFullPath(e.Args[captureIndex + 1]);
            window.Loaded += async (_, _) => await PrototypeChecks.RunAsync(window, directory);
        }
        window.Show();
    }
}
