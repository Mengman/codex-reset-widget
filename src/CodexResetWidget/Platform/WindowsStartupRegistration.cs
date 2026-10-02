using CodexResetWidget.Domain;
using Microsoft.Win32;

namespace CodexResetWidget.Platform;

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    public const string ValueName = "CodexResetWidget";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }
    public bool IsDisabledByWindows
    {
        get
        {
            // Respect a Task Manager / Windows Settings opt-out. Never change the approval record.
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } state && state[0] is 3 or 7;
        }
    }
    public void WriteCommand(string? command)
    {
        if (command is null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            existing?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
    }
}
