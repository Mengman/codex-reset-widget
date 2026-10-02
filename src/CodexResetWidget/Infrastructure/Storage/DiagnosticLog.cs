using System.IO;

namespace CodexResetWidget.Infrastructure.Storage;

public sealed class DiagnosticLog(string directory)
{
    private readonly object _gate = new();
    public void Write(string message)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "sync.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512_000)
                {
                    var previous = Path.Combine(directory, "sync.previous.log");
                    File.Move(path, previous, true);
                }
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
