namespace CodexResetWidget.Platform;

public interface ITimeZoneService
{
    TimeZoneInfo CurrentZone { get; }
    bool FollowsSystem { get; }
    event EventHandler? ZoneChanged;
    void Refresh();
}

public sealed class SystemTimeZoneService : ITimeZoneService
{
    private TimeZoneInfo _systemZone = TimeZoneInfo.Local;
    private TimeZoneInfo? _demoZone;
    public TimeZoneInfo CurrentZone => _demoZone ?? _systemZone;
    public bool FollowsSystem => _demoZone is null;
    public event EventHandler? ZoneChanged;

    public void Refresh()
    {
        TimeZoneInfo.ClearCachedData();
        var next = TimeZoneInfo.Local;
        var changed = next.Id != _systemZone.Id || !next.HasSameRules(_systemZone);
        _systemZone = next;
        if (changed && FollowsSystem) ZoneChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectDemoZone(string? id)
    {
        _demoZone = id is null ? null : TimeZoneInfo.FindSystemTimeZoneById(id);
        Refresh();
        ZoneChanged?.Invoke(this, EventArgs.Empty);
    }
}
