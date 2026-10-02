using CodexResetWidget.Domain;

namespace CodexResetWidget.Application;

public sealed record DemoScenario(string Id, string Label);

public static class DemoData
{
    public static IReadOnlyList<DemoScenario> Scenarios { get; } =
    [
        new("future", "Upcoming announcement"), new("arriving", "Target in 12 seconds"), new("past", "Expected time reached"),
        new("unknown", "Time not specified"), new("watch", "Watch signal only"), new("none", "No announcement"),
        new("missing", "Announcement withdrawn"), new("retimed", "Announced time changed"), new("cache", "Cache and update failure"),
        new("loading", "Initial loading"), new("error", "First launch offline"), new("long", "Long announcement"),
        new("multi", "Same-day events")
    ];

    public static WidgetSnapshot Create(string scenario, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var history = new List<ResetEvent>();
        var month = new DateTime(local.Year, local.Month, 1, 12, 0, 0, DateTimeKind.Unspecified);
        foreach (var day in new[] { 3, 5, 8, 12, 16, 22, 26, 29 })
        {
            if (day > DateTime.DaysInMonth(local.Year, local.Month)) continue;
            var published = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(month.AddDays(day - 1), zone));
            history.Add(new(new("demo", $"history-{day}"), day % 2 == 0 ? ResetType.Regular : ResetType.Banked,
                EventStatus.Recorded, SourceKind.Announcement, published, null,
                day % 2 == 0 ? "Global reset announced. This is sample text for the interface preview."
                    : "Banked reset announced. This sample does not confirm an individual account balance.",
                null, "Tibo", "@thsottiaux"));
        }
        var todayUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local.Date.AddHours(10), zone));
        history.Add(new(new("demo", "today-observed"), ResetType.Unknown, EventStatus.Recorded,
            SourceKind.Observed, todayUtc, null, "Sample observed record with no original post, author or source link.", RawType: "observed"));
        if (scenario == "multi")
        {
            history.Add(new(new("demo", "today-regular"), ResetType.Regular, EventStatus.Recorded,
                SourceKind.Announcement, todayUtc.AddMinutes(5), null, "Sample global reset announcement.", null, "Tibo", "@thsottiaux"));
            history.Add(new(new("demo", "today-banked"), ResetType.Banked, EventStatus.Recorded,
                SourceKind.Announcement, todayUtc.AddMinutes(10), null, "Sample banked reset announcement.", null, "Tibo", "@thsottiaux"));
        }
        var target = scenario switch
        {
            "arriving" => nowUtc.AddSeconds(12), "past" => nowUtc.AddHours(-2),
            "missing" => nowUtc.AddMinutes(-30), "retimed" => nowUtc.AddDays(1).AddHours(2),
            _ => nowUtc.AddHours(13).AddMinutes(22)
        };
        var text = "Global reset landing tomorrow. This is a sample announcement for the widget preview; the date and countdown are demo data.";
        if (scenario is "past" or "missing") text = "Global reset scheduled for earlier today. This is sample announcement text; it does not confirm that any individual account received a reset.";
        if (scenario == "long") text = string.Join("\n\n", Enumerable.Repeat(
            "Global reset announcement preview. The widget converts the structured API timestamp to your computer's time zone.\nThis long sample checks wrapping, scrolling and reading; dates and markers are test data.", 5));
        var announcement = new ResetEvent(new("demo", "current"), ResetType.Regular,
            EventStatus.Scheduled, SourceKind.Announcement, nowUtc.AddHours(scenario == "past" ? -3 : -1),
            scenario == "unknown" ? null : target, text, null, "Tibo", "@thsottiaux");
        var health = new DataHealth(LastSuccessAtUtc: nowUtc);
        if (scenario == "cache") health = new(IsFromCache: true, IsStale: true,
            LastSuccessAtUtc: nowUtc.AddHours(-1), LastError: "Error.UpdateFailed");
        if (scenario is "loading" or "error")
            return new(1, null, new([], false, null), null,
                scenario == "loading" ? new(IsLoading: true) : new(LastError: "Error.UpdateFailed"),
                new(IsLoading: scenario == "loading"));
        var show = scenario is not ("none" or "watch" or "missing");
        return new(1, new(show ? announcement : null,
            scenario == "watch" ? new("demo-watch", nowUtc.AddMinutes(30)) : null,
            history.OrderByDescending(e => e.AnnouncedAtUtc).First(), nowUtc, nowUtc),
            new(history.ToArray(), true, nowUtc),
            scenario == "missing" ? new(announcement, true) : scenario == "retimed" ? new(announcement, false, true) : null,
            health, new(LastSuccessAtUtc: nowUtc));
    }
}
