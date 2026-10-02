using System.IO;
using System.Security;
using CodexResetWidget.Domain;

namespace CodexResetWidget.Application;

public sealed class StartupService(IStartupRegistration registration, string executablePath)
{
    public bool IsEnabled { get; private set; }
    public string? Warning { get; private set; }
    public void Refresh()
    {
        try
        {
            var command = registration.ReadCommand();
            IsEnabled = !string.IsNullOrWhiteSpace(command) && !registration.IsDisabledByWindows;
            Warning = IsEnabled && !string.Equals(command, CommandFor(executablePath), StringComparison.OrdinalIgnoreCase)
                ? "Startup.OtherCopy" : null;
        }
        catch (Exception error) when (IsStorageError(error))
        { IsEnabled = false; Warning = "Startup.ReadFailed"; }
    }
    // Only an explicit user action calls this method; loading the app never registers startup.
    public bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled && registration.IsDisabledByWindows)
            { Refresh(); Warning = "Startup.DisabledByWindows"; return false; }
            registration.WriteCommand(enabled ? CommandFor(executablePath) : null);
            Refresh();
            return IsEnabled == enabled && Warning is null;
        }
        catch (Exception error) when (IsStorageError(error))
        { Refresh(); Warning = "Startup.WriteFailed"; return false; }
    }
    public static string CommandFor(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"') || path.Contains('\0') || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Startup requires an absolute executable path.", nameof(path));
        var command = '"' + Path.GetFullPath(path) + '"';
        if (command.Length > 260) throw new ArgumentException("Startup command exceeds the Windows Run key limit.", nameof(path));
        return command;
    }
    private static bool IsStorageError(Exception error) => error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException;
}
