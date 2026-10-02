using CodexResetWidget.Application;
using CodexResetWidget.Domain;

var now = DateTimeOffset.Parse("2026-10-02T15:00:00Z");
var utc = TimeZoneInfo.Utc;
var china = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
var india = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
var service = new ResetStateService();
var calendar = new CalendarService();
var passed = 0;
var failed = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception error) { Console.WriteLine("FAIL " + name + ": " + error.Message); failed++; }
}
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
WidgetSnapshot Sample(string scenario) => DemoData.Create(scenario, now, utc);

Test("Future countdown uses absolute target", () => {
    var s = Sample("future"); var b = service.Evaluate(s, now);
    Equal(BoardStatus.Countdown, b.Status); Equal(TimeSpan.FromHours(13) + TimeSpan.FromMinutes(22), b.Remaining);
});
Test("Exact target switches to recent expected time", () => {
    var s = Sample("future"); var b = service.Evaluate(s, s.Status!.ScheduledReset!.ScheduledForUtc!.Value);
    Equal(BoardStatus.RecentAnnouncement, b.Status); Equal(TimeSpan.Zero, b.Remaining);
});
Test("Resume after target never yields negative countdown", () => {
    var b = service.Evaluate(Sample("future"), now.AddDays(4)); Equal(BoardStatus.RecentAnnouncement, b.Status); Equal(TimeSpan.Zero, b.Remaining);
});
Test("Target missing does not create countdown", () => Equal(BoardStatus.TimeUnknown, service.Evaluate(Sample("unknown"), now).Status));
Test("Active watch never becomes countdown", () => Equal(BoardStatus.Watch, service.Evaluate(Sample("watch"), now).Status));
Test("Expired watch is ignored", () => Equal(BoardStatus.NoAnnouncement, service.Evaluate(Sample("watch"), now.AddHours(1)).Status));
Test("No announcement remains unknown", () => Equal(BoardStatus.NoAnnouncement, service.Evaluate(Sample("none"), now).Status));
Test("Missing announcement retains prior expected date", () => {
    var b = service.Evaluate(Sample("missing"), now); Equal(BoardStatus.RecentAnnouncement, b.Status); True(b.AnnouncementUnconfirmed); True(b.RelatedEvent!.ScheduledForUtc is not null);
});
Test("Missing future announcement retains flagged countdown", () => {
    var s = Sample("future"); var reset = s.Status!.ScheduledReset!;
    s = s with { Status = s.Status with { ScheduledReset = null }, Pending = new(reset, true) };
    var b = service.Evaluate(s, now); Equal(BoardStatus.Countdown, b.Status); True(b.AnnouncementUnconfirmed);
});
Test("Time correction changes target but keeps ID", () => {
    var s = Sample("future"); var before = s.Status!.ScheduledReset!;
    var revised = before with { ScheduledForUtc = now.AddDays(2) };
    s = s with { Status = s.Status with { ScheduledReset = revised }, Pending = new(revised, false, true) };
    var b = service.Evaluate(s, now); Equal(TimeSpan.FromDays(2), b.Remaining); Equal(before.Key, b.RelatedEvent!.Key); True(b.TargetChanged);
});
Test("New announcement overrides older past context", () => {
    var s = Sample("future") with { Pending = Sample("missing").Pending };
    var b = service.Evaluate(s, now); Equal(BoardStatus.Countdown, b.Status); True(!b.AnnouncementUnconfirmed);
});
Test("Source record does not create account completion", () => {
    var s = Sample("missing"); var reset = s.Pending!.LastKnownEvent with { Status = EventStatus.Recorded };
    s = s with { Status = s.Status! with { LatestReset = reset }, History = new([reset], true, now) };
    Equal(BoardStatus.RecentAnnouncement, service.Evaluate(s, now).Status);
});
Test("Cache failure preserves valid announcement", () => Equal(BoardStatus.Countdown, service.Evaluate(Sample("cache"), now).Status));
Test("No-cache error creates no invented date", () => True(service.Evaluate(Sample("error"), now).RelatedEvent is null));
Test("UTC to China crosses date correctly", () => Equal("10 月 3 日 01:00", TimeDisplay.DateTime(DateTimeOffset.Parse("2026-10-02T17:00:00Z"), china)));
Test("Half-hour offset is preserved", () => { Equal("10 月 2 日 22:30", TimeDisplay.DateTime(DateTimeOffset.Parse("2026-10-02T17:00:00Z"), india)); True(TimeDisplay.ZoneLabel(now, india, false).Contains("05:30")); });
Test("Offset input and UTC represent same instant", () => {
    var a = DateTimeOffset.Parse("2026-10-03T01:00:00+08:00"); var b = DateTimeOffset.Parse("2026-10-02T17:00:00Z");
    Equal(a, b); Equal(TimeDisplay.DateTime(a, india), TimeDisplay.DateTime(b, india));
});
Test("Pacific winter label uses standard offset", () => True(TimeDisplay.ZoneLabel(DateTimeOffset.Parse("2026-01-15T17:00:00Z"), pacific, false).Contains("08:00")));
Test("Pacific summer label uses daylight offset", () => True(TimeDisplay.ZoneLabel(DateTimeOffset.Parse("2026-07-15T17:00:00Z"), pacific, false).Contains("07:00")));
Test("Fall DST repeated hour remains distinguishable", () => {
    var a = DateTimeOffset.Parse("2026-11-01T08:30:00Z"); var b = a.AddHours(1);
    Equal(TimeDisplay.DateTime(a, pacific), TimeDisplay.DateTime(b, pacific)); True(TimeDisplay.ZoneLabel(a, pacific, false) != TimeDisplay.ZoneLabel(b, pacific, false));
});
Test("Spring DST skips nonexistent local hour", () => {
    Equal("3 月 8 日 01:30", TimeDisplay.DateTime(DateTimeOffset.Parse("2026-03-08T09:30:00Z"), pacific));
    Equal("3 月 8 日 03:30", TimeDisplay.DateTime(DateTimeOffset.Parse("2026-03-08T10:30:00Z"), pacific));
});
Test("Countdown includes days and clamps negative", () => { Equal("2 天 03:04:05", TimeDisplay.Countdown(new TimeSpan(2, 3, 4, 5))); Equal("00:00:00", TimeDisplay.Countdown(TimeSpan.FromSeconds(-1))); });
Test("Positive fraction does not show zero before target", () => Equal("00:00:01", TimeDisplay.Countdown(TimeSpan.FromMilliseconds(1))));
Test("Leap February has 29 in-month days and Monday start", () => {
    var m = calendar.BuildMonth(Sample("none"), new(2024, 2, 1), utc, now);
    Equal(29, m.Days.Count(d => d.IsCurrentMonth)); Equal(DayOfWeek.Monday, m.Days[0].Date.DayOfWeek); Equal(0, m.Days.Count % 7);
});
Test("Six-week month keeps all 42 cells", () => Equal(42, calendar.BuildMonth(Sample("none"), new(2026, 3, 1), utc, now).Days.Count));
Test("Four-week February stays valid", () => Equal(28, calendar.BuildMonth(Sample("none"), new(2021, 2, 1), utc, now).Days.Count));
Test("History date uses publication not target", () => {
    var e = Sample("future").Status!.ScheduledReset! with { Status = EventStatus.Recorded, AnnouncedAtUtc = DateTimeOffset.Parse("2026-09-30T17:00:00Z"), ScheduledForUtc = DateTimeOffset.Parse("2026-10-05T17:00:00Z") };
    var s = Sample("none") with { History = new([e], true, now) };
    var m = calendar.BuildMonth(s, new(2026, 10, 1), china, now);
    Equal(1, m.Days.Single(d => d.Date == new DateOnly(2026, 10, 1)).Events.Count);
    Equal(0, m.Days.Single(d => d.Date == new DateOnly(2026, 10, 6)).Events.Count);
});
Test("Time-zone conversion can move history across month", () => {
    var e = Sample("none").History.Events[0] with { AnnouncedAtUtc = DateTimeOffset.Parse("2026-09-30T17:00:00Z") };
    var s = Sample("none") with { History = new([e], true, now) };
    Equal(1, calendar.BuildMonth(s, new(2026, 9, 1), utc, now).Days.Single(d => d.Date == new DateOnly(2026, 9, 30)).Events.Count);
    Equal(1, calendar.BuildMonth(s, new(2026, 10, 1), china, now).Days.Single(d => d.Date == new DateOnly(2026, 10, 1)).Events.Count);
});
Test("Forecast stays separate from completed history", () => {
    var s = Sample("future") with { History = new([], true, now) };
    var m = calendar.BuildMonth(s, new(2026, 10, 1), utc, now);
    Equal(1, m.Days.Count(d => d.Forecast is not null)); Equal(0, m.Days.Sum(d => d.Events.Count));
});
Test("Past expected time is not a future calendar overlay", () => Equal(0, calendar.BuildMonth(Sample("past"), new(2026, 10, 1), utc, now).Days.Count(d => d.Forecast is not null)));
Test("Unknown scheduled time does not get a date cell", () => Equal(0, calendar.BuildMonth(Sample("unknown"), new(2026, 10, 1), utc, now).Days.Count(d => d.Forecast is not null)));
Test("All same-day event types are retained", () => {
    var m = calendar.BuildMonth(Sample("multi"), new(2026, 10, 1), utc, now);
    var day = m.Days.Single(d => d.Date == new DateOnly(2026, 10, 2)); Equal(3, day.Events.Count); True(day.Events.Any(e => e.Type == ResetType.Unknown));
});
Test("Observed record has no fabricated author or URL", () => {
    var e = Sample("multi").History.Events.Single(e => e.SourceKind == SourceKind.Observed); True(e.Author is null && e.SourceUrl is null && e.Handle is null);
});
Test("Watch expires at its exact boundary", () => Equal(BoardStatus.NoAnnouncement,
    service.Evaluate(Sample("watch"), now.AddMinutes(30)).Status));
Test("All demo scenarios evaluate without exceptions", () => {
    foreach (var scenario in DemoData.Scenarios) { var s = Sample(scenario.Id); _ = service.Evaluate(s, now); _ = calendar.BuildMonth(s, new(2026, 10, 1), china, now); }
});
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
