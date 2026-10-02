using System.Globalization;

namespace CodexResetWidget.Domain;

public static class TimeDisplay
{
    public static string DateTime(DateTimeOffset instant, TimeZoneInfo zone, bool includeYear = false) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString(L10n.Get(includeYear ? "Date.YearPattern" : "Date.Pattern"), L10n.Culture);
    public static string Date(DateOnly date) => date.ToString(L10n.Get("Date.DayPattern"), L10n.Culture);
    public static string Month(DateOnly date) => date.ToString(L10n.Get("Date.MonthPattern"), L10n.Culture);
    public static string ZoneLabel(DateTimeOffset instant, TimeZoneInfo zone, bool followsSystem)
    {
        var offset = TimeZoneInfo.ConvertTime(instant, zone).Offset;
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        offset = offset.Duration();
        return $"{L10n.Get(followsSystem ? "Time.Local" : "Time.DemoZone")} · UTC{sign}{(int)offset.TotalHours:00}:{offset.Minutes:00}";
    }
    public static string Countdown(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (long)Math.Ceiling(remaining.TotalSeconds));
        var time = TimeSpan.FromSeconds(seconds);
        return time.Days > 0 ? L10n.Format("Time.Days", time.Days, time.Hours, time.Minutes, time.Seconds)
            : $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }
    public static string TypeLabel(ResetEvent reset) => L10n.Get(reset.Type switch
    { ResetType.Regular => "Type.Regular", ResetType.Banked => "Type.Banked", _ => "Type.Unknown" });
}
