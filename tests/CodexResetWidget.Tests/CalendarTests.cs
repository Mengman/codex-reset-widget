using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class CalendarTests
{
    public static (int Passed, int Failed) Run()
    {
        var tests = new TestSuite("Calendar");
        var now = DateTimeOffset.Parse("2026-10-02T15:00:00Z");
        var utc = TimeZoneInfo.Utc;
        var china = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var calendar = new CalendarService();
        WidgetSnapshot Sample(string scenario) => DemoData.Create(scenario, now, utc);

        tests.Check("Leap February has 29 in-month days and Monday start", () => {
            var m = calendar.BuildMonth(Sample("none"), new(2024, 2, 1), utc, now);
            Equal(29, m.Days.Count(d => d.IsCurrentMonth)); Equal(DayOfWeek.Monday, m.Days[0].Date.DayOfWeek); Equal(0, m.Days.Count % 7);
        });
        tests.Check("Six-week month keeps all 42 cells", () => Equal(42, calendar.BuildMonth(Sample("none"), new(2026, 3, 1), utc, now).Days.Count));
        tests.Check("Four-week February stays valid", () => Equal(28, calendar.BuildMonth(Sample("none"), new(2021, 2, 1), utc, now).Days.Count));
        tests.Check("History date uses publication not target", () => {
            var e = Sample("future").Status!.ScheduledReset! with { Status = EventStatus.Recorded, AnnouncedAtUtc = DateTimeOffset.Parse("2026-09-30T17:00:00Z"), ScheduledForUtc = DateTimeOffset.Parse("2026-10-05T17:00:00Z") };
            var s = Sample("none") with { History = new([e], true, now) };
            var m = calendar.BuildMonth(s, new(2026, 10, 1), china, now);
            Equal(1, m.Days.Single(d => d.Date == new DateOnly(2026, 10, 1)).Events.Count);
            Equal(0, m.Days.Single(d => d.Date == new DateOnly(2026, 10, 6)).Events.Count);
        });
        tests.Check("Time-zone conversion can move history across month", () => {
            var e = Sample("none").History.Events[0] with { AnnouncedAtUtc = DateTimeOffset.Parse("2026-09-30T17:00:00Z") };
            var s = Sample("none") with { History = new([e], true, now) };
            Equal(1, calendar.BuildMonth(s, new(2026, 9, 1), utc, now).Days.Single(d => d.Date == new DateOnly(2026, 9, 30)).Events.Count);
            Equal(1, calendar.BuildMonth(s, new(2026, 10, 1), china, now).Days.Single(d => d.Date == new DateOnly(2026, 10, 1)).Events.Count);
        });
        tests.Check("Forecast stays separate from completed history", () => {
            var s = Sample("future") with { History = new([], true, now) };
            var m = calendar.BuildMonth(s, new(2026, 10, 1), utc, now);
            Equal(1, m.Days.Count(d => d.Forecast is not null)); Equal(0, m.Days.Sum(d => d.Events.Count));
        });
        tests.Check("Past expected time is not a future calendar overlay", () => Equal(0, calendar.BuildMonth(Sample("past"), new(2026, 10, 1), utc, now).Days.Count(d => d.Forecast is not null)));
        tests.Check("Unknown scheduled time does not get a date cell", () => Equal(0, calendar.BuildMonth(Sample("unknown"), new(2026, 10, 1), utc, now).Days.Count(d => d.Forecast is not null)));
        tests.Check("All same-day event types are retained", () => {
            var m = calendar.BuildMonth(Sample("multi"), new(2026, 10, 1), utc, now);
            var day = m.Days.Single(d => d.Date == new DateOnly(2026, 10, 2)); Equal(3, day.Events.Count); True(day.Events.Any(e => e.Type == ResetType.Unknown));
        });

        return tests.Result;
    }

}
