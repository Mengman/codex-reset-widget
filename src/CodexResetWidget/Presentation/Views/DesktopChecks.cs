using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform;

namespace CodexResetWidget.Presentation.Views;

public static class DesktopChecks
{
    public static async Task RunAsync(MainWindow window, SettingsStore store, string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        string? failure = null;
        var micaAccepted = false;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        async Task Idle() { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(100); }
        async Task Capture(string name)
        {
            // RenderTargetBitmap cannot capture the OS compositor's backdrop; use the fallback surface for exported images.
            window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
            await Idle();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
        }
        var vm = window.Model;
        try
        {
            await Idle();
            if (vm.IsCompact) vm.ToggleModeCommand.Execute(null);
            vm.ToolsOpen = false; vm.SelectedScenario = vm.Scenarios.Single(s => s.Id == "future");
            window.Theme.Apply(AppThemeMode.Dark);
            var expanded = window.Height;
            Check(window.HasTray, "Tray icon exists while window is shown");
            Check(window.IsTicking, "Visible window runs its display timer");
            var current = vm.Board.State.RelatedEvent!.Key;
            vm.OlderAnnouncementCommand.Execute(null);
            var historical = vm.Announcement.SelectedEvent;
            Check(historical is not null && vm.Board.State.RelatedEvent.Key == current, "Older announcement changes reading without changing the board");
            vm.NewerAnnouncementCommand.Execute(null);
            Check(vm.Announcement.CurrentEvent!.Key == current, "Newer announcement returns through chronological history");
            vm.OlderAnnouncementCommand.Execute(null);
            historical = vm.Announcement.SelectedEvent;
            var month = vm.Calendar.VisibleMonth;
            window.ContentViewport.ScrollToVerticalOffset(180); await Idle();
            var readingOffset = window.ContentViewport.VerticalOffset;
            vm.IsPinned = true; vm.ToggleModeCommand.Execute(null); await Idle();
            Check(window.FindName("PinButton") is System.Windows.Controls.Button pin && ReferenceEquals(pin.Background, window.FindResource("AccentSoftBrush")),
                "Pinned state has a visible indicator");
            window.SaveSettings(); var saved = store.Load();
            Check(saved.Compact && saved.Pinned && saved.Theme == "Dark", "Mode pin and theme persist to settings");
            Check(Math.Abs(saved.ExpandedHeight - expanded) < 2, "Compact mode preserves expanded height");
            vm.ToggleModeCommand.Execute(null); await Idle();
            Check(vm.Announcement.SelectedEvent == historical && vm.Calendar.VisibleMonth == month
                && Math.Abs(window.ContentViewport.VerticalOffset - readingOffset) < 2, "Expansion restores reading month and scroll position");
            var remaining = vm.Board.State.Remaining;
            vm.SelectedZone = vm.Zones.Single(z => z.Id == "India Standard Time");
            Check(vm.Announcement.SelectedEvent == historical && Math.Abs((vm.Board.State.Remaining - remaining).TotalSeconds) < 2,
                "Zone change preserves reading and absolute countdown");
            vm.SelectedZone = vm.Zones[0];
            window.Theme.Apply(AppThemeMode.Light); await Capture("desktop-light");
            Check(vm.Announcement.SelectedEvent == historical && vm.Calendar.VisibleMonth == month, "Theme switch preserves reading and month");
            window.Theme.Apply(AppThemeMode.Dark);
            micaAccepted = WindowBackdrop.Apply(window, window.Theme);
            window.Theme.Apply(AppThemeMode.Dark, true);
            Check(window.Theme.IsHighContrast && !WindowBackdrop.Apply(window, window.Theme), "High contrast uses system colors and disables backdrop");
            await Capture("desktop-high-contrast");
            window.Theme.Apply(AppThemeMode.Dark);
            vm.ReturnLatestCommand.Execute(null); window.ContentViewport.ScrollToTop();
            await Capture("desktop-dark");
            foreach (var (scale, height) in new[] { (100, 1000d), (150, 680d), (200, 500d) })
            {
                window.Width = 440; window.Height = height; await Idle();
                window.ContentViewport.ScrollToEnd(); await Idle();
                Check(window.ContentViewport.VerticalOffset <= window.ContentViewport.ScrollableHeight + 1,
                    $"Layout remains scrollable in the work area equivalent to {scale}% scaling");
            }
            await Capture("desktop-small-calendar");
            window.Height = expanded;
            vm.Calendar.SetMonth(new DateOnly(2026, 8, 1)); vm.RefreshSystemTimeZone();
            // Changing a zone triggers a calendar rebuild without changing the chosen month.
            vm.SelectedZone = vm.Zones[^1]; vm.SelectedZone = vm.Zones[0];
            Check(vm.Calendar.Days.Count == 42, "Six-row month keeps all date cells");
            window.ContentViewport.ScrollToEnd(); await Capture("desktop-six-row-calendar");
            var placement = new WindowPlacementService(window);
            placement.Restore(window.CaptureSettings() with { Monitor = "removed-monitor", PhysicalLeft = 999999, PhysicalTop = -999999 });
            await Idle(); window.EnsureOnScreen();
            var bounds = placement.CurrentBounds();
            Check(placement.Monitors.Any(m => bounds.X >= m.WorkArea.X - 1 && bounds.Y >= m.WorkArea.Y - 1
                && bounds.X + bounds.Width <= m.WorkArea.X + m.WorkArea.Width + 1
                && bounds.Y + bounds.Height <= m.WorkArea.Y + m.WorkArea.Height + 1), "Missing monitor restores the window within an available work area");
            window.SaveSettings();
            Check(store.Load().PhysicalLeft is not null && store.Load().Monitor is not null, "Window position and monitor persist");
            window.WindowState = WindowState.Minimized; await Idle();
            Check(!window.IsTicking, "Minimizing stops the display timer");
            window.WindowState = WindowState.Normal; await Idle();
            Check(window.IsTicking, "Restoring starts the display timer");
            vm.SelectedScenario = vm.Scenarios.Single(s => s.Id == "arriving");
            window.Close(); await Idle();
            Check(!window.IsVisible && window.HasTray && !window.IsTicking, "Close hides to tray and stops the display timer");
            Check(store.Load().TrayHintShown, "First close records the tray explanation");
            await Task.Delay(TimeSpan.FromSeconds(13));
            window.ShowFromTray(); await Idle();
            Check(window.IsVisible && window.IsTicking && vm.Board.ShowsDate, "Tray restore recomputes time after crossing the expected target");
            Check(window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), "Window exposes a keyboard focus target");
        }
        catch (Exception error) { failure = error.ToString(); }
        var monitors = new WindowPlacementService(window).Monitors;
        window.PrepareForExit(); window.Close();
        if (failure is null)
        {
            try { Check(!window.HasTray && !window.IsTicking, "Explicit exit removes tray icon and stops timers"); }
            catch (Exception error) { failure = error.ToString(); }
        }
        File.WriteAllText(Path.Combine(directory, "desktop-checks.json"), JsonSerializer.Serialize(new
        {
            Passed = failure is null, Checks = checks, Error = failure, MicaAcceptedByDwm = micaAccepted,
            HostDpi = VisualTreeHelper.GetDpi(window).PixelsPerInchX, MonitorCount = monitors.Count,
            Note = "100/150/200 percent work areas are layout simulations; OS scaling and display disconnection are not changed."
        }, new JsonSerializerOptions { WriteIndented = true }));
        System.Windows.Application.Current.Shutdown(failure is null ? 0 : 1);
    }
}
