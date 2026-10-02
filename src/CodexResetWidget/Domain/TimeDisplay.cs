using System.Globalization;

namespace CodexResetWidget.Domain;

public static class TimeDisplay
{
    public static string DateTime(DateTimeOffset instant, TimeZoneInfo zone, bool includeYear = false) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString(includeYear ? "yyyy 年 M 月 d 日 HH:mm" : "M 月 d 日 HH:mm", CultureInfo.InvariantCulture);

    public static string ZoneLabel(DateTimeOffset instant, TimeZoneInfo zone, bool followsSystem)
    {
        var offset = TimeZoneInfo.ConvertTime(instant, zone).Offset;
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        offset = offset.Duration();
        return $"{(followsSystem ? "本地时间" : "演示时区")} · UTC{sign}{(int)offset.TotalHours:00}:{offset.Minutes:00}";
    }

    public static string Countdown(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (long)Math.Ceiling(remaining.TotalSeconds));
        var time = TimeSpan.FromSeconds(seconds);
        return time.Days > 0 ? $"{time.Days} 天 {time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }

    public static string TypeLabel(ResetEvent reset) => reset.Type switch
    {
        ResetType.Regular => "全局重置", ResetType.Banked => "备用重置",
        _ => reset.RawType is { Length: > 0 } raw ? $"其他 · {raw}" : "其他记录"
    };
}
