namespace CodexResetWidget.Domain;

public sealed class ResetStateService
{
    public BoardState Evaluate(WidgetSnapshot snapshot, DateTimeOffset nowUtc)
    {
        var current = snapshot.Status?.ScheduledReset;
        var announcement = current ?? snapshot.Pending?.LastKnownEvent;
        var unconfirmed = current is null && snapshot.Pending?.MissingFromLatestStatus == true;
        var changed = snapshot.Pending?.TargetChanged == true &&
            snapshot.Pending.LastKnownEvent.Key == announcement?.Key;
        if (announcement is not null)
        {
            if (announcement.ScheduledForUtc is not { } target)
                return new(BoardStatus.TimeUnknown, announcement, TimeSpan.Zero, unconfirmed, changed);
            var remaining = target - nowUtc;
            return new(remaining > TimeSpan.Zero ? BoardStatus.Countdown : BoardStatus.RecentAnnouncement,
                announcement, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, unconfirmed, changed);
        }
        var watch = snapshot.Status?.ActiveWatch;
        if (watch is not null && (watch.ExpiresAtUtc is null || watch.ExpiresAtUtc > nowUtc))
            return new(BoardStatus.Watch, null, TimeSpan.Zero);
        return new(BoardStatus.NoAnnouncement, null, TimeSpan.Zero);
    }
}
