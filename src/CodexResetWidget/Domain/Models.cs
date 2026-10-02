namespace CodexResetWidget.Domain;

public readonly record struct EventKey(string Provider, string Id);
public enum ResetType { Regular, Banked, Unknown }
public enum EventStatus { Scheduled, Recorded, Unknown }
public enum SourceKind { Announcement, Observed, Unknown }
public enum BoardStatus { NoAnnouncement, Watch, TimeUnknown, Countdown, RecentAnnouncement }

public sealed record ResetEvent(
    EventKey Key, ResetType Type, EventStatus Status, SourceKind SourceKind,
    DateTimeOffset AnnouncedAtUtc, DateTimeOffset? ScheduledForUtc,
    string Text, Uri? SourceUrl = null, string? Author = null, string? Handle = null,
    string? RawType = null);

public sealed record WatchSignal(string Id, DateTimeOffset? ExpiresAtUtc);
public sealed record ProviderSnapshot(ResetEvent? ScheduledReset, WatchSignal? ActiveWatch,
    ResetEvent? LatestReset, DateTimeOffset? GeneratedAtUtc, DateTimeOffset? LastCheckedAtUtc);
public sealed record PendingAnnouncement(ResetEvent LastKnownEvent,
    bool MissingFromLatestStatus = false, bool TargetChanged = false);
public sealed record HistoryStore(IReadOnlyList<ResetEvent> Events, bool IsComplete,
    DateTimeOffset? LastFullSyncAtUtc);
public sealed record DataHealth(bool IsLoading = false, bool IsFromCache = false,
    bool IsStale = false, DateTimeOffset? LastSuccessAtUtc = null, string? LastError = null);
public sealed record WidgetSnapshot(long Revision, ProviderSnapshot? Status, HistoryStore History,
    PendingAnnouncement? Pending, DataHealth StatusHealth, DataHealth HistoryHealth);
public sealed record BoardState(BoardStatus Status, ResetEvent? RelatedEvent, TimeSpan Remaining,
    bool AnnouncementUnconfirmed = false, bool TargetChanged = false);

public sealed record CalendarDay(DateOnly Date, bool IsCurrentMonth, bool IsToday,
    IReadOnlyList<ResetEvent> Events, ResetEvent? Forecast);
public sealed record CalendarMonth(DateOnly Month, IReadOnlyList<CalendarDay> Days);
