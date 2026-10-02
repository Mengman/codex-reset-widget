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
        BoardStatus.Countdown => L10n.Get("Board.Title"),
        BoardStatus.RecentAnnouncement => L10n.Get("Board.RecentTitle"), _ => L10n.Get("Board.Title")
    };
    public string Badge => _health.IsLoading && _state.RelatedEvent is null ? L10n.Get("Board.Loading") : _state.Status switch
    {
        BoardStatus.Countdown => L10n.Get("Board.Announced"), BoardStatus.RecentAnnouncement => L10n.Get("Board.TimeReached"),
        BoardStatus.TimeUnknown => L10n.Get("Board.TimeUnknown"), BoardStatus.Watch => L10n.Get("Board.Watch"), _ => L10n.Get("Board.NoAnnouncement")
    };
    public string Countdown => TimeDisplay.Countdown(_state.Remaining);
    public string MainDate => _state.RelatedEvent?.ScheduledForUtc is { } instant ? TimeDisplay.DateTime(instant, _zone) : "";
    public string EmptyText => _health.IsLoading ? L10n.Get("Board.LoadingText") : _health.LastError is not null && _state.RelatedEvent is null ? L10n.Get("Board.FetchFailed") : _state.Status switch
    {
        BoardStatus.TimeUnknown => L10n.Get("Board.TimeUnknownText"),
        BoardStatus.Watch => L10n.Get("Board.WatchText"), _ => L10n.Get("Board.NoAnnouncementText")
    };
    public string ZoneLabel => TimeDisplay.ZoneLabel(_state.RelatedEvent?.ScheduledForUtc ?? _now, _zone, _followsSystem);
    public string Summary => Supporting;
    public string ZoneName => $"{ZoneLabel} · {_zone.Id}";
    public string Supporting => _state.RelatedEvent is { } reset ? TimeDisplay.TypeLabel(reset) : L10n.Get("Board.BasedOnPublic");
    public string Notice
    {
        get
        {
            var notices = new List<string>();
            if (_state.AnnouncementUnconfirmed) notices.Add(L10n.Get("Notice.Withdrawn"));
            if (_state.TargetChanged) notices.Add(L10n.Get("Notice.Retimed"));
            if (_health.IsFromCache) notices.Add(L10n.Format("Notice.Cache", _health.LastSuccessAtUtc is { } at ? TimeDisplay.DateTime(at, _zone) : L10n.Get("Common.UnknownTime")));
            if (_health.IsStale) notices.Add(L10n.Get("Notice.Stale"));
            if (_health.LastError is not null) notices.Add(L10n.Message(_health.LastError));
            return string.Join("\n", notices);
        }
    }
    public void Apply(BoardState state, DataHealth health, TimeZoneInfo zone, bool followsSystem, DateTimeOffset now)
    {
        _state = state; _health = health; _zone = zone; _followsSystem = followsSystem; _now = now;
        AllChanged();
    }
}
