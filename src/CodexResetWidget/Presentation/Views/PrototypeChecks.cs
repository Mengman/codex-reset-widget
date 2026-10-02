using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexResetWidget.Domain;
using CodexResetWidget.Platform;

namespace CodexResetWidget.Presentation.Views;

// Explicit development entry point: exercise bindings in a real WPF window and export its rendered content.
public static class PrototypeChecks
{
    public static async Task RunAsync(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        var bindings = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindings);
        try
        {
            await Idle(window);
            var vm = window.Model;
            if (vm.IsCompact) vm.ToggleModeCommand.Execute(null);
            vm.ToolsOpen = false;
            vm.SelectedZone = vm.Zones[0];
            vm.SelectedScenario = vm.Scenarios.Single(s => s.Id == "future");
            window.ContentViewport.ScrollToTop();
            void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); checks.Add(message); }
            async Task Capture(string name)
            {
                await Idle(window);
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
            }
            window.Theme.Apply(AppThemeMode.Light);
            Check(vm.Board.HasCountdown, "Future announcement shows countdown");
            await Capture("light-expanded-future");
            window.Theme.Apply(AppThemeMode.Dark);
            await Capture("dark-expanded-future");
            vm.SelectedScenario = vm.Scenarios.Single(s => s.Id == "multi");
            vm.TodayCommand.Execute(null);
            Check(vm.Calendar.Events.Count >= 3, "Same-day events remain individually selectable");
            vm.SelectEventCommand.Execute(vm.Calendar.Events[0]);
            var key = vm.Announcement.SelectedEvent;
            var month = vm.Calendar.VisibleMonth;
            vm.ToggleModeCommand.Execute(null);
            Check(vm.IsCompact && vm.Announcement.SelectedEvent == key, "Compact mode preserves historical selection");
            await Capture("dark-compact-future");
            vm.ToggleModeCommand.Execute(null);
            Check(vm.IsExpanded && vm.Calendar.VisibleMonth == month, "Expanded mode preserves visible month");
            vm.SelectedZone = vm.Zones.Single(z => z.Id == "India Standard Time");
            Check(vm.Announcement.SelectedEvent == key && vm.Calendar.VisibleMonth == month, "Time-zone change preserves event and month");
            Check(vm.Board.ZoneLabel.Contains("05:30"), "Half-hour offset appears in board label");
            await Capture("dark-expanded-india");
            vm.SelectedZone = vm.Zones[0];
            vm.ReturnLatestCommand.Execute(null);
            foreach (var id in new[] { "past", "unknown", "watch", "none", "missing", "cache", "loading", "error", "long", "multi" })
            {
                vm.SelectedScenario = vm.Scenarios.Single(s => s.Id == id);
                vm.TodayCommand.Execute(null);
                await Capture("dark-expanded-" + id);
                if (id == "past")
                {
                    Check(vm.Board.ShowsDate && !vm.Board.HasCountdown, "At target time the date replaces countdown");
                    Check(vm.Board.ShowsDate && window.FindName("CountdownCard") is System.Windows.Controls.Border, "Past announcement retains the board and its permanent account clarification");
                    vm.ToggleModeCommand.Execute(null); await Capture("dark-compact-past"); vm.ToggleModeCommand.Execute(null);
                }
                if (id == "missing") Check(vm.Board.Notice.Contains("待确认") && vm.Board.ShowsDate, "Missing announcement retains its expected date");
            }
            window.ContentViewport.ScrollToEnd();
            await Capture("dark-expanded-calendar");
            var normalHeight = window.Height;
            window.Width = 370; window.Height = 620;
            window.ContentViewport.ScrollToTop();
            await Capture("dark-small-window-top");
            Check(window.ContentViewport.ScrollableHeight > 0, "Small window exposes overflow through scrolling");
            window.ContentViewport.ScrollToEnd();
            await Capture("dark-small-window-calendar");
            Check(window.ContentViewport.VerticalOffset > 0, "Small window can reach calendar and daily details");
            window.Width = 400; window.Height = normalHeight;
            window.ContentViewport.ScrollToTop();
            vm.SelectedScenario = vm.Scenarios.Single(s => s.Id == "arriving");
            await Task.Delay(TimeSpan.FromSeconds(13));
            vm.Tick();
            Check(vm.Board.ShowsDate && !vm.Board.HasCountdown, "Live timer crosses target without showing completion");
            await Capture("dark-expanded-after-live-target");
            Check(bindings.Errors.Count == 0, "No WPF binding errors during scenario matrix");
            await File.WriteAllTextAsync(Path.Combine(directory, "ui-checks.json"), JsonSerializer.Serialize(new
            {
                Passed = true, Checks = checks, BindingErrors = bindings.Errors,
                Width = window.ActualWidth, Height = window.ActualHeight,
                Dpi = VisualTreeHelper.GetDpi(window).PixelsPerInchX
            }, new JsonSerializerOptions { WriteIndented = true }));
            System.Windows.Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "ui-checks.json"), JsonSerializer.Serialize(new { Passed = false, Checks = checks, Error = error.ToString(), BindingErrors = bindings.Errors }, new JsonSerializerOptions { WriteIndented = true }));
            System.Windows.Application.Current.Shutdown(1);
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(bindings); }
    }
    private static async Task Idle(Window window)
    {
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        await Task.Delay(100);
    }
    private sealed class BindingErrors : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (message?.Contains("Error", StringComparison.OrdinalIgnoreCase) == true) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
