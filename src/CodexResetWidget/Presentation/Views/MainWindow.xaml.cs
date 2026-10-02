using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform;
using CodexResetWidget.Presentation.ViewModels;

namespace CodexResetWidget.Presentation.Views;

public partial class MainWindow : Window
{
    public MainViewModel Model { get; }
    public ThemeService Theme { get; }
    public ScrollViewer ContentViewport => ContentScroll;
    public bool IsTicking => _timer.IsEnabled;
    public bool HasTray => _tray?.IsVisible == true;
    public bool BackdropEnabled { get; private set; }
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _saveTimer;
    private readonly SettingsStore? _store;
    private readonly WindowPlacementService _placement;
    private readonly bool _enableBackdrop;
    private DesktopSettings _settings;
    private TrayService? _tray;
    private bool _restoring = true;
    private bool _exiting;
    private double _expandedHeight;
    private double _expandedScroll;
    private int _ticks;

    public MainWindow(MainViewModel model, ThemeService theme, SettingsStore? store = null,
        DesktopSettings? settings = null, bool enableTray = false, bool enableBackdrop = false)
    {
        Model = model; Theme = theme; _store = store; _settings = settings ?? new(); _enableBackdrop = enableBackdrop;
        InitializeComponent(); DataContext = model;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(L10n.Culture.Name);
        Model.RefreshLanguage(); UpdateLanguageMenu();
        L10n.Changed += LanguageChanged;
        _placement = new(this);
        var readingKey = Model.Announcement.CurrentEvent?.Key;
        Model.Announcement.PropertyChanged += AnnouncementChanged;
        void AnnouncementChanged(object? sender, PropertyChangedEventArgs args)
        {
            var key = Model.Announcement.CurrentEvent?.Key;
            if (key != readingKey) { readingKey = key; AnnouncementScroll.ScrollToTop(); }
        }
        Closed += (_, _) => Model.Announcement.PropertyChanged -= AnnouncementChanged;
        Width = _settings.Width; _expandedHeight = _settings.ExpandedHeight;
        Height = model.IsCompact ? CompactHeight : _expandedHeight;
        if (_settings.PhysicalLeft is not null) WindowStartupLocation = WindowStartupLocation.Manual;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            Model.Tick();
            if (++_ticks % 15 == 0) Model.RefreshSystemTimeZone();
        }, Dispatcher);
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) =>
        {
            _saveTimer?.Stop(); SaveSettings();
        }, Dispatcher);
        SourceInitialized += (_, _) =>
        {
            _placement.Restore(_settings);
            if (Model.IsCompact) Height = CompactHeight;
            RefreshBackdrop();
            if (enableTray)
                _tray = new TrayService((System.Windows.Media.ImageSource)FindResource("AppLogo"), ShowFromTray,
                    HideToTray, () => Model.RefreshCommand.Execute(null), ExitApplication);
            UpdateNotice();
        };
        Loaded += (_, _) => { _restoring = false; _placement.EnsureVisible(); UpdateTimer(); };
        Model.ModeChanged += ModeChanged;
        Model.PropertyChanged += ModelChanged;
        Theme.Changed += ThemeChanged;
        StateChanged += (_, _) => { UpdateTimer(); if (WindowState == WindowState.Normal) _placement.EnsureVisible(); };
        IsVisibleChanged += (_, _) => UpdateTimer();
        Activated += (_, _) => RefreshTime();
        LocationChanged += (_, _) => ScheduleSave();
        SizeChanged += (_, _) =>
        {
            if (!_restoring && !Model.IsCompact && WindowState == WindowState.Normal) _expandedHeight = Height;
            ScheduleSave();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(_placement.EnsureVisible, DispatcherPriority.Loaded);
        SystemEvents.TimeChanged += TimeChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closing += WindowClosing;
        Closed += (_, _) =>
        {
            _timer.Stop(); _saveTimer.Stop(); _tray?.Dispose();
            Model.ModeChanged -= ModeChanged; Model.PropertyChanged -= ModelChanged; Theme.Changed -= ThemeChanged;
            SystemEvents.TimeChanged -= TimeChanged; SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.UserPreferenceChanged -= PreferenceChanged; SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            Model.Dispose();
            L10n.Changed -= LanguageChanged;
        };
    }

    private const double CompactHeight = 292;
    private void UpdateTimer()
    {
        if (!IsVisible || WindowState == WindowState.Minimized) _timer.Stop();
        else { RefreshTime(); _timer.Start(); }
    }
    private void RefreshTime() { Model.RefreshSystemTimeZone(); Model.Tick(); }
    private void ModeChanged(object? sender, EventArgs e)
    {
        if (Model.IsCompact)
        {
            _expandedHeight = Height; _expandedScroll = ContentScroll.VerticalOffset;
            Height = CompactHeight; ContentScroll.ScrollToTop();
        }
        else
        {
            Height = _expandedHeight;
            Dispatcher.BeginInvoke(() => ContentScroll.ScrollToVerticalOffset(_expandedScroll), DispatcherPriority.Loaded);
        }
        _placement.EnsureVisible(); ScheduleSave();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsPinned) or nameof(MainViewModel.IsCompact)) ScheduleSave();

    }
    private void ScheduleSave()
    {
        if (_restoring || !IsLoaded || _exiting) return;
        if (WindowState == WindowState.Normal) _settings = CaptureSettings();
        _saveTimer.Stop(); _saveTimer.Start();
    }
    public DesktopSettings CaptureSettings()
    {
        var bounds = _placement.CurrentBounds();
        return _settings with { Width = Math.Clamp(ActualWidth, 370, 4000), ExpandedHeight = Math.Clamp(_expandedHeight, 260, 5000),
            Compact = Model.IsCompact, Pinned = Model.IsPinned, Theme = Theme.Mode.ToString(), Language = L10n.Mode.ToString(), Monitor = _placement.CurrentMonitor(),
            PhysicalLeft = bounds.X, PhysicalTop = bounds.Y };
    }
    public void SaveSettings()
    {
        if (_store is null || !IsLoaded) return;
        _settings = WindowState == WindowState.Normal ? CaptureSettings() : _settings with
            { Compact = Model.IsCompact, Pinned = Model.IsPinned, Theme = Theme.Mode.ToString(), Language = L10n.Mode.ToString() };
        _store.Save(_settings); UpdateNotice();
    }
    private void UpdateNotice() => DesktopNotice.Text = L10n.Message(_store?.Warning);
    public void HideToTray()
    {
        if (_tray is null) { WindowState = WindowState.Minimized; return; }
        SaveSettings();
        if (!_settings.TrayHintShown)
        {
            _tray.ExplainClose(); _settings = _settings with { TrayHintShown = true };
            _store?.Save(_settings); UpdateNotice();
        }
        Hide(); ShowInTaskbar = false;
    }
    public void ShowFromTray()
    {
        ShowInTaskbar = true; Show(); WindowState = WindowState.Normal;
        _placement.EnsureVisible(); Activate(); RefreshTime(); UpdateTimer();
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && _tray is not null) { e.Cancel = true; HideToTray(); }
        else SaveSettings();
    }
    public void PrepareForExit() { SaveSettings(); _exiting = true; }
    public void ExitApplication() { PrepareForExit(); Close(); System.Windows.Application.Current.Shutdown(); }
    public void RefreshBackdrop() => BackdropEnabled = _enableBackdrop && WindowBackdrop.Apply(this, Theme);
    public void EnsureOnScreen() => _placement.EnsureVisible();
    private void ThemeChanged(object? sender, EventArgs e) { RefreshBackdrop(); ScheduleSave(); }
    private void TimeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshTime);
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() => { RefreshTime(); Model.RefreshAfterResume(); }); }
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    { Theme.Apply(Theme.Mode); LanguageService.RefreshSystemLanguage(); RefreshTime(); });
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(_placement.EnsureVisible);
    private void ShowMenu(object sender, RoutedEventArgs e) { MoreButton.ContextMenu.PlacementTarget = MoreButton; MoreButton.ContextMenu.IsOpen = true; }
    private void SelectTheme(object sender, RoutedEventArgs e) { if (sender is MenuItem { Tag: string tag } && Enum.TryParse<AppThemeMode>(tag, out var mode)) Theme.Apply(mode); }
    private void SelectLanguage(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<LanguageMode>(tag, out var mode)) LanguageService.Apply(mode);
        UpdateLanguageMenu();
        MoreButton.ContextMenu.IsOpen = false;
    }
    private void UpdateLanguageMenu()
    {
        SystemLanguageItem.IsChecked = L10n.Mode == LanguageMode.System;
        EnglishLanguageItem.IsChecked = L10n.Mode == LanguageMode.English;
        ChineseLanguageItem.IsChecked = L10n.Mode == LanguageMode.SimplifiedChinese;
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        Model.RefreshLanguage(); UpdateLanguageMenu(); UpdateNotice(); ScheduleSave();
    }
    private void ShowAbout(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
    private void OpenAnnouncement(object sender, RoutedEventArgs e)
    {
        if (Model.Announcement.CurrentEvent?.SourceUrl is { Scheme: "https" or "http" } url)
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void HideWindow(object sender, RoutedEventArgs e) => HideToTray();
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void ExitWindow(object sender, RoutedEventArgs e) => ExitApplication();
    private void OpenSource(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://codex-resets.com/") { UseShellExecute = true });
}
