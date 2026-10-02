using System.Net;
using System.Text.Json;
using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;

internal static class M2Checks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-02T15:00:00Z");
    private static string EventJson(string id = "2105843926221660585", string type = "regular") =>
        $$$"""{"id":"{{{id}}}","status":"scheduled","reset_type":"{{{type}}}","announced_at":"2026-10-02T02:14:51Z","scheduled_for":"2026-10-02T17:00:00Z","text":"Sample","source":{"type":"x_post","author":"thsottiaux","url":"https://x.com/thsottiaux/status/{{{id}}}"}}""";
    private static string Status(string? reset = null) => "{\"data\":{\"scheduled_reset\":" + (reset ?? EventJson()) + ",\"latest_reset\":null,\"active_watch\":null},\"meta\":{\"generated_at\":\"2026-10-02T15:00:00Z\"}}";
    private static string Page(string data, bool more = false, string? cursor = null) =>
        "{\"data\":[" + data + "],\"pagination\":{\"has_more\":" + (more ? "true" : "false") + ",\"next_cursor\":" + JsonSerializer.Serialize(cursor) + "}}";
    private static HttpResponseMessage Ok(string body, string? tag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        if (tag is not null) response.Headers.TryAddWithoutValidation("ETag", tag);
        return response;
    }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    public static async Task<(int Passed, int Failed)> RunAsync()
    {
        var passed = 0; var failed = 0;
        async Task Check(string name, Func<Task> run)
        {
            try { await run(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
        async Task Rejected(string body)
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(body))));
            try { await new CodexResetsClient(http).GetStatusAsync(Now, default); throw new Exception("Accepted invalid response"); }
            catch (ApiException) { }
        }
        await Check("Adapter preserves large string IDs and UTC targets", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Status()))));
            var result = await new CodexResetsClient(http).GetStatusAsync(Now, default);
            Assert(result.ScheduledReset!.Key.Id == "2105843926221660585" && result.ScheduledReset.Author == "Tibo");
            Assert(result.ScheduledReset.ScheduledForUtc == Now.AddHours(2));
        });
        await Check("Adapter retains unknown types and ignores additional fields", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Status(EventJson(type: "new_type")).Replace("\"data\":", "\"new_field\":42,\"data\":")))));
            var result = await new CodexResetsClient(http).GetStatusAsync(Now, default);
            Assert(result.ScheduledReset!.Type == ResetType.Unknown && result.ScheduledReset.RawType == "new_type");
        });
        await Check("Missing offset is rejected rather than guessed", () => Rejected(Status().Replace("2026-10-02T17:00:00Z", "2026-10-02T17:00:00")));
        await Check("Missing scheduled field is not treated as withdrawal", () => Rejected("{\"data\":{\"latest_reset\":null}}"));
        await Check("Unknown scheduled status is rejected", () => Rejected(Status().Replace("\"scheduled\"", "\"done\"")));
        await Check("Observed source keeps URL but never invents author", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Page(EventJson().Replace("\"x_post\"", "\"observed\""))))));
            var reset = (await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey>(), true, default)).Events[0];
            Assert(reset.SourceKind == SourceKind.Observed && reset.Author is null && reset.SourceUrl is not null);
        });
        await Check("304 reuses exact URL body and conditional header", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new Handler((req, _) =>
            {
                if (++calls == 1) return Task.FromResult(Ok(Status(), "\"v1\""));
                Assert(req.Headers.IfNoneMatch.Single().Tag == "\"v1\"" && req.Headers.CacheControl!.NoCache);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }));
            var client = new CodexResetsClient(http);
            var first = await client.GetStatusAsync(Now, default); var next = await client.GetStatusAsync(Now.AddMinutes(5), default);
            Assert(first.ScheduledReset == next.ScheduledReset && next.LastCheckedAtUtc == Now.AddMinutes(5));
        });
        await Check("304 without body retries unconditionally", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new Handler((req, _) => { Assert(req.Headers.IfNoneMatch.Count == 0); return Task.FromResult(++calls == 1 ? new HttpResponseMessage(HttpStatusCode.NotModified) : Ok(Status())); }));
            Assert((await new CodexResetsClient(http).GetStatusAsync(Now, default)).ScheduledReset is not null && calls == 2);
        });
        await Check("HTTP 429 parses Retry-After seconds", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
                return Task.FromResult(response);
            }));
            try { await new CodexResetsClient(http).GetStatusAsync(Now, default); throw new Exception("Expected limit"); }
            catch (ApiException error) { Assert(error.RetryAfter == TimeSpan.FromSeconds(90)); }
        });
        await Check("Cancellation reaches the HTTP request", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancel = new CancellationTokenSource();
            using var http = new HttpClient(new Handler(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Ok(Status()); }));
            var pending = new CodexResetsClient(http).GetStatusAsync(Now, cancel.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancel.Cancel();
            try { await pending; throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
        });
        await Check("Conditional response bodies survive a client restart", async () =>
        {
            using var firstHttp = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Status(), "\"persisted\""))));
            var first = new CodexResetsClient(firstHttp); await first.GetStatusAsync(Now, default);
            using var nextHttp = new HttpClient(new Handler((req, _) => { Assert(req.Headers.IfNoneMatch.Single().Tag == "\"persisted\""); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)); }));
            var next = new CodexResetsClient(nextHttp); next.RestoreDocuments(first.ExportDocuments());
            Assert((await next.GetStatusAsync(Now.AddMinutes(5), default)).ScheduledReset!.Key.Id == "2105843926221660585");
        });
        await Check("Sequential pagination deduplicates records", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new Handler((req, _) =>
            {
                calls++; if (calls == 2) Assert(req.RequestUri!.Query.EndsWith("cursor=abc"));
                return Task.FromResult(Ok(calls == 1 ? Page(EventJson("a"), true, "abc") : Page(EventJson("a") + "," + EventJson("b"))));
            }));
            var result = await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey>(), true, default);
            Assert(result.Complete && result.Events.Count == 2 && calls == 2);
        });
        await Check("Incremental history stops at known records", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Ok(Page(EventJson("a"), true, "abc"))); }));
            var result = await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey> { new(CodexResetsClient.Provider, "a") }, false, default);
            Assert(!result.Complete && calls == 1);
        });
        await Check("Invalid history rows leave a visible incomplete warning", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Page(EventJson("a") + ",{\"id\":\"bad\"}")))));
            var result = await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey>(), true, default);
            Assert(result.Events.Count == 1 && !result.Complete && result.Warning is not null);
        });
        await Check("Partial pagination failure retains successful pages", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(++calls == 1 ? Ok(Page(EventJson("a"), true, "abc")) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
            try { await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey>(), true, default); throw new Exception("Expected partial failure"); }
            catch (PartialHistoryException error) { Assert(error.Partial.Events.Count == 1 && !error.Partial.Complete); }
        });
        await Check("Repeated cursor stops pagination", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Page(EventJson("a"), true, "abc")))));
            try { await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey>(), true, default); throw new Exception("Expected cursor error"); }
            catch (PartialHistoryException error) { Assert(error.Message.Contains("重复")); }
        });
        await Check("429 Retry-After remains binding on manual refresh", async () =>
        {
            var calls = 0; var clock = new TestClock(Now);
            var provider = new FakeProvider { Status = (_, _) => { calls++; throw new ApiException("限流", TimeSpan.FromMinutes(10)); } };
            using var sync = new SyncController(provider, new MemoryCache(), clock);
            await sync.InitializeAsync(); await sync.RefreshAsync(); Assert(calls == 1);
            clock.UtcNow = Now.AddMinutes(10); await sync.PollAsync(); Assert(calls == 2);
        });
        await Check("Ordinary failures back off until next scheduled attempt", async () =>
        {
            var calls = 0; var clock = new TestClock(Now);
            var provider = new FakeProvider { Status = (_, _) => { calls++; throw new HttpRequestException(); } };
            using var sync = new SyncController(provider, new MemoryCache(), clock);
            await sync.InitializeAsync(); clock.UtcNow = Now.AddMinutes(4); await sync.PollAsync(); Assert(calls == 1);
            clock.UtcNow = Now.AddMinutes(6); await sync.PollAsync(); Assert(calls == 2);
        });
        await Check("Independent schedules refresh status at 5 and history at 30 minutes", async () =>
        {
            var statuses = 0; var histories = 0; var clock = new TestClock(Now);
            var provider = new FakeProvider { Status = (at, _) => { statuses++; return Task.FromResult(Snapshot(at)); }, History = (_, _, _) => { histories++; return Task.FromResult(new HistoryResult([], true)); } };
            using var sync = new SyncController(provider, new MemoryCache(), clock);
            await sync.InitializeAsync(); clock.UtcNow = Now.AddMinutes(5); await sync.PollAsync(); Assert(statuses == 2 && histories == 1);
            clock.UtcNow = Now.AddMinutes(30); await sync.PollAsync(); Assert(histories == 2);
        });
        await Check("Withdrawal and corrections preserve pending context across refresh", async () =>
        {
            var next = Snapshot(Now); var provider = new FakeProvider { Status = (_, _) => Task.FromResult(next) };
            using var sync = new SyncController(provider, new MemoryCache(), new TestClock(Now));
            await sync.InitializeAsync(); var target = sync.Current.Pending!.LastKnownEvent.ScheduledForUtc;
            next = next with { ScheduledReset = null }; await sync.RefreshAsync();
            Assert(sync.Current.Pending!.MissingFromLatestStatus && sync.Current.Pending.LastKnownEvent.ScheduledForUtc == target);
            next = Snapshot(Now) with { ScheduledReset = Snapshot(Now).ScheduledReset! with { ScheduledForUtc = target!.Value.AddHours(1) } };
            await sync.RefreshAsync(); Assert(sync.Current.Pending!.TargetChanged && !sync.Current.Pending.MissingFromLatestStatus);
        });
        await Check("Late response after cancellation cannot overwrite newer request", async () =>
        {
            var old = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
            var provider = new FakeProvider { Status = (at, _) => { if (++calls == 1) { entered.SetResult(); return old.Task; } return Task.FromResult(Snapshot(at) with { ScheduledReset = Snapshot(at).ScheduledReset! with { Text = "new" } }); } };
            using var sync = new SyncController(provider, new MemoryCache(), new TestClock(Now));
            var first = sync.RefreshAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second = sync.RefreshAsync(); old.SetResult(Snapshot(Now) with { ScheduledReset = Snapshot(Now).ScheduledReset! with { Text = "old" } });
            await Task.WhenAll(first, second); Assert(sync.Current.Status!.ScheduledReset!.Text == "new" && !sync.Current.StatusHealth.IsLoading);
        });
        await Check("First launch offline has no invented announcement", async () =>
        {
            using var sync = new SyncController(Offline(), new MemoryCache(), new TestClock(Now)); await sync.InitializeAsync();
            Assert(sync.Current.Status is null && sync.Current.StatusHealth.LastError is not null && !sync.Current.History.IsComplete);
        });
        await Check("Partial history refresh merges pages without deleting older records", async () =>
        {
            var calls = 0; var old = Snapshot(Now).ScheduledReset! with { Key = new(CodexResetsClient.Provider, "old"), Status = EventStatus.Recorded };
            var next = old with { Key = new(CodexResetsClient.Provider, "new") };
            var provider = new FakeProvider { History = (_, _, _) => ++calls == 1 ? Task.FromResult(new HistoryResult([old], true))
                : throw new PartialHistoryException(new([next], false), new ApiException("后续分页失败")) };
            using var sync = new SyncController(provider, new MemoryCache(), new TestClock(Now));
            await sync.InitializeAsync(); await sync.RefreshAsync();
            Assert(sync.Current.History.Events.Count == 2 && sync.Current.HistoryHealth.LastError is not null && sync.Current.Status is not null);
        });
        await Check("Disposal suppresses a late provider response", async () =>
        {
            var response = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new FakeProvider { Status = (_, _) => response.Task };
            var sync = new SyncController(provider, new MemoryCache(), new TestClock(Now));
            var request = sync.RefreshAsync(); sync.Dispose(); response.SetResult(Snapshot(Now)); await request;
            Assert(sync.Current.Status is null);
        });
        await Check("Offline restart restores pending target and complete history", async () =>
        {
            var cache = new MemoryCache();
            using (var sync = new SyncController(new FakeProvider(), cache, new TestClock(Now))) await sync.InitializeAsync();
            using var offline = new SyncController(Offline(), cache, new TestClock(Now.AddDays(1))); await offline.InitializeAsync();
            Assert(offline.Current.Status is not null && offline.Current.Pending is not null && offline.Current.StatusHealth.IsFromCache);
            Assert(new ResetStateService().Evaluate(offline.Current, Now.AddDays(1)).Status == BoardStatus.RecentAnnouncement);
        });
        await Check("Cache write failure leaves fetched data available", async () =>
        {
            using var sync = new SyncController(new FakeProvider(), new MemoryCache { FailWrites = true }, new TestClock(Now));
            await sync.InitializeAsync(); Assert(sync.Current.Status is not null && sync.CacheWarning is not null && sync.Current.StatusHealth.LastError is null);
        });
        await Check("Stale upstream generation is distinct from successful HTTP check", async () =>
        {
            var provider = new FakeProvider { Status = (at, _) => Task.FromResult(Snapshot(at) with { GeneratedAtUtc = at.AddHours(-2) }) };
            using var sync = new SyncController(provider, new MemoryCache(), new TestClock(Now)); await sync.InitializeAsync();
            Assert(sync.Current.StatusHealth.IsStale && sync.Current.StatusHealth.LastSuccessAtUtc == Now);
        });
        var directory = Path.Combine(Path.GetTempPath(), "CodexResetWidget-tests-" + Guid.NewGuid().ToString("N"));
        await Check("JSON cache round trip retains UTC, string ID and source", async () =>
        {
            var cache = new JsonCacheStore(directory);
            using var sync = new SyncController(new FakeProvider(), cache, new TestClock(Now)); await sync.InitializeAsync();
            var restored = await cache.LoadAsync(default);
            Assert(restored.Value!.Snapshot.Pending!.LastKnownEvent.Key.Id == "2105843926221660585");
            Assert(restored.Value.Snapshot.Pending.LastKnownEvent.ScheduledForUtc!.Value.Offset == TimeSpan.Zero);
            Assert(restored.Value.Snapshot.Pending.LastKnownEvent.SourceUrl!.Scheme == "https");
            Assert(!Directory.GetFiles(directory, "*.tmp").Any());
        });
        await Check("Cancelled cache replacement preserves the prior valid file", async () =>
        {
            var cache = new JsonCacheStore(directory); var previous = await File.ReadAllTextAsync(cache.FilePath);
            var loaded = await cache.LoadAsync(default); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await cache.SaveAsync(loaded.Value!, cancel.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
            Assert(await File.ReadAllTextAsync(cache.FilePath) == previous && !Directory.GetFiles(directory, "*.tmp").Any());
        });
        await Check("Corrupt cache is ignored without preventing startup", async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "snapshot.json"), "{broken");
            var result = await new JsonCacheStore(directory).LoadAsync(default); Assert(result.Value is null && result.Warning is not null);
        });
        await Check("Unsupported cache schema is ignored", async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "snapshot.json"), "{\"schemaVersion\":99}");
            var result = await new JsonCacheStore(directory).LoadAsync(default); Assert(result.Value is null && result.Warning is not null);
        });
        await Check("Structurally damaged JSON cache is ignored", async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "snapshot.json"), "{\"schemaVersion\":1,\"snapshot\":{\"history\":{\"events\":[]},\"statusHealth\":null,\"historyHealth\":null},\"documents\":{}}");
            var result = await new JsonCacheStore(directory).LoadAsync(default); Assert(result.Value is null && result.Warning is not null);
        });
        return (passed, failed);
    }
    private static ProviderSnapshot Snapshot(DateTimeOffset at) => new(CodexResetsClient.Normalize(JsonSerializer.Deserialize<ApiReset>(EventJson(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!, true), null, null, at, at);
    private static FakeProvider Offline() => new() { Status = (_, _) => throw new HttpRequestException(), History = (_, _, _) => throw new HttpRequestException() };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private sealed class TestClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; set; } = now; }
    private sealed class MemoryCache : ICacheStore
    {
        public bool FailWrites { get; init; }
        private CacheEnvelope? _value;
        public Task<CacheLoad> LoadAsync(CancellationToken token) => Task.FromResult(new CacheLoad(_value));
        public Task SaveAsync(CacheEnvelope envelope, CancellationToken token) { if (FailWrites) throw new IOException(); _value = envelope; return Task.CompletedTask; }
    }
    private sealed class FakeProvider : IResetProvider
    {
        public Func<DateTimeOffset, CancellationToken, Task<ProviderSnapshot>> Status { get; init; } = (at, _) => Task.FromResult(Snapshot(at));
        public Func<IReadOnlySet<EventKey>, bool, CancellationToken, Task<HistoryResult>> History { get; init; } = (_, _, _) => Task.FromResult(new HistoryResult([], true));
        public Task<ProviderSnapshot> GetStatusAsync(DateTimeOffset at, CancellationToken token) => Status(at, token);
        public Task<HistoryResult> GetHistoryAsync(IReadOnlySet<EventKey> known, bool full, CancellationToken token) => History(known, full, token);
    }
}
