using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

namespace CodexResetWidget.Platform;

public static class WindowBackdrop
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    public static bool Apply(Window window, ThemeService theme)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return false;
        var dark = theme.IsDark ? 1 : 0;
        var rounded = 2;
        DwmSetWindowAttribute(handle, 20, ref dark, 4);
        DwmSetWindowAttribute(handle, 33, ref rounded, 4);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var transparency = key?.GetValue("EnableTransparency") is not int value || value != 0;
        var enable = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && transparency && !theme.IsHighContrast;
        var backdrop = enable ? 2 : 1; // MAINWINDOW (Mica) / NONE
        var accepted = DwmSetWindowAttribute(handle, 38, ref backdrop, 4) == 0 && enable;
        var chrome = WindowChrome.GetWindowChrome(window);
        chrome.GlassFrameThickness = accepted ? new Thickness(-1) : new Thickness(0);
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
            target.BackgroundColor = accepted ? Colors.Transparent : theme.IsDark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243);
        if (accepted) window.Background = Brushes.Transparent;
        else window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        return accepted;
    }
}
