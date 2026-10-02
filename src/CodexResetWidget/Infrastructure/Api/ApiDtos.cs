using System.Text.Json.Serialization;

namespace CodexResetWidget.Infrastructure.Api;

public sealed record ApiSource(string? Type, string? Author, string? Url);
public sealed record ApiReset(string? Id, [property: JsonPropertyName("reset_type")] string? ResetType,
    [property: JsonPropertyName("announced_at")] string? AnnouncedAt,
    [property: JsonPropertyName("scheduled_for")] string? ScheduledFor, string? Text, ApiSource? Source, string? Status);
public sealed record ApiWatch([property: JsonPropertyName("observed_at")] string? ObservedAt,
    [property: JsonPropertyName("expires_at")] string? ExpiresAt);
public sealed record ApiStatusData([property: JsonPropertyName("scheduled_reset")] ApiReset? ScheduledReset,
    [property: JsonPropertyName("latest_reset")] ApiReset? LatestReset,
    [property: JsonPropertyName("active_watch")] ApiWatch? ActiveWatch);
public sealed record ApiMeta([property: JsonPropertyName("generated_at")] string? GeneratedAt);
public sealed record ApiStatusEnvelope(ApiStatusData? Data, ApiMeta? Meta);
public sealed record ApiPagination([property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);
public sealed record ApiHistoryEnvelope(ApiReset[]? Data, ApiPagination? Pagination, ApiMeta? Meta);
