using Microsoft.Win32;
using System.Windows;

namespace CodexResetWidget.Platform;

public enum AppThemeMode { System, Light, Dark }
public sealed class ThemeService
{
    public AppThemeMode Mode { get; private set; } = AppThemeMode.System;
    public bool IsDark { get; private set; }
    public bool IsHighContrast { get; private set; }
    public event EventHandler? Changed;
    public void Apply(AppThemeMode mode, bool? highContrastOverride = null)
    {
        Mode = mode;
        IsHighContrast = highContrastOverride ?? SystemParameters.HighContrast;
        IsDark = mode == AppThemeMode.Dark || mode == AppThemeMode.System && ReadSystemDark();
        var dictionary = new ResourceDictionary { Source = new Uri($"Resources/{(IsHighContrast ? "HighContrast" : IsDark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        System.Windows.Application.Current.Resources.MergedDictionaries[0] = dictionary;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private static bool ReadSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
