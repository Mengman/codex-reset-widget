using CodexResetWidget.Domain;

namespace CodexResetWidget.Presentation.ViewModels;

public sealed class AnnouncementViewModel : ObservableObject
{
    private ResetEvent? _event;
    private TimeZoneInfo _zone = TimeZoneInfo.Local;
    private bool _history;
    private bool _followsSystem;
    public bool IsDemo { get; set; } = true;
    public string SourceBadge => IsDemo ? L10n.Get("Announcement.Sample") : L10n.Get("Announcement.Public");
    public ResetEvent? CurrentEvent => _event;
    public EventKey? SelectedEvent => _history ? _event?.Key : null;
    public bool IsReadingHistory => _history;
    public bool HasEvent => _event is not null;
    public bool HasLink => _event?.SourceUrl is { Scheme: "https" or "http" };
    public string Heading => L10n.Get("Announcement.Title");
    private bool IsTibo => _event?.Author == "Tibo" || _event?.Handle == "@thsottiaux"
        || _event?.SourceUrl is { } url && (url.Host is "x.com" or "www.x.com" or "twitter.com" or "www.twitter.com")
            && url.AbsolutePath.StartsWith("/thsottiaux/status/", StringComparison.OrdinalIgnoreCase);
    public string Author => IsTibo ? "Tibo" : _event?.Author ?? L10n.Get("Announcement.Record");
    public string Handle => IsTibo ? "@thsottiaux" : _event?.Handle ?? L10n.Get("Announcement.UnknownAuthor");
    public string Avatar => IsTibo ? "T" : _event?.Author is { Length: > 0 } author ? author[..1] : "·";
    public string Text => _event?.Text ?? (IsDemo ? L10n.Get("Announcement.NoDemoPost") : L10n.Get("Announcement.NoPost"));
    public string Published => _event is null ? "" : TimeDisplay.DateTime(_event.AnnouncedAtUtc, _zone);
    public string ZoneLabel => _event is null ? "" : TimeDisplay.ZoneLabel(_event.AnnouncedAtUtc, _zone, _followsSystem);
    public string SourceNote => _event?.Status == EventStatus.Recorded ? L10n.Get("Announcement.SampleRecord") : L10n.Get("Announcement.SampleNote");
    public void Show(ResetEvent? reset, bool history, TimeZoneInfo zone, bool followsSystem)
    {
        _event = reset; _history = history; _zone = zone; _followsSystem = followsSystem; AllChanged();
    }
    public void UpdateZone(TimeZoneInfo zone, bool followsSystem) { _zone = zone; _followsSystem = followsSystem; AllChanged(); }
}
