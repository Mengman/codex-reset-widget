using System.Collections.ObjectModel;
using CodexResetWidget.Domain;

namespace CodexResetWidget.Presentation.ViewModels;

public sealed class CalendarDayViewModel(CalendarDay day, bool selected)
{
    public CalendarDay Day { get; } = day;
    public string Number => Day.Date.Day.ToString();
    public bool IsCurrentMonth => Day.IsCurrentMonth;
    public bool IsToday => Day.IsToday;
    public bool IsSelected { get; } = selected;
    public bool HasRegular => Day.Events.Any(e => e.Type == ResetType.Regular);
    public bool HasBanked => Day.Events.Any(e => e.Type == ResetType.Banked);
    public bool HasUnknown => Day.Events.Any(e => e.Type == ResetType.Unknown);
    public bool HasForecast => Day.Forecast is not null;
    public bool HasMultiple => Day.Events.Count > 1;
    public string Count => $"{Day.Events.Count}";
    public string AccessibleName => $"{Day.Date:yyyy-MM-dd}，{Day.Events.Count} 条公告记录{(HasForecast ? "，有未来预告" : "")}";
    public string Tooltip => $"{Day.Date:yyyy-MM-dd} · {Day.Events.Count} 条公告记录{(HasForecast ? " · 公告预计时间" : "")}";
}

public sealed class EventRowViewModel(ResetEvent reset, string? date = null)
{
    public ResetEvent Event { get; } = reset;
    public string Label => date is null ? TimeDisplay.TypeLabel(Event) : $"{date} · {TimeDisplay.TypeLabel(Event)}";
    public string Marker => Event.Type switch { ResetType.Regular => "●", ResetType.Banked => "◆", _ => "○" };
    public string Detail => Event.Type == ResetType.Banked ? "手动使用 · 查看公告" : "查看公告";
}

public sealed class CalendarViewModel : ObservableObject
{
    private DateOnly _month;
    private DateOnly _selectedDate;
    private bool _complete;
    public DateOnly VisibleMonth => _month;
    public DateOnly SelectedDate => _selectedDate;
    public string MonthLabel => $"{_month.Year} 年 {_month.Month} 月";
    public string SelectedLabel => $"{_selectedDate.Month} 月 {_selectedDate.Day} 日 · 公告记录";
    public string EmptyText => _complete ? "当前数据源未收录当天记录" : "历史记录尚未加载完整";
    public bool HasEvents => Events.Count > 0;
    public ObservableCollection<CalendarDayViewModel> Days { get; } = [];
    public ObservableCollection<EventRowViewModel> Events { get; } = [];
    public void Initialize(DateOnly today) { _month = new(today.Year, today.Month, 1); _selectedDate = today; }
    public void SetMonth(DateOnly month) { _month = new(month.Year, month.Month, 1); }
    public void Select(DateOnly date) { _selectedDate = date; }
    public void Apply(CalendarMonth month, WidgetSnapshot snapshot, TimeZoneInfo zone)
    {
        _month = month.Month; _complete = snapshot.History.IsComplete;
        Days.Clear();
        foreach (var day in month.Days) Days.Add(new(day, day.Date == _selectedDate));
        Events.Clear();
        foreach (var reset in snapshot.History.Events.Where(e => e.Status != EventStatus.Scheduled && CalendarService.LocalDate(e.AnnouncedAtUtc, zone) == _selectedDate).OrderByDescending(e => e.AnnouncedAtUtc))
            Events.Add(new(reset, $"{_selectedDate.Month} 月 {_selectedDate.Day} 日"));
        AllChanged();
    }
}
