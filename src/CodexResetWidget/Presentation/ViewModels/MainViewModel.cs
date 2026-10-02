using System.Windows.Input;
using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using CodexResetWidget.Platform;
using CodexResetWidget.Platform.Clock;

namespace CodexResetWidget.Presentation.ViewModels;

public sealed record ZoneOption(string Label, string? Id);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IClock _clock;
    private readonly SystemTimeZoneService _zones;
    private readonly ResetStateService _states = new();
    private readonly CalendarService _calendar = new();
    private readonly SyncController? _sync;
    private DemoScenario _scenario = DemoData.Scenarios[0];
    private ZoneOption _zoneOption;
    private WidgetSnapshot _snapshot;
    private bool _compact;
    private bool _pinned;
    private bool _toolsOpen;
    private DateOnly _lastToday;
    public IReadOnlyList<DemoScenario> Scenarios => DemoData.Scenarios;
    public IReadOnlyList<ZoneOption> Zones { get; } =
    [new("跟随电脑时区", null), new("UTC+08:00 · 中国", "China Standard Time"),
     new("UTC+05:30 · 印度", "India Standard Time"), new("美国太平洋 · 夏令时", "Pacific Standard Time"), new("UTC", "UTC")];
    public BoardViewModel Board { get; } = new();
    public AnnouncementViewModel Announcement { get; } = new();
    public CalendarViewModel Calendar { get; } = new();
    public WidgetSnapshot Snapshot => _snapshot;
    public TimeZoneInfo CurrentZone => _zones.CurrentZone;
    public bool IsDemo => _sync is null;
    public string DemoLabel => IsDemo ? "演示模式 · 模拟数据 · 不查询个人额度" : "公开公告 · 实际额度以 Codex 为准";
    public string FooterTime => _snapshot.StatusHealth.IsLoading ? "正在检查公告…" : _snapshot.StatusHealth.LastSuccessAtUtc is { } at
        ? $"{(IsDemo ? "模拟检查于" : "公告检查于")} {TimeDisplay.DateTime(at, _zones.CurrentZone)}" : "尚无成功检查";
    public string HistoryHealthText => IsDemo ? "" : _snapshot.HistoryHealth.IsLoading ? "正在加载历史记录…"
        : _snapshot.HistoryHealth.LastError is { } error ? $"历史：{error}" : _snapshot.HistoryHealth.LastSuccessAtUtc is { } at
        ? $"{(_snapshot.HistoryHealth.IsFromCache ? "缓存 · " : "")}{(_snapshot.History.IsComplete ? "历史检查于" : "历史尚未完整 · 检查于")} {TimeDisplay.DateTime(at, _zones.CurrentZone)}{(_snapshot.HistoryHealth.IsStale ? " · 可能已过期" : "")}" : "历史记录尚未加载";
    public string CacheWarning => _sync?.CacheWarning ?? "";
    public string UpstreamTime => _snapshot.Status?.GeneratedAtUtc is { } at
        ? $"上游响应生成于 {TimeDisplay.DateTime(at, _zones.CurrentZone, true)}；检查成功不代表上游已采集最新公告。" : "上游尚未提供生成时间。";
    public string FooterZone => TimeDisplay.ZoneLabel(_clock.UtcNow, _zones.CurrentZone, _zones.FollowsSystem);
    public bool IsCompact { get => _compact; private set { if (Set(ref _compact, value)) { Changed(nameof(IsExpanded)); Changed(nameof(ModeAction)); } } }
    public bool IsExpanded => !IsCompact;
    public string ModeAction => IsCompact ? "展开" : "收起";
    public bool IsPinned { get => _pinned; set => Set(ref _pinned, value); }
    public bool ToolsOpen { get => _toolsOpen; set => Set(ref _toolsOpen, value); }
    public DemoScenario SelectedScenario
    {
        get => _scenario;
        set { if (value is not null && Set(ref _scenario, value)) LoadScenario(); }
    }
    public ZoneOption SelectedZone
    {
        get => _zoneOption;
        set { if (value is not null && Set(ref _zoneOption, value)) _zones.SelectDemoZone(value.Id); }
    }
    public ICommand ToggleModeCommand { get; }
    public ICommand TogglePinCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SelectDayCommand { get; }
    public ICommand SelectEventCommand { get; }
    public ICommand PreviousMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand ReturnLatestCommand { get; }
    public ICommand OlderAnnouncementCommand { get; }
    public ICommand NewerAnnouncementCommand { get; }
    public ICommand ToggleToolsCommand { get; }
    public event EventHandler? ModeChanged;

    public MainViewModel(IClock clock, SystemTimeZoneService zones, SyncController? sync = null)
    {
        _clock = clock; _zones = zones; _zoneOption = Zones[0]; _sync = sync;
        _snapshot = sync?.Current ?? DemoData.Create(_scenario.Id, clock.UtcNow, zones.CurrentZone);
        Announcement.IsDemo = IsDemo;
        _lastToday = CalendarService.LocalDate(clock.UtcNow, zones.CurrentZone);
        Calendar.Initialize(_lastToday);
        ToggleModeCommand = new DelegateCommand(_ => { IsCompact = !IsCompact; ModeChanged?.Invoke(this, EventArgs.Empty); });
        TogglePinCommand = new DelegateCommand(_ => IsPinned = !IsPinned);
        RefreshCommand = new DelegateCommand(_ => { if (_sync is null) LoadScenario(); else _ = _sync.RefreshAsync(); });
        ToggleToolsCommand = new DelegateCommand(_ => { if (IsDemo) ToolsOpen = !ToolsOpen; });
        PreviousMonthCommand = new DelegateCommand(_ => MoveMonth(-1));
        NextMonthCommand = new DelegateCommand(_ => MoveMonth(1));
        TodayCommand = new DelegateCommand(_ => { var today = CalendarService.LocalDate(_clock.UtcNow, _zones.CurrentZone); Calendar.SetMonth(today); Calendar.Select(today); RebuildCalendar(); });
        SelectDayCommand = new DelegateCommand(p =>
        {
            if (p is not CalendarDayViewModel cell) return;
            Calendar.Select(cell.Day.Date);
            if (!cell.Day.IsCurrentMonth) Calendar.SetMonth(cell.Day.Date);
            RebuildCalendar();
            if (Calendar.Events.FirstOrDefault() is { } row) SelectEvent(row.Event);
        });
        SelectEventCommand = new DelegateCommand(p => { if (p is EventRowViewModel row) SelectEvent(row.Event); });
        ReturnLatestCommand = new DelegateCommand(_ => ShowLatest());
        OlderAnnouncementCommand = new DelegateCommand(_ => NavigateAnnouncement(1), _ => CanNavigateAnnouncement(1));
        NewerAnnouncementCommand = new DelegateCommand(_ => NavigateAnnouncement(-1), _ => CanNavigateAnnouncement(-1));
        _zones.ZoneChanged += OnZoneChanged;
        ShowLatest(); RebuildCalendar(); Tick();
    }

    private void LoadScenario()
    {
        if (!IsDemo) return;
        ApplySnapshot(DemoData.Create(_scenario.Id, _clock.UtcNow, _zones.CurrentZone));
    }

    public void ApplySnapshot(WidgetSnapshot snapshot)
    {
        if (snapshot.Revision < _snapshot.Revision && !IsDemo) return;
        _snapshot = snapshot;
        if (Announcement.SelectedEvent is { } key)
        {
            var reset = _snapshot.History.Events.FirstOrDefault(e => e.Key == key);
            if (reset is not null) Announcement.Show(reset, true, _zones.CurrentZone, _zones.FollowsSystem);
            else ShowLatest();
        }
        else ShowLatest();
        RebuildCalendar(); Tick(); Changed(nameof(FooterTime)); Changed(nameof(HistoryHealthText)); Changed(nameof(CacheWarning)); Changed(nameof(UpstreamTime));
    }

    private void ShowLatest()
    {
        var reset = _snapshot.Status?.ScheduledReset ?? _snapshot.Pending?.LastKnownEvent ?? _snapshot.Status?.LatestReset;
        Announcement.Show(reset, false, _zones.CurrentZone, _zones.FollowsSystem);
    }
    private void SelectEvent(ResetEvent reset) => Announcement.Show(reset, true, _zones.CurrentZone, _zones.FollowsSystem);
    private IReadOnlyList<ResetEvent> ReadingEvents() => new[] { _snapshot.Status?.ScheduledReset, _snapshot.Pending?.LastKnownEvent, _snapshot.Status?.LatestReset }
        .OfType<ResetEvent>().Concat(_snapshot.History.Events).DistinctBy(e => e.Key).OrderByDescending(e => e.AnnouncedAtUtc).ToArray();
    private bool CanNavigateAnnouncement(int delta)
    {
        var events = ReadingEvents();
        var index = events.ToList().FindIndex(e => e.Key == Announcement.CurrentEvent?.Key);
        return index >= 0 && index + delta >= 0 && index + delta < events.Count;
    }
    private void NavigateAnnouncement(int delta)
    {
        if (!CanNavigateAnnouncement(delta)) return;
        var events = ReadingEvents();
        var index = events.ToList().FindIndex(e => e.Key == Announcement.CurrentEvent?.Key);
        SelectEvent(events[index + delta]);
    }
    public void RestoreDesktop(bool compact, bool pinned) { IsCompact = compact; IsPinned = pinned; }
    private void MoveMonth(int delta) { Calendar.SetMonth(Calendar.VisibleMonth.AddMonths(delta)); RebuildCalendar(); }
    private void RebuildCalendar() => Calendar.Apply(_calendar.BuildMonth(_snapshot, Calendar.VisibleMonth, _zones.CurrentZone, _clock.UtcNow), _snapshot, _zones.CurrentZone);
    private void OnZoneChanged(object? sender, EventArgs e)
    {
        Announcement.UpdateZone(_zones.CurrentZone, _zones.FollowsSystem);
        if (Announcement.IsReadingHistory && Announcement.CurrentEvent is { } reset)
            Calendar.Select(CalendarService.LocalDate(reset.AnnouncedAtUtc, _zones.CurrentZone));
        RebuildCalendar(); Tick(); Changed(nameof(FooterTime)); Changed(nameof(FooterZone)); Changed(nameof(HistoryHealthText)); Changed(nameof(UpstreamTime));
    }
    public void Tick()
    {
        var now = _clock.UtcNow;
        var previous = Board.State.Status;
        Board.Apply(_states.Evaluate(_snapshot, now), _snapshot.StatusHealth, _zones.CurrentZone, _zones.FollowsSystem, now);
        var today = CalendarService.LocalDate(now, _zones.CurrentZone);
        if (today != _lastToday || previous != Board.State.Status) { _lastToday = today; RebuildCalendar(); }
        Changed(nameof(FooterZone));
    }
    public void RefreshSystemTimeZone() => _zones.Refresh();
    public void RefreshAfterResume() { if (_sync is not null) _ = _sync.PollAsync(); }
    public void Dispose() { _zones.ZoneChanged -= OnZoneChanged; _sync?.Dispose(); }
}
