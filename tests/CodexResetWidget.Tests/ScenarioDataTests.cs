using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class ScenarioDataTests
{
    public static (int Passed, int Failed) Run()
    {
        var tests = new TestSuite("ScenarioData");
        var now = DateTimeOffset.Parse("2026-10-02T15:00:00Z");
        var utc = TimeZoneInfo.Utc;
        var china = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var service = new ResetStateService();
        var calendar = new CalendarService();
        WidgetSnapshot Sample(string scenario) => DemoData.Create(scenario, now, utc);

        tests.Check("Observed record has no fabricated author or URL", () => {
            var e = Sample("multi").History.Events.Single(e => e.SourceKind == SourceKind.Observed); True(e.Author is null && e.SourceUrl is null && e.Handle is null);
        });
        tests.Check("All demo scenarios evaluate without exceptions", () => {
            foreach (var scenario in DemoData.Scenarios) { var s = Sample(scenario.Id); _ = service.Evaluate(s, now); _ = calendar.BuildMonth(s, new(2026, 10, 1), china, now); }
        });

        return tests.Result;
    }

}
