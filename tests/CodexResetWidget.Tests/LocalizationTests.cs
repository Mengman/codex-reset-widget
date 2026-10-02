using System.Globalization;
using System.Text;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Storage;
using static TestAssert;

internal static class LocalizationTests
{
    public static (int Passed, int Failed) Run()
    {
        var tests = new TestSuite("Localization");
        var original = L10n.Mode;
        try
        {
            foreach (var name in new[] { "zh-CN", "zh-TW", "zh-HK", "zh-Hans", "zh-Hant" })
                tests.Check($"System language {name} selects Simplified Chinese", () =>
                    Equal("zh-CN", L10n.Resolve(LanguageMode.System, CultureInfo.GetCultureInfo(name))));
            foreach (var name in new[] { "en-US", "en-GB", "fr-FR", "ja-JP", "ar-SA" })
                tests.Check($"System language {name} selects English", () =>
                    Equal("en", L10n.Resolve(LanguageMode.System, CultureInfo.GetCultureInfo(name))));
            tests.Check("Manual English overrides Chinese system language", () =>
                Equal("en", L10n.Resolve(LanguageMode.English, CultureInfo.GetCultureInfo("zh-TW"))));
            tests.Check("Manual Chinese overrides English system language", () =>
                Equal("zh-CN", L10n.Resolve(LanguageMode.SimplifiedChinese, CultureInfo.GetCultureInfo("en-US"))));
            tests.Check("Both languages contain identical resource keys", () =>
                Assert(L10n.Resources("en").Keys.Order().SequenceEqual(L10n.Resources("zh-CN").Keys.Order())));
            tests.Check("Localized format placeholders match in both languages", () =>
            {
                foreach (var key in L10n.Keys)
                {
                    var english = CompositeFormat.Parse(L10n.Resources("en")[key]);
                    var chinese = CompositeFormat.Parse(L10n.Resources("zh-CN")[key]);
                    Equal(english.MinimumArgumentCount, chinese.MinimumArgumentCount);
                }
            });
            tests.Check("Language changes notify once and no-op selections do not notify", () =>
            {
                L10n.Apply(LanguageMode.English); var changes = 0;
                void Changed(object? sender, EventArgs args) => changes++;
                L10n.Changed += Changed;
                try { L10n.Apply(LanguageMode.SimplifiedChinese); L10n.Apply(LanguageMode.SimplifiedChinese); Equal(1, changes); }
                finally { L10n.Changed -= Changed; }
            });
            tests.Check("System mode responds to updated OS display language", () =>
            {
                L10n.Apply(LanguageMode.System, CultureInfo.GetCultureInfo("zh-TW")); Equal("zh-CN", L10n.Locale);
                L10n.Apply(LanguageMode.System, CultureInfo.GetCultureInfo("de-DE")); Equal("en", L10n.Locale);
            });
            tests.Check("Locale affects dates but preserves the absolute countdown", () =>
            {
                var instant = DateTimeOffset.Parse("2026-10-02T17:00:00Z");
                var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                L10n.Apply(LanguageMode.English); Equal("Oct 3, 01:00", TimeDisplay.DateTime(instant, zone));
                var remaining = TimeDisplay.Countdown(TimeSpan.FromHours(2));
                L10n.Apply(LanguageMode.SimplifiedChinese);
                Equal("10 \u6708 3 \u65e5 01:00", TimeDisplay.DateTime(instant, zone));
                Equal(remaining, TimeDisplay.Countdown(TimeSpan.FromHours(2)));
                Equal("2026 \u5e74 10 \u6708", TimeDisplay.Month(new(2026, 10, 1)));
            });
            tests.Check("Cached resource keys render in the newly selected language", () =>
            {
                var message = L10n.Pack("Error.HistoryRowsSkipped", 3);
                L10n.Apply(LanguageMode.English); Equal("Skipped 3 history records with invalid fields.", L10n.Message(message));
                L10n.Apply(LanguageMode.SimplifiedChinese); Assert(L10n.Message(message).Contains("3"));
                Assert(L10n.Message(message) != "Skipped 3 history records with invalid fields.");
            });
            tests.Check("Legacy rendered cache messages can change language", () =>
            {
                var legacy = L10n.Resources("zh-CN")["Error.CacheRead"];
                L10n.Apply(LanguageMode.English); Equal(L10n.Resources("en")["Error.CacheRead"], L10n.Message(legacy));
            });
            tests.Check("Malformed cached message arguments cannot break the UI", () =>
            {
                L10n.Apply(LanguageMode.English);
                Equal(L10n.Get("Error.HistoryRowsSkipped"), L10n.Message("Error.HistoryRowsSkipped|[]"));
                Equal(L10n.Get("Error.HistoryRowsSkipped"), L10n.Message("Error.HistoryRowsSkipped|invalid"));
            });
            tests.Check("Manual language selection persists in schema-1 settings", () =>
            {
                var root = Path.Combine(Path.GetTempPath(), "CodexResetWidget-language-" + Guid.NewGuid().ToString("N"));
                var store = new SettingsStore(root); var settings = new DesktopSettings(Language: "SimplifiedChinese");
                store.Save(settings); Equal(settings, store.Load());
                Assert(!PlacementPolicy.Valid(settings with { Language = "unsupported" }));
            });
        }
        finally { L10n.Apply(original, CultureInfo.CurrentUICulture); }
        return tests.Result;
    }
}
