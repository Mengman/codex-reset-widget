namespace CodexResetWidget.Domain;

public sealed class CalendarService
{
    public CalendarMonth BuildMonth(WidgetSnapshot snapshot, DateOnly month, TimeZoneInfo zone,
        DateTimeOffset nowUtc)
    {
        month = new(month.Year, month.Month, 1);
        var first = month.AddDays(-((int)month.DayOfWeek + 6) % 7);
        var length = DateTime.DaysInMonth(month.Year, month.Month);
        var count = ((month.DayNumber - first.DayNumber + length + 6) / 7) * 7;
        var today = LocalDate(nowUtc, zone);
        // Scheduled records must never become completed history simply by appearing in a page.
        var groups = snapshot.History.Events.Where(e => e.Status != EventStatus.Scheduled)
            .GroupBy(e => LocalDate(e.AnnouncedAtUtc, zone))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ResetEvent>)g.OrderByDescending(e => e.AnnouncedAtUtc).ToArray());
        var forecast = snapshot.Status?.ScheduledReset ?? snapshot.Pending?.LastKnownEvent;
        DateOnly? forecastDate = forecast?.ScheduledForUtc is { } target && target > nowUtc
            ? LocalDate(target, zone) : null;
        return new(month, Enumerable.Range(0, count).Select(index =>
        {
            var date = first.AddDays(index);
            return new CalendarDay(date, date.Month == month.Month && date.Year == month.Year,
                date == today, groups.GetValueOrDefault(date) ?? [],
                forecastDate == date ? forecast : null);
        }).ToArray());
    }

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
