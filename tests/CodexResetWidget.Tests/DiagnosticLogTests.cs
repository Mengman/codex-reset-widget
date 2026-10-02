using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class DiagnosticLogTests
{
    public static async Task<(int Passed, int Failed)> RunAsync()
    {
        var tests = new TestSuite("DiagnosticLog");
        var directory = Path.Combine(Path.GetTempPath(), "CodexResetWidget-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await tests.CheckAsync("Diagnostic log rotates and bounds oversized messages", () =>
        {
            var logs = Path.Combine(directory, "logs");
            var logger = new DiagnosticLog(logs);
            for (var i = 0; i < 100; i++) logger.Write(new string('中', 20_000));
            var files = Directory.GetFiles(logs);
            Assert(files.Length == 2 && files.Sum(f => new FileInfo(f).Length) < 1_100_000);
            Assert(File.ReadAllLines(Path.Combine(logs, "sync.log")).All(line => line.Length < 4200));
            return Task.CompletedTask;
        });
        await tests.CheckAsync("Diagnostic messages cannot inject additional log lines", () =>
        {
            var logs = Path.Combine(directory, "single-line");
            new DiagnosticLog(logs).Write("first\r\nsecond\nthird");
            Assert(File.ReadAllLines(Path.Combine(logs, "sync.log")).Length == 1);
            return Task.CompletedTask;
        });
        await tests.CheckAsync("An unavailable diagnostic directory does not prevent execution", () =>
        {
            var file = Path.Combine(directory, "blocked"); File.WriteAllText(file, "x");
            new DiagnosticLog(file).Write("test");
            Assert(File.ReadAllText(file) == "x"); return Task.CompletedTask;
        });

        return tests.Result;
    }

}
