using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;
using CodexResetWidget.Infrastructure.Storage;
using CodexResetWidget.Platform.Clock;
using static TestAssert;

internal static class WindowPlacementTests
{
    public static (int Passed, int Failed) Run()
    {
        var tests = new TestSuite("WindowPlacement");
        var monitors = new MonitorArea[]
        {
            new("primary", new(0, 0, 1920, 1040), 1),
            new("right", new(1920, 0, 2560, 1400), 1.5),
            new("left", new(-1920, -200, 1920, 1040), 2)
        };
        tests.Check("Default settings are valid", () => Assert(PlacementPolicy.Valid(new())));
        tests.Check("Saved monitor is restored at its own DPI", () =>
        {
            var (monitor, rect) = PlacementPolicy.Restore(new(Width: 440, ExpandedHeight: 1020, Compact: false, Monitor: "right", PhysicalLeft: 2100, PhysicalTop: 100), monitors);
            Assert(monitor.Name == "right" && rect.Width == 660 && rect.Height == 1400 && rect.X == 2100 && rect.Y == 0);
        });
        tests.Check("Negative monitor coordinates remain valid", () =>
        {
            var (monitor, rect) = PlacementPolicy.Restore(new(Monitor: "left", PhysicalLeft: -1700, PhysicalTop: -100, Compact: true), monitors);
            Assert(monitor.Name == "left" && rect.X == -1700 && rect.Y == -100 && rect.Width == 800 && rect.Height == 584);
        });
        tests.Check("Removed monitor falls back to nearest available monitor", () =>
        {
            var (monitor, rect) = PlacementPolicy.Restore(new(Monitor: "removed", PhysicalLeft: 99999, PhysicalTop: 200), monitors);
            Assert(monitor.Name == "right" && rect.X + rect.Width == 4480 && rect.Y >= 0);
        });
        tests.Check("Oversized window is clamped to working area", () =>
        {
            var (_, rect) = PlacementPolicy.Restore(new(Width: 4000, ExpandedHeight: 5000, Compact: false, Monitor: "primary"), monitors);
            Assert(rect == monitors[0].WorkArea);
        });
        tests.Check("New window is centered within working area", () =>
        {
            var (_, rect) = PlacementPolicy.Restore(new(Monitor: "primary", Compact: true), monitors);
            Assert(rect.X == 760 && rect.Y == 374);
        });
        foreach (var percent in new[] { 100, 150, 200 })
            tests.Check($"Placement fits {percent} percent scaled work area", () =>
            {
                var area = new DesktopRect(0, 0, 1920, 1040);
                var (_, rect) = PlacementPolicy.Restore(new(PhysicalLeft: 1800, PhysicalTop: 1000), [new("test", area, percent / 100d)]);
                Assert(rect.X >= 0 && rect.Y >= 0 && rect.X + rect.Width <= area.Width && rect.Y + rect.Height <= area.Height);
            });
        tests.Check("Nonfinite and invalid dimensions are rejected", () =>
        {
            Assert(!PlacementPolicy.Valid(new(Width: double.NaN)) && !PlacementPolicy.Valid(new(ExpandedHeight: double.PositiveInfinity))
                && !PlacementPolicy.Valid(new(PhysicalLeft: double.NegativeInfinity)) && !PlacementPolicy.Valid(new(Width: -1)));
        });
        tests.Check("Unknown theme and future schema are rejected", () => Assert(!PlacementPolicy.Valid(new(Theme: "Other")) && !PlacementPolicy.Valid(new(SchemaVersion: 99))));

        return tests.Result;
    }

}
