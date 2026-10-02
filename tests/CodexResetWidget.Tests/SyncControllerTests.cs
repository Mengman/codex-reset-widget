using System.Net;
using static SyncTestData;
using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class SyncControllerTests
{
    public static async Task<(int Passed, int Failed)> RunAsync()
    {
        var tests = new TestSuite("SyncController");
        await tests.CheckAsync("429 Retry-After remains binding on manual refresh", async () =>
        {
            var calls = 0; var clock = new TestClock(Now);
            var provider = new FakeProvider { Status = (_, _) => { calls++; throw new ApiException("限流", TimeSpan.FromMinutes(10)); } };
            using var sync = new SyncController(provider, new MemoryCache(), clock);
            await sync.InitializeAsync(); await sync.RefreshAsync(); Assert(calls == 1);
            clock.UtcNow = Now.AddMinutes(10); await sync.PollAsync(); Assert(calls == 2);
        });
        await tests.CheckAsync("Ordinary failures back off until next scheduled attempt", async () =>
        {
            var calls = 0; var clock = new TestClock(Now);
            var provider = new FakeProvider { Status = (_, _) => { calls++; throw new HttpRequestException(); } };
            using var sync = new SyncController(provider, new MemoryCache(), clock);
            await sync.InitializeAsync(); clock.UtcNow = Now.AddMinutes(4); await sync.PollAsync(); Assert(calls == 1);
            clock.UtcNow = Now.AddMinutes(6); await sync.PollAsync(); Assert(calls == 2);
        });
        await tests.CheckAsync("Independent schedules refresh status at 5 and history at 30 minutes", async () =>
        {
            var statuses = 0; var histories = 0; var clock = new TestClock(Now);
            var provider = new FakeProvider { Status = (at, _) => { statuses++; return Task.FromResult(Snapshot(at)); }, History = (_, _, _) => { histories++; return Task.FromResult(new HistoryResult([], true)); } };
            using var sync = new SyncController(provider, new MemoryCache(), clock);
            await sync.InitializeAsync(); clock.UtcNow = Now.AddMinutes(5); await sync.PollAsync(); Assert(statuses == 2 && histories == 1);
            clock.UtcNow = Now.AddMinutes(30); await sync.PollAsync(); Assert(histories == 2);
        });
        await tests.CheckAsync("Withdrawal and corrections preserve pending context across refresh", async () =>
        {
            var next = Snapshot(Now); var provider = new FakeProvider { Status = (_, _) => Task.FromResult(next) };
            using var sync = new SyncController(provider, new MemoryCache(), new TestClock(Now));
            await sync.InitializeAsync(); var target = sync.Current.Pending!.LastKnownEvent.ScheduledForUtc;
            next = next with { ScheduledReset = null }; await sync.RefreshAsync();
            Assert(sync.Current.Pending!.MissingFromLatestStatus && sync.Current.Pending.LastKnownEvent.ScheduledForUtc == target);
            next = Snapshot(Now) with { ScheduledReset = Snapshot(Now).ScheduledReset! with { ScheduledForUtc = target!.Value.AddHours(1) } };
            await sync.RefreshAsync(); Assert(sync.Current.Pending!.TargetChanged && !sync.Current.Pending.MissingFromLatestStatus);
        });
        await tests.CheckAsync("Late response after cancellation cannot overwrite newer request", async () =>
        {
            var old = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
            var provider = new FakeProvider { Status = (at, _) => { if (++calls == 1) { entered.SetResult(); return old.Task; } return Task.FromResult(Snapshot(at) with { ScheduledReset = Snapshot(at).ScheduledReset! with { Text = "new" } }); } };
            using var sync = new SyncController(provider, new MemoryCache(), new TestClock(Now));
            var first = sync.RefreshAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second = sync.RefreshAsync(); old.SetResult(Snapshot(Now) with { ScheduledReset = Snapshot(Now).ScheduledReset! with { Text = "old" } });
            await Task.WhenAll(first, second); Assert(sync.Current.Status!.ScheduledReset!.Text == "new" && !sync.Current.StatusHealth.IsLoading);
        });
        await tests.CheckAsync("First launch offline has no invented announcement", async () =>
        {
            using var sync = new SyncController(Offline(), new MemoryCache(), new TestClock(Now)); await sync.InitializeAsync();
            Assert(sync.Current.Status is null && sync.Current.StatusHealth.LastError is not null && !sync.Current.History.IsComplete);
        });
        await tests.CheckAsync("Partial history refresh merges pages without deleting older records", async () =>
        {
            var calls = 0; var old = Snapshot(Now).ScheduledReset! with { Key = new(CodexResetsClient.Provider, "old"), Status = EventStatus.Recorded };
            var next = old with { Key = new(CodexResetsClient.Provider, "new") };
            var provider = new FakeProvider { History = (_, _, _) => ++calls == 1 ? Task.FromResult(new HistoryResult([old], true))
                : throw new PartialHistoryException(new([next], false), new ApiException("后续分页失败")) };
            using var sync = new SyncController(provider, new MemoryCache(), new TestClock(Now));
            await sync.InitializeAsync(); await sync.RefreshAsync();
            Assert(sync.Current.History.Events.Count == 2 && sync.Current.HistoryHealth.LastError is not null && sync.Current.Status is not null);
        });
        await tests.CheckAsync("Disposal suppresses a late provider response", async () =>
        {
            var response = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new FakeProvider { Status = (_, _) => response.Task };
            var sync = new SyncController(provider, new MemoryCache(), new TestClock(Now));
            var request = sync.RefreshAsync(); sync.Dispose(); response.SetResult(Snapshot(Now)); await request;
            Assert(sync.Current.Status is null);
        });
        await tests.CheckAsync("Offline restart restores pending target and complete history", async () =>
        {
            var cache = new MemoryCache();
            using (var sync = new SyncController(new FakeProvider(), cache, new TestClock(Now))) await sync.InitializeAsync();
            using var offline = new SyncController(Offline(), cache, new TestClock(Now.AddDays(1))); await offline.InitializeAsync();
            Assert(offline.Current.Status is not null && offline.Current.Pending is not null && offline.Current.StatusHealth.IsFromCache);
            Assert(new ResetStateService().Evaluate(offline.Current, Now.AddDays(1)).Status == BoardStatus.RecentAnnouncement);
        });
        await tests.CheckAsync("Cache write failure leaves fetched data available", async () =>
        {
            using var sync = new SyncController(new FakeProvider(), new MemoryCache { FailWrites = true }, new TestClock(Now));
            await sync.InitializeAsync(); Assert(sync.Current.Status is not null && sync.CacheWarning is not null && sync.Current.StatusHealth.LastError is null);
        });
        await tests.CheckAsync("Stale upstream generation is distinct from successful HTTP check", async () =>
        {
            var provider = new FakeProvider { Status = (at, _) => Task.FromResult(Snapshot(at) with { GeneratedAtUtc = at.AddHours(-2) }) };
            using var sync = new SyncController(provider, new MemoryCache(), new TestClock(Now)); await sync.InitializeAsync();
            Assert(sync.Current.StatusHealth.IsStale && sync.Current.StatusHealth.LastSuccessAtUtc == Now);
        });

        return tests.Result;
    }

}
