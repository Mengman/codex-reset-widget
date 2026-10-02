namespace CodexResetWidget.Domain;

public interface IStartupRegistration
{
    string? ReadCommand();
    bool IsDisabledByWindows { get; }
    void WriteCommand(string? command);
}
