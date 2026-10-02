using System.Net;
using static SyncTestData;
using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class JsonCacheStoreTests
{
    public static async Task<(int Passed, int Failed)> RunAsync()
    {
        var tests = new TestSuite("JsonCacheStore");
        var directory = Path.Combine(Path.GetTempPath(), "CodexResetWidget-tests-" + Guid.NewGuid().ToString("N"));
        await tests.CheckAsync("JSON cache round trip retains UTC, string ID and source", async () =>
        {
            var cache = new JsonCacheStore(directory);
            using var sync = new SyncController(new FakeProvider(), cache, new TestClock(Now)); await sync.InitializeAsync();
            var restored = await cache.LoadAsync(default);
            Assert(restored.Value!.Snapshot.Pending!.LastKnownEvent.Key.Id == "2105843926221660585");
            Assert(restored.Value.Snapshot.Pending.LastKnownEvent.ScheduledForUtc!.Value.Offset == TimeSpan.Zero);
            Assert(restored.Value.Snapshot.Pending.LastKnownEvent.SourceUrl!.Scheme == "https");
            Assert(!Directory.GetFiles(directory, "*.tmp").Any());
        });
        await tests.CheckAsync("Cancelled cache replacement preserves the prior valid file", async () =>
        {
            var cache = new JsonCacheStore(directory); var previous = await File.ReadAllTextAsync(cache.FilePath);
            var loaded = await cache.LoadAsync(default); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await cache.SaveAsync(loaded.Value!, cancel.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
            Assert(await File.ReadAllTextAsync(cache.FilePath) == previous && !Directory.GetFiles(directory, "*.tmp").Any());
        });
        await tests.CheckAsync("Corrupt cache is ignored without preventing startup", async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "snapshot.json"), "{broken");
            var result = await new JsonCacheStore(directory).LoadAsync(default); Assert(result.Value is null && result.Warning is not null);
        });
        await tests.CheckAsync("Unsupported cache schema is ignored", async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "snapshot.json"), "{\"schemaVersion\":99}");
            var result = await new JsonCacheStore(directory).LoadAsync(default); Assert(result.Value is null && result.Warning is not null);
        });
        await tests.CheckAsync("Structurally damaged JSON cache is ignored", async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "snapshot.json"), "{\"schemaVersion\":1,\"snapshot\":{\"history\":{\"events\":[]},\"statusHealth\":null,\"historyHealth\":null},\"documents\":{}}");
            var result = await new JsonCacheStore(directory).LoadAsync(default); Assert(result.Value is null && result.Warning is not null);
        });

        return tests.Result;
    }

}
