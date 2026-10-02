using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexResetWidget.Application;
using CodexResetWidget.Platform;
using CodexResetWidget.Domain;
using CodexResetWidget.Presentation.ViewModels;

namespace CodexResetWidget.Presentation.Views;

public static class LiveChecks
{
    public static async Task RunAsync(MainWindow window, SyncController sync, string directory)
    {
        Directory.CreateDirectory(directory);
        var startup = new { Compact = window.Model.IsCompact, Pinned = window.Model.IsPinned,
            Theme = window.Theme.Mode.ToString(), Language = L10n.Mode.ToString(), Locale = L10n.Locale, window.Width, window.Height };
        window.Model.ApplySnapshot(sync.Current);
        foreach (var theme in new[] { AppThemeMode.Light, AppThemeMode.Dark })
        {
            window.Theme.Apply(theme);
            // WPF export cannot capture the native compositor's Mica surface.
            window.SetResourceReference(System.Windows.Window.BackgroundProperty, "WindowBrush");
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, "live-" + theme.ToString().ToLowerInvariant() + ".png")); encoder.Save(file);
        }
        var readingPreserved = false;
        if (sync.Current.History.Events.FirstOrDefault() is { } historical)
        {
            window.Model.SelectEventCommand.Execute(new EventRowViewModel(historical));
            var key = window.Model.Announcement.SelectedEvent;
            var month = window.Model.Calendar.VisibleMonth;
            await sync.RefreshAsync();
            window.Model.ApplySnapshot(sync.Current);
            readingPreserved = key == window.Model.Announcement.SelectedEvent && month == window.Model.Calendar.VisibleMonth;
            window.Model.ReturnLatestCommand.Execute(null);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "live-checks.json"), JsonSerializer.Serialize(new
        {
            StatusLoaded = sync.Current.Status is not null,
            Startup = startup, window.Model.IsDemo,
            RuntimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            ProcessPath = Environment.ProcessPath,
            HistoryCount = sync.Current.History.Events.Count,
            HistoryComplete = sync.Current.History.IsComplete,
            ReadingPreservedAfterRefresh = readingPreserved,
            sync.Current.StatusHealth, sync.Current.HistoryHealth, sync.CacheWarning,
            Board = window.Model.Board.Title, ExpectedTime = window.Model.Board.MainDate,
            Zone = window.Model.Board.ZoneLabel, Source = window.Model.Announcement.CurrentEvent?.SourceUrl,
            AnnouncementId = window.Model.Announcement.CurrentEvent?.Key.Id
        }, new JsonSerializerOptions { WriteIndented = true }));
        System.Windows.Application.Current.Shutdown(sync.Current.Status is null || sync.Current.History.Events.Count == 0 || !readingPreserved ? 1 : 0);
    }
}
