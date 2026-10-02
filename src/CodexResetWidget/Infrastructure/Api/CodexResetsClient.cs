using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexResetWidget.Domain;

namespace CodexResetWidget.Infrastructure.Api;

public sealed record HttpDocument(string Body, string? ETag);
public sealed record HistoryResult(IReadOnlyList<ResetEvent> Events, bool Complete, string? Warning = null);
public sealed class ApiException(string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
public sealed class PartialHistoryException(HistoryResult partial, Exception inner) : Exception(inner.Message, inner)
{
    public HistoryResult Partial { get; } = partial;
}
public interface IResetProvider
{
    Task<ProviderSnapshot> GetStatusAsync(DateTimeOffset checkedAt, CancellationToken token);
    Task<HistoryResult> GetHistoryAsync(IReadOnlySet<EventKey> known, bool full, CancellationToken token);
}

public sealed class CodexResetsClient(HttpClient http) : IResetProvider
{
    public const string Provider = "codex-resets";
    public const string StatusUrl = "https://codex-resets.com/api/v1/status";
    public const string HistoryUrl = "https://codex-resets.com/api/v1/resets?limit=100&order=desc";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, HttpDocument> _documents = [];
    private readonly object _documentLock = new();
    public Dictionary<string, HttpDocument> ExportDocuments() { lock (_documentLock) return new(_documents); }
    public void RestoreDocuments(Dictionary<string, HttpDocument> documents)
    {
        lock (_documentLock)
            foreach (var (url, document) in documents)
                if ((url == StatusUrl || url.StartsWith(HistoryUrl + "&cursor=", StringComparison.Ordinal) || url == HistoryUrl)
                    && document is { Body: not null } && document.Body.Length < 4_000_000) _documents[url] = document;
    }

    public async Task<ProviderSnapshot> GetStatusAsync(DateTimeOffset checkedAt, CancellationToken token)
    {
        var (body, tag) = await GetAsync(StatusUrl, token);
        var envelope = JsonSerializer.Deserialize<ApiStatusEnvelope>(body, Json) ?? throw new ApiException("状态响应为空。");
        var data = envelope.Data ?? throw new ApiException("状态响应缺少 data。");
        // Distinguish absent contract fields from an explicit null announcement.
        using var raw = JsonDocument.Parse(body);
        if (!raw.RootElement.GetProperty("data").TryGetProperty("scheduled_reset", out _))
            throw new ApiException("状态响应缺少 scheduled_reset，已保留原有预告。");
        var scheduled = data.ScheduledReset is null ? null : Normalize(data.ScheduledReset, true);
        var latest = data.LatestReset is null ? null : Normalize(data.LatestReset, false);
        var watch = data.ActiveWatch is null ? null : new WatchSignal(
            RequiredTime(data.ActiveWatch.ObservedAt).ToString("O"), RequiredTime(data.ActiveWatch.ExpiresAt));
        var result = new ProviderSnapshot(scheduled, watch, latest, OptionalTime(envelope.Meta?.GeneratedAt), checkedAt);
        token.ThrowIfCancellationRequested(); Store(StatusUrl, body, tag);
        return result;
    }

    public async Task<HistoryResult> GetHistoryAsync(IReadOnlySet<EventKey> known, bool full, CancellationToken token)
    {
        var events = new Dictionary<EventKey, ResetEvent>();
        var cursors = new HashSet<string>();
        var url = HistoryUrl;
        var rejected = 0;
        try
        {
            for (var page = 0; page < 100; page++)
            {
                var (body, tag) = await GetAsync(url, token);
                var envelope = JsonSerializer.Deserialize<ApiHistoryEnvelope>(body, Json) ?? throw new ApiException("历史响应为空。");
                if (envelope.Data is null || envelope.Pagination is null) throw new ApiException("历史响应缺少记录或分页信息。");
                using var raw = JsonDocument.Parse(body);
                if (!raw.RootElement.GetProperty("pagination").TryGetProperty("has_more", out _))
                    throw new ApiException("历史响应缺少分页边界。");
                var reachedKnown = false;
                foreach (var item in envelope.Data)
                {
                    try { var reset = Normalize(item, false); reachedKnown |= known.Contains(reset.Key); events.TryAdd(reset.Key, reset); }
                    catch (ApiException) { rejected++; }
                }
                token.ThrowIfCancellationRequested();
                // Invalid pages are not used as conditional-request bodies on future refreshes.
                if (rejected == 0) Store(url, body, tag);
                if (!envelope.Pagination.HasMore || (!full && reachedKnown))
                    return new(events.Values.ToArray(), !envelope.Pagination.HasMore && rejected == 0,
                        rejected > 0 ? $"已跳过 {rejected} 条字段无效的历史记录。" : null);
                var cursor = envelope.Pagination.NextCursor;
                if (string.IsNullOrEmpty(cursor) || cursor.Length > 1024 || !Regex.IsMatch(cursor, "^[A-Za-z0-9_-]+$") || !cursors.Add(cursor))
                    throw new ApiException("历史分页游标无效或重复。");
                url = HistoryUrl + "&cursor=" + Uri.EscapeDataString(cursor);
            }
            throw new ApiException("历史分页超过安全上限，保留已加载记录。");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (events.Count > 0) { throw new PartialHistoryException(new(events.Values.ToArray(), false), error); }
    }

    public static ResetEvent Normalize(ApiReset dto, bool scheduled)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Id) || dto.Id.Length > 128 || dto.Text is null || dto.Source is null)
            throw new ApiException("公告缺少有效 ID、文本或来源。");
        if (scheduled && dto.Status != "scheduled") throw new ApiException("未知预告状态，已保留原有预告。");
        var rawType = dto.ResetType ?? "unknown";
        var type = rawType switch { "regular" => ResetType.Regular, "banked" => ResetType.Banked, _ => ResetType.Unknown };
        var kind = dto.Source.Type switch { "x_post" => SourceKind.Announcement, "observed" => SourceKind.Observed, _ => SourceKind.Unknown };
        Uri? link = Uri.TryCreate(dto.Source.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri : null;
        var author = kind == SourceKind.Announcement ? dto.Source.Author : null;
        return new(new(Provider, dto.Id), type, scheduled ? EventStatus.Scheduled : EventStatus.Recorded, kind,
            RequiredTime(dto.AnnouncedAt), OptionalTime(dto.ScheduledFor), dto.Text, link,
            author == "thsottiaux" ? "Tibo" : author, author is null ? null : "@" + author, rawType);
    }
    public static DateTimeOffset RequiredTime(string? value)
    {
        if (value is null || !Regex.IsMatch(value, @"(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            throw new ApiException("接口时间缺少明确时区或格式无效。");
        return result.ToUniversalTime();
    }
    private static DateTimeOffset? OptionalTime(string? value) => value is null ? null : RequiredTime(value);
    private void Store(string url, string body, string? tag) { lock (_documentLock) _documents[url] = new(body, tag); }
    private async Task<(string Body, string? Tag)> GetAsync(string url, CancellationToken token)
    {
        HttpDocument? document;
        lock (_documentLock) _documents.TryGetValue(url, out document);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            request.Headers.UserAgent.ParseAdd("CodexResetWidget/0.2.0");
            if (document?.ETag is { } tag) request.Headers.TryAddWithoutValidation("If-None-Match", tag);
            using var response = await http.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (document is not null) return (document.Body, document.ETag);
                // No valid corresponding body: retry with an unconditional request.
                continue;
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                throw new ApiException("数据源限流，稍后重试。", retry is { } wait && wait > TimeSpan.Zero ? wait : TimeSpan.FromMinutes(5));
            }
            if (!response.IsSuccessStatusCode) throw new ApiException($"数据源返回 HTTP {(int)response.StatusCode}。");
            if (response.Content.Headers.ContentLength > 4_000_000) throw new ApiException("接口响应过大。");
            var body = await response.Content.ReadAsStringAsync(token);
            if (body.Length > 4_000_000) throw new ApiException("接口响应过大。");
            return (body, response.Headers.ETag?.ToString());
        }
        throw new ApiException("数据源返回 304，但没有可用的对应缓存。");
    }
}
