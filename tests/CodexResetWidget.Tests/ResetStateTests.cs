using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class ResetStateTests
{
    public static (int Passed, int Failed) Run()
    {
        var tests = new TestSuite("ResetState");
        var now = DateTimeOffset.Parse("2026-10-02T15:00:00Z");
        var utc = TimeZoneInfo.Utc;
        var service = new ResetStateService();
        WidgetSnapshot Sample(string scenario) => DemoData.Create(scenario, now, utc);

        tests.Check("Future countdown uses absolute target", () => {
            var s = Sample("future"); var b = service.Evaluate(s, now);
            Equal(BoardStatus.Countdown, b.Status); Equal(TimeSpan.FromHours(13) + TimeSpan.FromMinutes(22), b.Remaining);
        });
        tests.Check("Exact target switches to recent expected time", () => {
            var s = Sample("future"); var b = service.Evaluate(s, s.Status!.ScheduledReset!.ScheduledForUtc!.Value);
            Equal(BoardStatus.RecentAnnouncement, b.Status); Equal(TimeSpan.Zero, b.Remaining);
        });
        tests.Check("Resume after target never yields negative countdown", () => {
            var b = service.Evaluate(Sample("future"), now.AddDays(4)); Equal(BoardStatus.RecentAnnouncement, b.Status); Equal(TimeSpan.Zero, b.Remaining);
        });
        tests.Check("Target missing does not create countdown", () => Equal(BoardStatus.TimeUnknown, service.Evaluate(Sample("unknown"), now).Status));
        tests.Check("Active watch never becomes countdown", () => Equal(BoardStatus.Watch, service.Evaluate(Sample("watch"), now).Status));
        tests.Check("Expired watch is ignored", () => Equal(BoardStatus.NoAnnouncement, service.Evaluate(Sample("watch"), now.AddHours(1)).Status));
        tests.Check("No announcement remains unknown", () => Equal(BoardStatus.NoAnnouncement, service.Evaluate(Sample("none"), now).Status));
        tests.Check("Missing announcement retains prior expected date", () => {
            var b = service.Evaluate(Sample("missing"), now); Equal(BoardStatus.RecentAnnouncement, b.Status); True(b.AnnouncementUnconfirmed); True(b.RelatedEvent!.ScheduledForUtc is not null);
        });
        tests.Check("Missing future announcement retains flagged countdown", () => {
            var s = Sample("future"); var reset = s.Status!.ScheduledReset!;
            s = s with { Status = s.Status with { ScheduledReset = null }, Pending = new(reset, true) };
            var b = service.Evaluate(s, now); Equal(BoardStatus.Countdown, b.Status); True(b.AnnouncementUnconfirmed);
        });
        tests.Check("Time correction changes target but keeps ID", () => {
            var s = Sample("future"); var before = s.Status!.ScheduledReset!;
            var revised = before with { ScheduledForUtc = now.AddDays(2) };
            s = s with { Status = s.Status with { ScheduledReset = revised }, Pending = new(revised, false, true) };
            var b = service.Evaluate(s, now); Equal(TimeSpan.FromDays(2), b.Remaining); Equal(before.Key, b.RelatedEvent!.Key); True(b.TargetChanged);
        });
        tests.Check("New announcement overrides older past context", () => {
            var s = Sample("future") with { Pending = Sample("missing").Pending };
            var b = service.Evaluate(s, now); Equal(BoardStatus.Countdown, b.Status); True(!b.AnnouncementUnconfirmed);
        });
        tests.Check("Source record does not create account completion", () => {
            var s = Sample("missing"); var reset = s.Pending!.LastKnownEvent with { Status = EventStatus.Recorded };
            s = s with { Status = s.Status! with { LatestReset = reset }, History = new([reset], true, now) };
            Equal(BoardStatus.RecentAnnouncement, service.Evaluate(s, now).Status);
        });
        tests.Check("Cache failure preserves valid announcement", () => Equal(BoardStatus.Countdown, service.Evaluate(Sample("cache"), now).Status));
        tests.Check("No-cache error creates no invented date", () => True(service.Evaluate(Sample("error"), now).RelatedEvent is null));
        tests.Check("Watch expires at its exact boundary", () => Equal(BoardStatus.NoAnnouncement,
            service.Evaluate(Sample("watch"), now.AddMinutes(30)).Status));

        return tests.Result;
    }

}
