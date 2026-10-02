using System.Net;
using System.Text.Json;
using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class SyncTestData
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-02T15:00:00Z");
    public static string EventJson(string id = "2105843926221660585", string type = "regular") =>
        $$$"""{"id":"{{{id}}}","status":"scheduled","reset_type":"{{{type}}}","announced_at":"2026-10-02T02:14:51Z","scheduled_for":"2026-10-02T17:00:00Z","text":"Sample","source":{"type":"x_post","author":"thsottiaux","url":"https://x.com/thsottiaux/status/{{{id}}}"}}""";
    public static string Status(string? reset = null) => "{\"data\":{\"scheduled_reset\":" + (reset ?? EventJson()) + ",\"latest_reset\":null,\"active_watch\":null},\"meta\":{\"generated_at\":\"2026-10-02T15:00:00Z\"}}";
    public static string Page(string data, bool more = false, string? cursor = null) =>
        "{\"data\":[" + data + "],\"pagination\":{\"has_more\":" + (more ? "true" : "false") + ",\"next_cursor\":" + JsonSerializer.Serialize(cursor) + "}}";
    public static HttpResponseMessage Ok(string body, string? tag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        if (tag is not null) response.Headers.TryAddWithoutValidation("ETag", tag);
        return response;
    }
    public static ProviderSnapshot Snapshot(DateTimeOffset at) => new(CodexResetsClient.Normalize(JsonSerializer.Deserialize<ApiReset>(EventJson(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!, true), null, null, at, at);
    public static FakeProvider Offline() => new() { Status = (_, _) => throw new HttpRequestException(), History = (_, _, _) => throw new HttpRequestException() };
    public sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    public sealed class TestClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; set; } = now; }
    public sealed class MemoryCache : ICacheStore
    {
        public bool FailWrites { get; init; }
        private CacheEnvelope? _value;
        public Task<CacheLoad> LoadAsync(CancellationToken token) => Task.FromResult(new CacheLoad(_value));
        public Task SaveAsync(CacheEnvelope envelope, CancellationToken token) { if (FailWrites) throw new IOException(); _value = envelope; return Task.CompletedTask; }
    }
    public sealed class FakeProvider : IResetProvider
    {
        public Func<DateTimeOffset, CancellationToken, Task<ProviderSnapshot>> Status { get; init; } = (at, _) => Task.FromResult(Snapshot(at));
        public Func<IReadOnlySet<EventKey>, bool, CancellationToken, Task<HistoryResult>> History { get; init; } = (_, _, _) => Task.FromResult(new HistoryResult([], true));
        public Task<ProviderSnapshot> GetStatusAsync(DateTimeOffset at, CancellationToken token) => Status(at, token);
        public Task<HistoryResult> GetHistoryAsync(IReadOnlySet<EventKey> known, bool full, CancellationToken token) => History(known, full, token);
    }
}
