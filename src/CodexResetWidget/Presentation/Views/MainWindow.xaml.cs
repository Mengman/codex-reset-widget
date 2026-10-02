using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using CodexResetWidget.Platform;
using CodexResetWidget.Presentation.ViewModels;

namespace CodexResetWidget.Presentation.Views;

public partial class MainWindow : Window
{
    public MainViewModel Model { get; }
    public ThemeService Theme { get; }
    public ScrollViewer ContentViewport => ContentScroll;
    private readonly DispatcherTimer _timer;
    private double _expandedHeight;
    private double _expandedScroll;
    private int _ticks;

    public MainWindow(MainViewModel model, ThemeService theme)
    {
        Model = model; Theme = theme;
        InitializeComponent(); DataContext = model;
        Height = Math.Min(1020, SystemParameters.WorkArea.Height - 28);
        _expandedHeight = Height;
        Width = Math.Min(440, SystemParameters.WorkArea.Width - 20);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            Model.Tick();
            if (++_ticks % 15 == 0) Model.RefreshSystemTimeZone();
        }, Dispatcher);
        Model.ModeChanged += ModeChanged;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) _timer.Stop(); else { Model.RefreshSystemTimeZone(); Model.Tick(); _timer.Start(); } };
        Activated += (_, _) => { Model.RefreshSystemTimeZone(); Model.Tick(); };
        SystemEvents.TimeChanged += TimeChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        Closed += (_, _) =>
        {
            _timer.Stop(); Model.ModeChanged -= ModeChanged;
            SystemEvents.TimeChanged -= TimeChanged; SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.UserPreferenceChanged -= PreferenceChanged; Model.Dispose();
        };
    }

    private void ModeChanged(object? sender, EventArgs e)
    {
        if (Model.IsCompact)
        {
            _expandedHeight = ActualHeight; _expandedScroll = ContentScroll.VerticalOffset;
            Height = Math.Min(Model.ToolsOpen ? 380 : 330, SystemParameters.WorkArea.Height - 28);
            ContentScroll.ScrollToTop();
        }
        else
        {
            Height = Math.Min(_expandedHeight, SystemParameters.WorkArea.Height - 28);
            Dispatcher.BeginInvoke(() => ContentScroll.ScrollToVerticalOffset(_expandedScroll), DispatcherPriority.Loaded);
        }
        Top = Math.Clamp(Top, SystemParameters.WorkArea.Top, Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - Height));
    }
    private void TimeChanged(object? sender, EventArgs e) => DispatchTimeUpdate();
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) { DispatchTimeUpdate(); Model.RefreshAfterResume(); } }
    private void DispatchTimeUpdate() => Dispatcher.BeginInvoke(() => { Model.RefreshSystemTimeZone(); Model.Tick(); });
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => { if (Theme.Mode == AppThemeMode.System) Theme.Apply(AppThemeMode.System); Model.RefreshSystemTimeZone(); });
    private void ShowMenu(object sender, RoutedEventArgs e) { MoreButton.ContextMenu.PlacementTarget = MoreButton; MoreButton.ContextMenu.IsOpen = true; }
    private void SelectTheme(object sender, RoutedEventArgs e) { if (sender is MenuItem { Tag: string tag } && Enum.TryParse<AppThemeMode>(tag, out var mode)) Theme.Apply(mode); }
    private void ToggleTools(object sender, RoutedEventArgs e) { if (Model.IsDemo) Model.ToolsOpen = !Model.ToolsOpen; }
    private void OpenDemo(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { Arguments = "--demo", UseShellExecute = true });
    private void OpenAnnouncement(object sender, RoutedEventArgs e)
    {
        if (Model.Announcement.CurrentEvent?.SourceUrl is { Scheme: "https" or "http" } url)
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
    private async void RunPrototypeChecks(object sender, RoutedEventArgs e) => await PrototypeChecks.RunAsync(this,
        System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "captures")));
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ExitWindow(object sender, RoutedEventArgs e) => Close();
    private void OpenSource(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://codex-resets.com/") { UseShellExecute = true });
}

