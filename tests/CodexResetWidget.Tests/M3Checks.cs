using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Storage;

internal static class M3Checks
{
    public static (int Passed, int Failed) Run()
    {
        var passed = 0; var failed = 0;
        void Check(string name, Action test)
        { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); } }
        void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
        var monitors = new MonitorArea[]
        {
            new("primary", new(0, 0, 1920, 1040), 1),
            new("right", new(1920, 0, 2560, 1400), 1.5),
            new("left", new(-1920, -200, 1920, 1040), 2)
        };
        Check("Default settings are valid", () => Assert(PlacementPolicy.Valid(new())));
        Check("Saved monitor is restored at its own DPI", () =>
        {
            var (monitor, rect) = PlacementPolicy.Restore(new(Monitor: "right", PhysicalLeft: 2100, PhysicalTop: 100), monitors);
            Assert(monitor.Name == "right" && rect.Width == 660 && rect.Height == 1400 && rect.X == 2100 && rect.Y == 0);
        });
        Check("Negative monitor coordinates remain valid", () =>
        {
            var (monitor, rect) = PlacementPolicy.Restore(new(Monitor: "left", PhysicalLeft: -1700, PhysicalTop: -100, Compact: true), monitors);
            Assert(monitor.Name == "left" && rect.X == -1700 && rect.Y == -100 && rect.Width == 880 && rect.Height == 800);
        });
        Check("Removed monitor falls back to nearest available monitor", () =>
        {
            var (monitor, rect) = PlacementPolicy.Restore(new(Monitor: "removed", PhysicalLeft: 99999, PhysicalTop: 200), monitors);
            Assert(monitor.Name == "right" && rect.X + rect.Width == 4480 && rect.Y >= 0);
        });
        Check("Oversized window is clamped to working area", () =>
        {
            var (_, rect) = PlacementPolicy.Restore(new(Width: 4000, ExpandedHeight: 5000, Monitor: "primary"), monitors);
            Assert(rect == monitors[0].WorkArea);
        });
        Check("New window is centered within working area", () =>
        {
            var (_, rect) = PlacementPolicy.Restore(new(Monitor: "primary", Compact: true), monitors);
            Assert(rect.X == 740 && rect.Y == 320);
        });
        foreach (var percent in new[] { 100, 150, 200 })
            Check($"Placement fits {percent} percent scaled work area", () =>
            {
                var area = new DesktopRect(0, 0, 1920, 1040);
                var (_, rect) = PlacementPolicy.Restore(new(PhysicalLeft: 1800, PhysicalTop: 1000), [new("test", area, percent / 100d)]);
                Assert(rect.X >= 0 && rect.Y >= 0 && rect.X + rect.Width <= area.Width && rect.Y + rect.Height <= area.Height);
            });
        Check("Nonfinite and invalid dimensions are rejected", () =>
        {
            Assert(!PlacementPolicy.Valid(new(Width: double.NaN)) && !PlacementPolicy.Valid(new(ExpandedHeight: double.PositiveInfinity))
                && !PlacementPolicy.Valid(new(PhysicalLeft: double.NegativeInfinity)) && !PlacementPolicy.Valid(new(Width: -1)));
        });
        Check("Unknown theme and future schema are rejected", () => Assert(!PlacementPolicy.Valid(new(Theme: "Other")) && !PlacementPolicy.Valid(new(SchemaVersion: 99))));
        var directory = Path.Combine(Path.GetTempPath(), "CodexResetWidget-settings-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(directory);
        Check("Missing settings use defaults", () => Assert(store.Load() == new DesktopSettings() && store.Warning is null));
        Check("Settings round trip retains mode theme pin size and physical position", () =>
        {
            var settings = new DesktopSettings(Width: 520, ExpandedHeight: 900, Compact: true, Pinned: true, Theme: "Dark",
                Monitor: "left", PhysicalLeft: -1500, PhysicalTop: -100, TrayHintShown: true);
            store.Save(settings); Assert(store.Load() == settings && store.Warning is null && !Directory.GetFiles(directory, "*.tmp").Any());
        });
        Check("Malformed settings safely fall back", () =>
        { File.WriteAllText(store.FilePath, "{broken"); Assert(store.Load() == new DesktopSettings() && store.Warning is not null); });
        Check("Incompatible settings schema safely falls back", () =>
        { File.WriteAllText(store.FilePath, "{\"schemaVersion\":99}"); Assert(store.Load() == new DesktopSettings()); });
        Check("Invalid setting save preserves valid existing file", () =>
        {
            store.Save(new()); var previous = File.ReadAllText(store.FilePath);
            store.Save(new(Width: double.NaN)); Assert(File.ReadAllText(store.FilePath) == previous && store.Warning is not null);
        });
        Check("Unwritable settings location does not crash", () =>
        {
            var blockingFile = Path.Combine(directory, "not-a-directory"); File.WriteAllText(blockingFile, "x");
            var blocked = new SettingsStore(blockingFile); blocked.Save(new()); Assert(blocked.Warning is not null);
        });
        return (passed, failed);
    }
}
