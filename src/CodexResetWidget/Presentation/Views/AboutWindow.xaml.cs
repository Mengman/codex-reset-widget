using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace CodexResetWidget.Presentation.Views;

public partial class AboutWindow : Window
{
    public const string GitHubUrl = "https://github.com/Mengman";
    public string DisplayVersion { get; } = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? typeof(App).Assembly.GetName().Version?.ToString(3) ?? L10n.Get("Common.Unknown");
    public AboutWindow()
    {
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(L10n.Culture.Name);
        RefreshLanguage(null, EventArgs.Empty);
        L10n.Changed += RefreshLanguage;
        Closed += (_, _) => L10n.Changed -= RefreshLanguage;
    }
    private void RefreshLanguage(object? sender, EventArgs e) => VersionLabel.Text = L10n.Format("About.Version", DisplayVersion);
    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void OpenGitHub(object sender, RoutedEventArgs e) => Open(GitHubUrl);
    private void OpenSource(object sender, RoutedEventArgs e) => Open("https://codex-resets.com/");
}
