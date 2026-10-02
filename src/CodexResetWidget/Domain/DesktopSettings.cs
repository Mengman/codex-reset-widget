namespace CodexResetWidget.Domain;

public sealed record DesktopSettings(int SchemaVersion = 1, double Width = 400, double ExpandedHeight = 840,
    bool Compact = true, bool Pinned = false, string Theme = "System", string? Monitor = null,
    double? PhysicalLeft = null, double? PhysicalTop = null, bool TrayHintShown = false);
public readonly record struct DesktopRect(double X, double Y, double Width, double Height);
public sealed record MonitorArea(string Name, DesktopRect WorkArea, double Scale);

public static class PlacementPolicy
{
    public static bool Valid(DesktopSettings settings) => settings.SchemaVersion == 1
        && double.IsFinite(settings.Width) && settings.Width is >= 370 and <= 4000
        && double.IsFinite(settings.ExpandedHeight) && settings.ExpandedHeight is >= 260 and <= 5000
        && (settings.PhysicalLeft is null || double.IsFinite(settings.PhysicalLeft.Value))
        && (settings.PhysicalTop is null || double.IsFinite(settings.PhysicalTop.Value))
        && settings.Theme is "System" or "Light" or "Dark";

    public static (MonitorArea Monitor, DesktopRect Bounds) Restore(DesktopSettings settings, IReadOnlyList<MonitorArea> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("No available monitor", nameof(monitors));
        var x = settings.PhysicalLeft ?? monitors[0].WorkArea.X;
        var y = settings.PhysicalTop ?? monitors[0].WorkArea.Y;
        var monitor = monitors.FirstOrDefault(m => m.Name == settings.Monitor)
            ?? monitors.MinBy(m => Distance(x, y, m.WorkArea))!;
        var bounds = monitor.WorkArea;
        var width = Math.Min(settings.Width * monitor.Scale, bounds.Width);
        var height = Math.Min((settings.Compact ? 292 : settings.ExpandedHeight) * monitor.Scale, bounds.Height);
        if (settings.PhysicalLeft is null || settings.PhysicalTop is null)
        { x = bounds.X + (bounds.Width - width) / 2; y = bounds.Y + (bounds.Height - height) / 2; }
        return (monitor, Fit(new(x, y, width, height), bounds));
    }

    public static DesktopRect Fit(DesktopRect window, DesktopRect area)
    {
        var width = Math.Min(window.Width, area.Width);
        var height = Math.Min(window.Height, area.Height);
        return new(Math.Clamp(window.X, area.X, area.X + area.Width - width),
            Math.Clamp(window.Y, area.Y, area.Y + area.Height - height), width, height);
    }
    private static double Distance(double x, double y, DesktopRect area)
    {
        var dx = x - Math.Clamp(x, area.X, area.X + area.Width);
        var dy = y - Math.Clamp(y, area.Y, area.Y + area.Height);
        return dx * dx + dy * dy;
    }
}
