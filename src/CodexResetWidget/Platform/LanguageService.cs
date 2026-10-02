using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;

namespace CodexResetWidget.Platform;

public static class LanguageService
{
    [DllImport("kernel32.dll")] private static extern ushort GetUserDefaultUILanguage();
    public static void Initialize(LanguageMode mode)
    {
        L10n.Changed += (_, _) => UpdateResources();
        Apply(mode);
        UpdateResources();
    }
    public static void Apply(LanguageMode mode) => L10n.Apply(mode, CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()));
    public static void RefreshSystemLanguage() => Apply(L10n.Mode);
    private static void UpdateResources()
    {
        foreach (var key in L10n.Keys) System.Windows.Application.Current.Resources["Text." + key] = L10n.Get(key);
        foreach (Window window in System.Windows.Application.Current.Windows)
            window.Language = XmlLanguage.GetLanguage(L10n.Culture.Name);
    }
}
