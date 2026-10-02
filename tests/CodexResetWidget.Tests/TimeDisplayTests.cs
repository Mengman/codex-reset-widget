using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class TimeDisplayTests
{
    public static (int Passed, int Failed) Run()
    {
        var tests = new TestSuite("TimeDisplay");
        var now = DateTimeOffset.Parse("2026-10-02T15:00:00Z");
        var china = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var india = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        tests.Check("UTC to China crosses date correctly", () => Equal("10 月 3 日 01:00", TimeDisplay.DateTime(DateTimeOffset.Parse("2026-10-02T17:00:00Z"), china)));
        tests.Check("Half-hour offset is preserved", () => { Equal("10 月 2 日 22:30", TimeDisplay.DateTime(DateTimeOffset.Parse("2026-10-02T17:00:00Z"), india)); True(TimeDisplay.ZoneLabel(now, india, false).Contains("05:30")); });
        tests.Check("Offset input and UTC represent same instant", () => {
            var a = DateTimeOffset.Parse("2026-10-03T01:00:00+08:00"); var b = DateTimeOffset.Parse("2026-10-02T17:00:00Z");
            Equal(a, b); Equal(TimeDisplay.DateTime(a, india), TimeDisplay.DateTime(b, india));
        });
        tests.Check("Pacific winter label uses standard offset", () => True(TimeDisplay.ZoneLabel(DateTimeOffset.Parse("2026-01-15T17:00:00Z"), pacific, false).Contains("08:00")));
        tests.Check("Pacific summer label uses daylight offset", () => True(TimeDisplay.ZoneLabel(DateTimeOffset.Parse("2026-07-15T17:00:00Z"), pacific, false).Contains("07:00")));
        tests.Check("Fall DST repeated hour remains distinguishable", () => {
            var a = DateTimeOffset.Parse("2026-11-01T08:30:00Z"); var b = a.AddHours(1);
            Equal(TimeDisplay.DateTime(a, pacific), TimeDisplay.DateTime(b, pacific)); True(TimeDisplay.ZoneLabel(a, pacific, false) != TimeDisplay.ZoneLabel(b, pacific, false));
        });
        tests.Check("Spring DST skips nonexistent local hour", () => {
            Equal("3 月 8 日 01:30", TimeDisplay.DateTime(DateTimeOffset.Parse("2026-03-08T09:30:00Z"), pacific));
            Equal("3 月 8 日 03:30", TimeDisplay.DateTime(DateTimeOffset.Parse("2026-03-08T10:30:00Z"), pacific));
        });
        tests.Check("Countdown includes days and clamps negative", () => { Equal("2 天 03:04:05", TimeDisplay.Countdown(new TimeSpan(2, 3, 4, 5))); Equal("00:00:00", TimeDisplay.Countdown(TimeSpan.FromSeconds(-1))); });
        tests.Check("Positive fraction does not show zero before target", () => Equal("00:00:01", TimeDisplay.Countdown(TimeSpan.FromMilliseconds(1))));

        return tests.Result;
    }

}
