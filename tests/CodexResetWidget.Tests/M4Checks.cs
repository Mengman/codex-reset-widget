using System.Net;
using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;

internal static class M4Checks
{
    public static async Task<(int Passed, int Failed)> RunAsync()
    {
        var passed = 0; var failed = 0;
        var directory = Path.Combine(Path.GetTempPath(), "CodexResetWidget-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
        async Task Check(string name, Func<Task> run)
        {
            try { await run(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
        // Fixed schema-1 files are the on-disk upgrade contract, independent of current serialization.
        await Check("M3 settings preserve expanded choice and old window size on upgrade", () =>
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), """
                {"schemaVersion":1,"width":440,"expandedHeight":1020,"compact":false,"pinned":true,
                 "theme":"Light","monitor":"old-monitor","physicalLeft":-1500,"physicalTop":120,"trayHintShown":true}
                """);
            var settings = new SettingsStore(directory).Load();
            Assert(!settings.Compact && settings.Pinned && settings.Width == 440 && settings.ExpandedHeight == 1020
                && settings.Theme == "Light" && settings.PhysicalLeft == -1500 && settings.TrayHintShown);
            return Task.CompletedTask;
        });
        await Check("Schema-1 cache survives upgrade and offline startup after expected time", async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "snapshot.json"), """
                {"schemaVersion":1,"snapshot":{"revision":44,"status":null,
                 "history":{"events":[],"isComplete":true,"lastFullSyncAtUtc":"2026-10-02T08:00:00Z"},
                 "pending":{"lastKnownEvent":{"key":{"provider":"codex-resets","id":"m3-upgrade-contract"},
                    "type":0,"status":0,"sourceKind":0,"announcedAtUtc":"2026-10-02T02:14:51Z",
                    "scheduledForUtc":"2026-10-02T17:00:00Z","text":"Upgrade fixture"},
                    "missingFromLatestStatus":true,"targetChanged":false},
                 "statusHealth":{"lastSuccessAtUtc":"2026-10-02T08:00:00Z"},
                 "historyHealth":{"lastSuccessAtUtc":"2026-10-02T08:00:00Z"}},"documents":{}}
                """);
            var cache = new JsonCacheStore(directory);
            var original = await cache.LoadAsync(default);
            Assert(original.Value is not null && original.Warning is null);
            using var http = new HttpClient(new OfflineHandler());
            var clock = new FixedClock(DateTimeOffset.Parse("2026-10-03T01:00:00Z"));
            using var sync = new SyncController(new CodexResetsClient(http), cache, clock);
            await sync.InitializeAsync();
            var board = new ResetStateService().Evaluate(sync.Current, clock.UtcNow);
            Assert(sync.Current.StatusHealth.IsFromCache && sync.Current.StatusHealth.LastError is not null
                && board.Status == BoardStatus.RecentAnnouncement
                && board.RelatedEvent?.Key.Id == "m3-upgrade-contract");
        });
        await Check("Diagnostic log rotates and bounds oversized messages", () =>
        {
            var logs = Path.Combine(directory, "logs");
            var logger = new DiagnosticLog(logs);
            for (var i = 0; i < 100; i++) logger.Write(new string('中', 20_000));
            var files = Directory.GetFiles(logs);
            Assert(files.Length == 2 && files.Sum(f => new FileInfo(f).Length) < 1_100_000);
            Assert(File.ReadAllLines(Path.Combine(logs, "sync.log")).All(line => line.Length < 4200));
            return Task.CompletedTask;
        });
        await Check("Diagnostic messages cannot inject additional log lines", () =>
        {
            var logs = Path.Combine(directory, "single-line");
            new DiagnosticLog(logs).Write("first\r\nsecond\nthird");
            Assert(File.ReadAllLines(Path.Combine(logs, "sync.log")).Length == 1);
            return Task.CompletedTask;
        });
        await Check("An unavailable diagnostic directory does not prevent execution", () =>
        {
            var file = Path.Combine(directory, "blocked"); File.WriteAllText(file, "x");
            new DiagnosticLog(file).Write("test");
            Assert(File.ReadAllText(file) == "x"); return Task.CompletedTask;
        });
        return (passed, failed);
    }
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
