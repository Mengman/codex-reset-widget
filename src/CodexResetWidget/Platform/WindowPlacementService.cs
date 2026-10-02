using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CodexResetWidget.Domain;
using Forms = System.Windows.Forms;

namespace CodexResetWidget.Platform;

public sealed class WindowPlacementService(Window window)
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(System.Drawing.Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    private nint Handle => new WindowInteropHelper(window).Handle;
    public IReadOnlyList<MonitorArea> Monitors => Forms.Screen.AllScreens.Select(screen =>
    {
        var work = screen.WorkingArea;
        var monitor = MonitorFromPoint(new System.Drawing.Point(work.Left + 1, work.Top + 1), 2);
        var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96d : VisualTreeHelper.GetDpi(window).DpiScaleX;
        return new MonitorArea(screen.DeviceName, new(work.Left, work.Top, work.Width, work.Height), scale);
    }).ToArray();
    public void Restore(DesktopSettings settings)
    {
        var (_, bounds) = PlacementPolicy.Restore(settings, Monitors);
        Apply(bounds);
    }
    public DesktopRect CurrentBounds()
    {
        if (!GetWindowRect(Handle, out var rect)) return new(0, 0, window.Width, window.Height);
        return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    public void EnsureVisible()
    {
        if (window.WindowState != WindowState.Normal) return;
        var bounds = CurrentBounds();
        var screen = Forms.Screen.FromRectangle(new((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height));
        var work = screen.WorkingArea;
        Apply(PlacementPolicy.Fit(bounds, new(work.Left, work.Top, work.Width, work.Height)));
    }
    public string CurrentMonitor()
    {
        var bounds = CurrentBounds();
        return Forms.Screen.FromRectangle(new((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height)).DeviceName;
    }
    private void Apply(DesktopRect rect) => SetWindowPos(Handle, 0, (int)Math.Round(rect.X), (int)Math.Round(rect.Y),
        (int)Math.Round(rect.Width), (int)Math.Round(rect.Height), 0x0014); // NOZORDER | NOACTIVATE
}
