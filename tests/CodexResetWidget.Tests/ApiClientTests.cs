using System.Net;
using static SyncTestData;
using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class ApiClientTests
{
    public static async Task<(int Passed, int Failed)> RunAsync()
    {
        var tests = new TestSuite("ApiClient");
        async Task Rejected(string body)
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(body))));
            try { await new CodexResetsClient(http).GetStatusAsync(Now, default); throw new Exception("Accepted invalid response"); }
            catch (ApiException) { }
        }
        await tests.CheckAsync("Adapter preserves large string IDs and UTC targets", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Status()))));
            var result = await new CodexResetsClient(http).GetStatusAsync(Now, default);
            Assert(result.ScheduledReset!.Key.Id == "2105843926221660585" && result.ScheduledReset.Author == "Tibo");
            Assert(result.ScheduledReset.ScheduledForUtc == Now.AddHours(2));
        });
        await tests.CheckAsync("Adapter retains unknown types and ignores additional fields", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Status(EventJson(type: "new_type")).Replace("\"data\":", "\"new_field\":42,\"data\":")))));
            var result = await new CodexResetsClient(http).GetStatusAsync(Now, default);
            Assert(result.ScheduledReset!.Type == ResetType.Unknown && result.ScheduledReset.RawType == "new_type");
        });
        await tests.CheckAsync("Missing offset is rejected rather than guessed", () => Rejected(Status().Replace("2026-10-02T17:00:00Z", "2026-10-02T17:00:00")));
        await tests.CheckAsync("Missing scheduled field is not treated as withdrawal", () => Rejected("{\"data\":{\"latest_reset\":null}}"));
        await tests.CheckAsync("Unknown scheduled status is rejected", () => Rejected(Status().Replace("\"scheduled\"", "\"done\"")));
        await tests.CheckAsync("Observed source keeps URL but never invents author", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Page(EventJson().Replace("\"x_post\"", "\"observed\""))))));
            var reset = (await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey>(), true, default)).Events[0];
            Assert(reset.SourceKind == SourceKind.Observed && reset.Author is null && reset.SourceUrl is not null);
        });
        await tests.CheckAsync("304 reuses exact URL body and conditional header", async () =>
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
        await tests.CheckAsync("304 without body retries unconditionally", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new Handler((req, _) => { Assert(req.Headers.IfNoneMatch.Count == 0); return Task.FromResult(++calls == 1 ? new HttpResponseMessage(HttpStatusCode.NotModified) : Ok(Status())); }));
            Assert((await new CodexResetsClient(http).GetStatusAsync(Now, default)).ScheduledReset is not null && calls == 2);
        });
        await tests.CheckAsync("HTTP 429 parses Retry-After seconds", async () =>
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
        await tests.CheckAsync("Cancellation reaches the HTTP request", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancel = new CancellationTokenSource();
            using var http = new HttpClient(new Handler(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Ok(Status()); }));
            var pending = new CodexResetsClient(http).GetStatusAsync(Now, cancel.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancel.Cancel();
            try { await pending; throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
        });
        await tests.CheckAsync("Conditional response bodies survive a client restart", async () =>
        {
            using var firstHttp = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Status(), "\"persisted\""))));
            var first = new CodexResetsClient(firstHttp); await first.GetStatusAsync(Now, default);
            using var nextHttp = new HttpClient(new Handler((req, _) => { Assert(req.Headers.IfNoneMatch.Single().Tag == "\"persisted\""); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)); }));
            var next = new CodexResetsClient(nextHttp); next.RestoreDocuments(first.ExportDocuments());
            Assert((await next.GetStatusAsync(Now.AddMinutes(5), default)).ScheduledReset!.Key.Id == "2105843926221660585");
        });
        await tests.CheckAsync("Sequential pagination deduplicates records", async () =>
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
        await tests.CheckAsync("Incremental history stops at known records", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Ok(Page(EventJson("a"), true, "abc"))); }));
            var result = await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey> { new(CodexResetsClient.Provider, "a") }, false, default);
            Assert(!result.Complete && calls == 1);
        });
        await tests.CheckAsync("Invalid history rows leave a visible incomplete warning", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Page(EventJson("a") + ",{\"id\":\"bad\"}")))));
            var result = await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey>(), true, default);
            Assert(result.Events.Count == 1 && !result.Complete && result.Warning is not null);
        });
        await tests.CheckAsync("Partial pagination failure retains successful pages", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(++calls == 1 ? Ok(Page(EventJson("a"), true, "abc")) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
            try { await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey>(), true, default); throw new Exception("Expected partial failure"); }
            catch (PartialHistoryException error) { Assert(error.Partial.Events.Count == 1 && !error.Partial.Complete); }
        });
        await tests.CheckAsync("Repeated cursor stops pagination", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(Page(EventJson("a"), true, "abc")))));
            try { await new CodexResetsClient(http).GetHistoryAsync(new HashSet<EventKey>(), true, default); throw new Exception("Expected cursor error"); }
            catch (PartialHistoryException error) { Assert(error.Message.Contains("重复")); }
        });

        return tests.Result;
    }

}
