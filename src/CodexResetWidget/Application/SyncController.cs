using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;

namespace CodexResetWidget.Application;

public sealed class SyncController(IResetProvider provider, ICacheStore cache, IClock clock, Action<string>? log = null) : IDisposable
{
    private sealed class Channel
    {
        public readonly SemaphoreSlim Gate = new(1);
        public CancellationTokenSource? Request;
        public long Sequence;
        public int Failures;
        public DateTimeOffset Due;
        public DateTimeOffset NotBefore;
    }
    private readonly Channel _status = new(), _history = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _cacheGate = new(1);
    private readonly object _stateLock = new();
    private WidgetSnapshot _current = new(0, null, new([], false, null), null, new(), new());
    public WidgetSnapshot Current { get { lock (_stateLock) return _current; } }
    public event Action<WidgetSnapshot>? Updated;
    public string? CacheWarning { get; private set; }
    public async Task InitializeAsync()
    {
        var loaded = await cache.LoadAsync(_lifetime.Token);
        CacheWarning = loaded.Warning;
        if (loaded.Warning is not null) log?.Invoke(loaded.Warning);
        if (loaded.Value is { } value)
        {
            if (provider is CodexResetsClient client) client.RestoreDocuments(value.Documents);
            Publish(_ => value.Snapshot with
            {
                StatusHealth = value.Snapshot.StatusHealth with { IsLoading = false, IsFromCache = true },
                HistoryHealth = value.Snapshot.HistoryHealth with { IsLoading = false, IsFromCache = true }
            });
        }
        else Publish(s => s with { StatusHealth = s.StatusHealth with { LastError = loaded.Warning } });
        await RefreshAsync(true);
    }
    public Task RefreshAsync(bool fullHistory = true) => Task.WhenAll(RunAsync(_status, true, false), RunAsync(_history, false, fullHistory));
    public async Task PollAsync()
    {
        UpdateStaleness();
        var now = clock.UtcNow;
        var tasks = new List<Task>();
        var current = Current;
        if (now >= _status.Due && !current.StatusHealth.IsLoading) tasks.Add(RunAsync(_status, true, false));
        if (now >= _history.Due && !current.HistoryHealth.IsLoading)
            tasks.Add(RunAsync(_history, false, !current.History.IsComplete || current.History.LastFullSyncAtUtc < now.AddHours(-6)));
        await Task.WhenAll(tasks);
    }
    private void UpdateStaleness() => Publish(s => s with
    {
        StatusHealth = s.StatusHealth with { IsStale = s.StatusHealth.LastSuccessAtUtc is { } at && clock.UtcNow - at > TimeSpan.FromMinutes(15)
            || s.Status?.GeneratedAtUtc is { } generated && clock.UtcNow - generated > TimeSpan.FromMinutes(15) },
        HistoryHealth = s.HistoryHealth with { IsStale = s.HistoryHealth.LastSuccessAtUtc is { } historyAt && clock.UtcNow - historyAt > TimeSpan.FromMinutes(60) }
    });
    private async Task RunAsync(Channel channel, bool status, bool full)
    {
        if (_lifetime.IsCancellationRequested || clock.UtcNow < channel.NotBefore) return;
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        long sequence;
        lock (channel) { channel.Request?.Cancel(); channel.Request = request; sequence = ++channel.Sequence; }
        var entered = false;
        try
        {
            await channel.Gate.WaitAsync(request.Token); entered = true;
            request.Token.ThrowIfCancellationRequested();
            Publish(s => status ? s with { StatusHealth = s.StatusHealth with { IsLoading = true } }
                : s with { HistoryHealth = s.HistoryHealth with { IsLoading = true } });
            if (status)
            {
                var result = await provider.GetStatusAsync(clock.UtcNow, request.Token);
                if (!IsCurrent(channel, sequence, request)) return;
                Publish(s =>
                {
                    var reset = result.ScheduledReset;
                    var pending = reset is not null ? new PendingAnnouncement(reset, false,
                        s.Pending?.LastKnownEvent.Key == reset.Key && s.Pending.LastKnownEvent.ScheduledForUtc != reset.ScheduledForUtc)
                        : s.Pending is { } previous ? previous with { MissingFromLatestStatus = true } : null;
                    return s with { Status = result, Pending = pending, StatusHealth = new(LastSuccessAtUtc: clock.UtcNow) };
                });
            }
            else
            {
                var result = await provider.GetHistoryAsync(Current.History.Events.Select(e => e.Key).ToHashSet(), full, request.Token);
                if (!IsCurrent(channel, sequence, request)) return;
                MergeHistory(result, full);
                if (result.Warning is not null) log?.Invoke(result.Warning);
                Publish(s => s with { HistoryHealth = new(LastSuccessAtUtc: clock.UtcNow, LastError: result.Warning) });
            }
            channel.Failures = 0; channel.NotBefore = default;
            channel.Due = clock.UtcNow.AddMinutes(status ? 5 : 30);
            UpdateStaleness();
            await SaveAsync();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!IsCurrent(channel, sequence, request)) return;
            if (error is PartialHistoryException partial) MergeHistory(partial.Partial, false);
            var actual = error is PartialHistoryException p ? p.InnerException! : error;
            log?.Invoke($"{(status ? "Status" : "History")} {actual.GetType().Name}: {actual.Message} {actual.InnerException?.GetType().Name}: {actual.InnerException?.Message}");
            var message = actual is ApiException ? actual.Message : actual is System.Text.Json.JsonException or KeyNotFoundException
                ? "接口数据格式异常，保留已有数据。" : actual is OperationCanceledException ? "请求超时，保留已有数据。" : "更新失败，请检查网络；保留已有数据。";
            var retry = actual is ApiException { RetryAfter: { } wait } ? wait
                : TimeSpan.FromMinutes(Math.Min(30, 5 * Math.Pow(2, Math.Min(channel.Failures++, 3)))) + TimeSpan.FromSeconds(Random.Shared.Next(0, 30));
            channel.Due = clock.UtcNow + retry;
            // Manual refresh can retry ordinary failures; server rate limits remain binding.
            if (actual is ApiException { RetryAfter: not null }) channel.NotBefore = channel.Due;
            Publish(s => status ? s with { StatusHealth = s.StatusHealth with { IsLoading = false, LastError = message } }
                : s with { HistoryHealth = s.HistoryHealth with { IsLoading = false, LastError = message } });
            UpdateStaleness(); await SaveAsync();
        }
        finally
        {
            if (entered) channel.Gate.Release();
            lock (channel) { if (ReferenceEquals(channel.Request, request)) channel.Request = null; request.Dispose(); }
        }
    }
    private bool IsCurrent(Channel channel, long sequence, CancellationTokenSource request) => sequence == channel.Sequence && !request.IsCancellationRequested;
    private void MergeHistory(HistoryResult result, bool full) => Publish(s =>
    {
        var events = s.History.Events.ToDictionary(e => e.Key);
        foreach (var reset in result.Events) events[reset.Key] = reset;
        return s with { History = new(events.Values.OrderByDescending(e => e.AnnouncedAtUtc).ToArray(),
            s.History.IsComplete || result.Complete, full && result.Complete ? clock.UtcNow : s.History.LastFullSyncAtUtc) };
    });
    private void Publish(Func<WidgetSnapshot, WidgetSnapshot> change)
    {
        WidgetSnapshot snapshot;
        lock (_stateLock) { snapshot = change(_current) with { Revision = _current.Revision + 1 }; _current = snapshot; }
        Updated?.Invoke(snapshot);
    }
    private async Task SaveAsync()
    {
        try
        {
            await _cacheGate.WaitAsync(_lifetime.Token);
            try
            {
                var snapshot = Current;
                if (snapshot.Status is null && snapshot.History.Events.Count == 0) return;
                await cache.SaveAsync(new(1, snapshot, provider is CodexResetsClient client ? client.ExportDocuments() : []), _lifetime.Token);
                CacheWarning = null;
            }
            finally { _cacheGate.Release(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { CacheWarning = "缓存无法保存；当前数据仍可阅读，下次启动可能无法离线恢复。"; log?.Invoke(CacheWarning); Publish(s => s); }
    }
    public void Dispose()
    {
        _lifetime.Cancel();
        lock (_status) _status.Request?.Cancel();
        lock (_history) _history.Request?.Cancel();
    }
}
