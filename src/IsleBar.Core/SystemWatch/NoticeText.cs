using System.Globalization;
using IsleBar.Core.Localization;

namespace IsleBar.Core.SystemWatch;

/// <summary>
/// Builds notice text. Fills the translation placeholders (<c>{}</c> name, <c>{p}</c> percent, <c>{m}</c> minutes, <c>{h}</c> hours,
/// <c>{n}</c> count). Leading and trailing parts are always joined with " · " (like the island's other text).
/// </summary>
public static class NoticeText
{
    /// <summary>Joiner inside titles. "Charging · 78%".</summary>
    public const string Separator = " · ";

    /// <summary>Dots shown instead of copied content that looks like a password.</summary>
    public const string Masked = "••••••";

    public static string Join(string head, string? tail)
        => string.IsNullOrWhiteSpace(tail) ? head : head + Separator + tail;

    public static string Charging(LanguageStrings s, int percent) => Percent(s.NoticeCharging, percent);

    public static string OnBattery(LanguageStrings s, int percent) => Percent(s.NoticeOnBattery, percent);

    public static string BatteryLevel(LanguageStrings s, int percent) => Percent(s.NoticeBatteryLevel, percent);

    /// <summary>"Battery 20% · about 45 min". Just the first part if the time remaining is unknown.</summary>
    public static string BatteryLow(LanguageStrings s, int percent, TimeSpan? remaining)
        => Join(BatteryLevel(s, percent), Remaining(s, remaining));

    /// <summary>
    /// "about 45 min" · "about 1 h 20 min". Null if unknown or nonsensical (under 1 minute, over a day) —
    /// Windows gives a very large value when it does not know yet, e.g. right after unplugging.
    /// </summary>
    public static string? Remaining(LanguageStrings s, TimeSpan? remaining)
    {
        if (remaining is not { } t || t < TimeSpan.FromMinutes(1) || t > TimeSpan.FromHours(24))
        {
            return null;
        }

        var minutes = (int)t.TotalMinutes;
        return minutes < 60
            ? Fill(s.NoticeAboutMin, "{m}", minutes)
            : Fill(Fill(s.NoticeAboutHourMin, "{h}", minutes / 60), "{m}", minutes % 60);
    }

    public static string Connected(LanguageStrings s, string name) => Name(s.NoticeConnected, name);

    public static string Disconnected(LanguageStrings s, string name) => Name(s.NoticeDisconnected, name);

    public static string Copied(LanguageStrings s, string preview)
        => preview.Length == 0 ? Name(s.NoticeCopied, string.Empty).TrimEnd(' ', '·', ':', '：') : Name(s.NoticeCopied, preview);

    /// <summary>"File copied" (1) · "3 files copied".</summary>
    public static string CopiedFiles(LanguageStrings s, int count)
        => count <= 1 ? s.NoticeCopiedFile : Fill(s.NoticeCopiedFiles, "{n}", count);

    /// <summary>"CPU 96% · chrome". Just the first part if the program name is unknown.</summary>
    public static string Cpu(LanguageStrings s, int percent, string? topProcess)
        => Join(Percent(s.NoticeCpu, percent), topProcess);

    public static string Memory(LanguageStrings s, int percent) => Percent(s.NoticeMemory, percent);

    /// <summary>
    /// When a calendar event starts, for a reminder (user 10-10): within the hour "In 10 min" (it counts down); later the time —
    /// "14:00" today, "Tomorrow 8:00", else the weekday "Wed 8:00" ("in 1 h" would be wrong a few minutes later).
    /// The event's name follows apart (grey), not in this text.
    /// </summary>
    public static string EventWhen(LanguageStrings s, DateTimeOffset start, DateTimeOffset now, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (start - now <= TimeSpan.FromHours(1))
        {
            return EventSoon(s, CalendarLogic.MinutesLeft(start, now));
        }

        var local = start.ToLocalTime();
        var clock = local.ToString("t", culture);
        var days = (local.Date - now.ToLocalTime().Date).Days;
        return days switch
        {
            <= 0 => clock,
            1 => s.NoticeEventTomorrow.Replace("{t}", clock, StringComparison.Ordinal),
            _ => local.ToString("ddd", culture) + " " + clock,
        };
    }

    /// <summary>"In 10 min" (a reminder within the hour, a gap).</summary>
    public static string EventSoon(LanguageStrings s, int minutes) => Fill(s.NoticeEventSoon, "{m}", Math.Max(1, minutes));

    private static string Percent(string template, int percent) => Fill(template, "{p}", Math.Clamp(percent, 0, 100));

    private static string Name(string template, string name)
        => template.Replace("{}", name ?? string.Empty, StringComparison.Ordinal);

    private static string Fill(string template, string key, int value)
        => template.Replace(key, value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
