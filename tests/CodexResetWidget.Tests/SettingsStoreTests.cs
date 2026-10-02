using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class SettingsStoreTests
{
    public static (int Passed, int Failed) Run()
    {
        var tests = new TestSuite("SettingsStore");
        var directory = Path.Combine(Path.GetTempPath(), "CodexResetWidget-settings-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(directory);
        tests.Check("Missing settings use defaults", () => Assert(store.Load() == new DesktopSettings() && store.Warning is null));
        tests.Check("Settings round trip retains mode theme pin size and physical position", () =>
        {
            var settings = new DesktopSettings(Width: 520, ExpandedHeight: 900, Compact: true, Pinned: true, Theme: "Dark",
                Monitor: "left", PhysicalLeft: -1500, PhysicalTop: -100, TrayHintShown: true);
            store.Save(settings); Assert(store.Load() == settings && store.Warning is null && !Directory.GetFiles(directory, "*.tmp").Any());
        });
        tests.Check("Malformed settings safely fall back", () =>
        { File.WriteAllText(store.FilePath, "{broken"); Assert(store.Load() == new DesktopSettings() && store.Warning is not null); });
        tests.Check("Incompatible settings schema safely falls back", () =>
        { File.WriteAllText(store.FilePath, "{\"schemaVersion\":99}"); Assert(store.Load() == new DesktopSettings()); });
        tests.Check("Invalid setting save preserves valid existing file", () =>
        {
            store.Save(new()); var previous = File.ReadAllText(store.FilePath);
            store.Save(new(Width: double.NaN)); Assert(File.ReadAllText(store.FilePath) == previous && store.Warning is not null);
        });
        tests.Check("Unwritable settings location does not crash", () =>
        {
            var blockingFile = Path.Combine(directory, "not-a-directory"); File.WriteAllText(blockingFile, "x");
            var blocked = new SettingsStore(blockingFile); blocked.Save(new()); Assert(blocked.Warning is not null);
        });

        return tests.Result;
    }

}
