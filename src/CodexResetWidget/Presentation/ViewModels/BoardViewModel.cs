using CodexResetWidget.Domain;

namespace CodexResetWidget.Presentation.ViewModels;

public sealed class BoardViewModel : ObservableObject
{
    private BoardState _state = new(BoardStatus.NoAnnouncement, null, TimeSpan.Zero);
    private DataHealth _health = new();
    private TimeZoneInfo _zone = TimeZoneInfo.Local;
    private bool _followsSystem = true;
    private DateTimeOffset _now;
    public BoardState State => _state;
    public bool HasCountdown => _state.Status == BoardStatus.Countdown;
    public bool ShowsDate => _state.Status == BoardStatus.RecentAnnouncement;
    public bool HasNotice => Notice.Length > 0;
    public string Title => _state.Status switch
    {
        BoardStatus.Countdown => "下一次公告重置",
        BoardStatus.RecentAnnouncement => "最近公告的预计重置时间", _ => "重置公告板"
    };
    public string Badge => _health.IsLoading && _state.RelatedEvent is null ? "加载中" : _state.Status switch
    {
        BoardStatus.Countdown => "已预告", BoardStatus.RecentAnnouncement => "预计时间已到",
        BoardStatus.TimeUnknown => "时间未定", BoardStatus.Watch => "观察信号", _ => "尚未公布"
    };
    public string Countdown => TimeDisplay.Countdown(_state.Remaining);
    public string MainDate => _state.RelatedEvent?.ScheduledForUtc is { } instant ? TimeDisplay.DateTime(instant, _zone) : "";
    public string EmptyText => _health.IsLoading ? "正在加载公告" : _health.LastError is not null && _state.RelatedEvent is null ? "暂时无法获取公告" : _state.Status switch
    {
        BoardStatus.TimeUnknown => "重置已预告\n具体时间待公布",
        BoardStatus.Watch => "有重置信号\n时间未定", _ => "下一次重置\n尚未公布"
    };
    public string ZoneLabel => TimeDisplay.ZoneLabel(_state.RelatedEvent?.ScheduledForUtc ?? _now, _zone, _followsSystem);
    public string Summary => $"{ZoneLabel} · {Supporting}";
    public string ZoneName => _zone.DisplayName;
    public string Supporting => _state.RelatedEvent is { } reset ? TimeDisplay.TypeLabel(reset) : "以公开公告为依据";
    public string Notice
    {
        get
        {
            var notices = new List<string>();
            if (ShowsDate) notices.Add("实际额度请以 Codex 中显示为准。");
            if (_state.AnnouncementUnconfirmed) notices.Add("该预告已不在当前数据源中，状态待确认。");
            if (_state.TargetChanged) notices.Add("公告时间已更新。");
            if (_health.IsFromCache) notices.Add($"缓存：{(_health.LastSuccessAtUtc is { } at ? TimeDisplay.DateTime(at, _zone) : "时间未知")}");
            if (_health.IsStale) notices.Add("数据可能已过期。");
            if (_health.LastError is not null) notices.Add(_health.LastError);
            return string.Join("\n", notices);
        }
    }
    public void Apply(BoardState state, DataHealth health, TimeZoneInfo zone, bool followsSystem, DateTimeOffset now)
    {
        _state = state; _health = health; _zone = zone; _followsSystem = followsSystem; _now = now;
        AllChanged();
    }
}

