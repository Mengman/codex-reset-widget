using CodexResetWidget.Domain;

namespace CodexResetWidget.Presentation.ViewModels;

public sealed class AnnouncementViewModel : ObservableObject
{
    private ResetEvent? _event;
    private TimeZoneInfo _zone = TimeZoneInfo.Local;
    private bool _history;
    private bool _followsSystem;
    public bool IsDemo { get; set; } = true;
    public string SourceBadge => IsDemo ? "示例公告" : "公开公告";
    public ResetEvent? CurrentEvent => _event;
    public EventKey? SelectedEvent => _history ? _event?.Key : null;
    public bool IsReadingHistory => _history;
    public bool HasEvent => _event is not null;
    public bool HasLink => _event?.SourceUrl is { Scheme: "https" or "http" };
    public string Heading => "重置公告";
    private bool IsTibo => _event?.Author == "Tibo" || _event?.Handle == "@thsottiaux"
        || _event?.SourceUrl is { } url && (url.Host is "x.com" or "www.x.com" or "twitter.com" or "www.twitter.com")
            && url.AbsolutePath.StartsWith("/thsottiaux/status/", StringComparison.OrdinalIgnoreCase);
    public string Author => IsTibo ? "Tibo" : _event?.Author ?? "公告记录";
    public string Handle => IsTibo ? "@thsottiaux" : _event?.Handle ?? "来源未提供作者";
    public string Avatar => IsTibo ? "T" : _event?.Author is { Length: > 0 } author ? author[..1] : "·";
    public string Text => _event?.Text ?? (IsDemo ? "暂无公告。可使用演示场景检查不同状态。" : "暂无可用公告，获取成功后会在这里显示原文。");
    public string Published => _event is null ? "" : TimeDisplay.DateTime(_event.AnnouncedAtUtc, _zone);
    public string ZoneLabel => _event is null ? "" : TimeDisplay.ZoneLabel(_event.AnnouncedAtUtc, _zone, _followsSystem);
    public string SourceNote => _event?.Status == EventStatus.Recorded ? "模拟数据源记录 · 不代表个人额度恢复" : "模拟公告 · 用于界面检查";
    public void Show(ResetEvent? reset, bool history, TimeZoneInfo zone, bool followsSystem)
    {
        _event = reset; _history = history; _zone = zone; _followsSystem = followsSystem; AllChanged();
    }
    public void UpdateZone(TimeZoneInfo zone, bool followsSystem) { _zone = zone; _followsSystem = followsSystem; AllChanged(); }
}
