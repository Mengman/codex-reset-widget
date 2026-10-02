using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using CodexResetWidget.Platform;
using CodexResetWidget.Platform.Clock;
using CodexResetWidget.Presentation.ViewModels;
using CodexResetWidget.Presentation.Views;
using CodexResetWidget.Application;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;

namespace CodexResetWidget;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var theme = new ThemeService();
        theme.Apply(AppThemeMode.System);
        var captureIndex = Array.IndexOf(e.Args, "--capture-dir");
        var desktopIndex = Array.IndexOf(e.Args, "--desktop-check-dir");
        var demo = captureIndex >= 0 || desktopIndex >= 0;
        var clock = new SystemClock();
        var http = demo ? null : new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexResetWidget", "cache");
        var cacheArg = Array.IndexOf(e.Args, "--cache-dir");
        if (cacheArg >= 0 && cacheArg + 1 < e.Args.Length) cacheRoot = Path.GetFullPath(e.Args[cacheArg + 1]);
        var log = new DiagnosticLog(Path.Combine(Path.GetDirectoryName(cacheRoot)!, "logs"));
        var sync = http is null ? null : new SyncController(new CodexResetsClient(http), new JsonCacheStore(cacheRoot), clock, log.Write);
        var model = new MainViewModel(clock, new SystemTimeZoneService(), sync);
        var settingsArg = Array.IndexOf(e.Args, "--settings-dir");
        var settingsRoot = settingsArg >= 0 && settingsArg + 1 < e.Args.Length ? Path.GetFullPath(e.Args[settingsArg + 1])
            : desktopIndex >= 0 && desktopIndex + 1 < e.Args.Length ? Path.Combine(Path.GetFullPath(e.Args[desktopIndex + 1]), "isolated-settings")
            : Path.GetDirectoryName(cacheRoot)!;
        var store = !demo || desktopIndex >= 0 || settingsArg >= 0 ? new SettingsStore(settingsRoot) : null;
        var settings = store?.Load() ?? new CodexResetWidget.Domain.DesktopSettings();
        LanguageService.Initialize(Enum.Parse<LanguageMode>(settings.Language));
        theme.Apply(Enum.Parse<AppThemeMode>(settings.Theme));
        model.RestoreDesktop(settings.Compact, settings.Pinned);
        var window = new MainWindow(model, theme, store, settings, enableTray: !demo || desktopIndex >= 0,
            enableBackdrop: captureIndex < 0 && desktopIndex < 0, startup: demo ? DesktopChecks.CreateStartupService()
                : new StartupService(new WindowsStartupRegistration(), Environment.ProcessPath!));
        MainWindow = window;
        // Demo and capture modes never construct the production provider or cache.
        if (captureIndex >= 0 && captureIndex + 1 < e.Args.Length)
        {
            var directory = Path.GetFullPath(e.Args[captureIndex + 1]);
            window.Loaded += async (_, _) => await PrototypeChecks.RunAsync(window, directory);
        }
        if (desktopIndex >= 0 && desktopIndex + 1 < e.Args.Length)
            window.Loaded += async (_, _) => await DesktopChecks.RunAsync(window, store!, Path.GetFullPath(e.Args[desktopIndex + 1]));
        if (sync is not null)
        {
            var lifetime = new CancellationTokenSource();
            void Updated(CodexResetWidget.Domain.WidgetSnapshot snapshot) => Dispatcher.BeginInvoke(() => model.ApplySnapshot(snapshot));
            sync.Updated += Updated;
            void NetworkChanged(object? sender, NetworkAvailabilityEventArgs args)
            {
                if (args.IsAvailable && !lifetime.IsCancellationRequested) _ = sync.RefreshAsync(false);
            }
            NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
            window.Closed += (_, _) => { lifetime.Cancel(); sync.Updated -= Updated; NetworkChange.NetworkAvailabilityChanged -= NetworkChanged; http?.Dispose(); };
            window.Loaded += async (_, _) =>
            {
                try
                {
                    await sync.InitializeAsync();
                    var liveCapture = Array.IndexOf(e.Args, "--live-capture-dir");
                    if (liveCapture >= 0 && liveCapture + 1 < e.Args.Length)
                    {
                        await LiveChecks.RunAsync(window, sync, Path.GetFullPath(e.Args[liveCapture + 1]));
                        return;
                    }
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
                    while (await timer.WaitForNextTickAsync(lifetime.Token)) await sync.PollAsync();
                }
                catch (OperationCanceledException) { }
            };
        }
        window.Show();
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    { if (MainWindow is MainWindow window) window.PrepareForExit(); base.OnSessionEnding(e); }
}
