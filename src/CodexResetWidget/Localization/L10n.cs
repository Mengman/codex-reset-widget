using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CodexResetWidget.Localization;

public enum LanguageMode { System, English, SimplifiedChinese }

public static class L10n
{
    private static readonly IReadOnlyDictionary<string, string> English = Load("en");
    private static readonly IReadOnlyDictionary<string, string> Chinese = Load("zh-CN");
    private static CultureInfo _systemCulture = CultureInfo.CurrentUICulture;
    public static LanguageMode Mode { get; private set; } = LanguageMode.System;
    public static string Locale { get; private set; } = Resolve(LanguageMode.System, _systemCulture);
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Locale == "en" ? "en-US" : Locale);
    public static event EventHandler? Changed;
    public static IEnumerable<string> Keys => English.Keys;
    public static IReadOnlyDictionary<string, string> Resources(string locale) => locale == "zh-CN" ? Chinese : English;
    public static string Resolve(LanguageMode mode, CultureInfo systemCulture) => mode switch
    {
        LanguageMode.English => "en", LanguageMode.SimplifiedChinese => "zh-CN",
        _ => systemCulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en"
    };
    public static void Apply(LanguageMode mode, CultureInfo? systemCulture = null)
    {
        _systemCulture = systemCulture ?? _systemCulture;
        var locale = Resolve(mode, _systemCulture);
        if (Mode == mode && Locale == locale) return;
        Mode = mode; Locale = locale; Changed?.Invoke(null, EventArgs.Empty);
    }
    public static string Get(string key) => Resources(Locale).TryGetValue(key, out var value) ? value
        : English.TryGetValue(key, out value) ? value : key;
    public static string Format(string key, params object?[] values) => string.Format(Culture, Get(key), values);
    // Stable keys survive cache persistence and can be rendered again after a language switch.
    public static string Pack(string key, params object?[] values) => key + "|" + JsonSerializer.Serialize(values);
    public static string Message(string? message)
    {
        if (string.IsNullOrEmpty(message)) return "";
        var split = message.IndexOf('|');
        if (split >= 0 && English.ContainsKey(message[..split]))
        {
            try
            {
                var values = JsonSerializer.Deserialize<JsonElement[]>(message[(split + 1)..]);
                return Format(message[..split], values?.Select(v => (object?)v.ToString()).ToArray() ?? []);
            }
            catch (Exception error) when (error is JsonException or FormatException) { return Get(message[..split]); }
        }
        if (English.ContainsKey(message)) return Get(message);
        // Older cache files contain rendered messages rather than resource keys.
        foreach (var entry in English.Concat(Chinese)) if (entry.Value == message) return Get(entry.Key);
        return message;
    }
    private static IReadOnlyDictionary<string, string> Load(string locale)
    {
        using var stream = typeof(L10n).Assembly.GetManifestResourceStream("CodexResetWidget.Localization.Strings." + locale + ".json")
            ?? throw new InvalidOperationException("Missing language resources: " + locale);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("Invalid language resources: " + locale);
    }
}
