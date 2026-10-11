using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace IsleBar.Core.SystemWatch;

/// <summary>
/// The part of a recurrence rule (RRULE) we handle. Rules we cannot handle are <see cref="Unsupported"/> — only the first occurrence (DTSTART) of that event is used.
/// </summary>
public sealed record IcsRule(
    string Freq,
    int Interval,
    int? Count,
    DateTimeOffset? Until,
    IReadOnlyList<DayOfWeek> ByDay,
    bool Unsupported);

/// <summary>One VEVENT from an ICS file.</summary>
public sealed class IcsEvent
{
    public string Uid { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    /// <summary>Start "wall clock" time (relative to <see cref="Zone"/>). For all-day events, midnight that day.</summary>
    public DateTime Start { get; init; }

    /// <summary>Time zone of the start time. UTC if Z, that zone if TZID, and this PC's zone if neither (floating time).</summary>
    public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Utc;

    public bool AllDay { get; init; }

    public IcsRule? Rule { get; init; }

    /// <summary>Occurrences excluded from the recurrence (UTC).</summary>
    public IReadOnlyList<DateTimeOffset> ExDates { get; init; } = [];

    /// <summary>If this VEVENT modifies one occurrence of a recurring event, that occurrence's original time (UTC).</summary>
    public DateTimeOffset? RecurrenceId { get; init; }

    /// <summary>STATUS:CANCELLED.</summary>
    public bool Cancelled { get; init; }

    /// <summary>How long it lasts (DTEND − DTSTART, or DURATION). Zero when it has neither (a moment, RFC 5545); a day for an all-day one.</summary>
    public TimeSpan Length { get; init; }

    /// <summary>
    /// Its reminders (VALARM), as how long before the start each goes off — zero = at the start (Google exports "P0D"). Only
    /// reminders at or before the start, relative to it; ACTION:NONE (a reminder turned off) is left out.
    /// </summary>
    public IReadOnlyList<TimeSpan> Alarms { get; init; } = [];

    /// <summary>Which of the calendars in the settings it came from (0 = the first) — a gap only counts within one calendar.</summary>
    public int Calendar { get; init; }

    /// <summary>Start time (UTC).</summary>
    public DateTimeOffset StartUtc => IcsParser.ToUtc(Start, Zone);
}

/// <summary>One expanded occurrence.</summary>
/// <param name="Uid">The event's UID (the same for every occurrence of a recurring event).</param>
/// <param name="Alarms">How long before <paramref name="Start"/> each reminder goes off (see <see cref="IcsEvent.Alarms"/>).</param>
public readonly record struct IcsOccurrence(
    string Summary, DateTimeOffset Start, bool AllDay, DateTimeOffset End = default, string Uid = "", int Calendar = 0,
    IReadOnlyList<TimeSpan>? Alarms = null)
{
    /// <summary>The end; the start for a moment without one (<see cref="End"/> left default).</summary>
    public DateTimeOffset Until => End > Start ? End : Start;

    /// <summary>One occurrence among all the calendars' (its event, start and calendar).</summary>
    public string Id => (Uid.Length > 0 ? Uid : Summary) + "@" + Start.UtcTicks.ToString(CultureInfo.InvariantCulture) + "#"
                        + Calendar.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// ICS (iCalendar) reading — just enough to read Google/Outlook calendars' "secret iCal address".
/// <list type="bullet">
/// <item>Times: <c>…Z</c> (UTC) · <c>TZID=</c> (IANA name <c>Asia/Seoul</c> or Windows name <c>Korea Standard Time</c>) ·
/// no time zone (this PC's) · all-day (<c>VALUE=DATE</c>).</item>
/// <item>Recurrence: INTERVAL, COUNT, UNTIL for DAILY, WEEKLY (+BYDAY), MONTHLY, YEARLY; EXDATE; modified occurrences (RECURRENCE-ID).</item>
/// <item><b>Not supported</b>: rules like BYMONTHDAY, BYSETPOS, "first Monday" (1MO) (only the first occurrence is used), WKST (weeks start on Monday),
/// definitions inside VTIMEZONE (only the TZID name is trusted), RDATE.</item>
/// </list>
/// </summary>
public static class IcsParser
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo?> ZoneCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ICS text to a VEVENT list. Broken lines and unknown properties are skipped (never throws).</summary>
    /// <param name="local">Time zone for floating times and all-day events. Defaults to this PC's.</param>
    /// <param name="calendar">Which calendar in the settings this text is (<see cref="IcsEvent.Calendar"/>).</param>
    public static IReadOnlyList<IcsEvent> Parse(string? text, TimeZoneInfo? local = null, int calendar = 0)
    {
        local ??= TimeZoneInfo.Local;
        var events = new List<IcsEvent>();
        Builder? current = null;
        var depth = 0;   // sub-blocks inside a VEVENT, such as VALARM
        Alarm? alarm = null;   // the VALARM being read (directly inside the VEVENT)

        foreach (var line in Unfold(text ?? string.Empty))
        {
            if (!TrySplit(line, out var name, out var parameters, out var value))
            {
                continue;
            }

            if (name == "BEGIN")
            {
                if (value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase) && current is null)
                {
                    current = new Builder();
                }
                else if (current is not null)
                {
                    depth++;
                    if (depth == 1 && value.Equals("VALARM", StringComparison.OrdinalIgnoreCase))
                    {
                        alarm = new Alarm();
                    }
                }

                continue;
            }

            if (name == "END")
            {
                if (current is not null && depth > 0)
                {
                    if (depth == 1 && alarm?.Before() is { } before)
                    {
                        current.Alarms.Add(before);
                    }

                    if (depth == 1)
                    {
                        alarm = null;
                    }

                    depth--;
                }
                else if (current is not null && value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    if (current.Build(calendar) is { } built)
                    {
                        events.Add(built);
                    }

                    current = null;
                }

                continue;
            }

            if (current is not null && depth == 1 && alarm is not null)
            {
                switch (name)
                {
                    case "ACTION":
                        alarm.Off = value.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "TRIGGER":
                        // only relative to the start: "-PT15M", "P0D" (an absolute date-time or RELATED=END is left out)
                        var related = parameters.TryGetValue("RELATED", out var rel) && rel.Equals("END", StringComparison.OrdinalIgnoreCase);
                        var absolute = parameters.TryGetValue("VALUE", out var kind) && kind.Equals("DATE-TIME", StringComparison.OrdinalIgnoreCase);
                        alarm.Trigger = related || absolute ? null : ParseDuration(value);
                        break;
                }

                continue;
            }

            if (current is null || depth > 0)
            {
                continue;
            }

            switch (name)
            {
                case "UID":
                    current.Uid = value;
                    break;
                case "SUMMARY":
                    current.Summary = Unescape(value);
                    break;
                case "STATUS":
                    current.Cancelled = value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);
                    break;
                case "DTSTART":
                    if (ParseTime(value, parameters, local) is { } start)
                    {
                        current.Start = start;
                    }

                    break;
                case "DTEND":
                    current.End = ParseTime(value, parameters, local);
                    break;
                case "DURATION":
                    current.Duration = ParseDuration(value);
                    break;
                case "RRULE":
                    current.RuleText = value;
                    break;
                case "EXDATE":
                    foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (ParseTime(part, parameters, local) is { } ex)
                        {
                            current.ExDates.Add(ToUtc(ex.Wall, ex.Zone));
                        }
                    }

                    break;
                case "RECURRENCE-ID":
                    if (ParseTime(value, parameters, local) is { } rid)
                    {
                        current.RecurrenceId = ToUtc(rid.Wall, rid.Zone);
                    }

                    break;
            }
        }

        return events;
    }

    /// <summary>
    /// Time zone name → time zone. Accepts both IANA (<c>Asia/Seoul</c>) and Windows (<c>Korea Standard Time</c>) names.
    /// Null for unknown names (the caller then uses this PC's time zone).
    /// </summary>
    public static TimeZoneInfo? ResolveZone(string? tzid)
    {
        if (string.IsNullOrWhiteSpace(tzid))
        {
            return null;
        }

        return ZoneCache.GetOrAdd(tzid.Trim().Trim('"'), static id =>
        {
            foreach (var candidate in Candidates(id))
            {
                if (Find(candidate) is { } zone)
                {
                    return zone;
                }

                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(candidate, out var windowsId) && Find(windowsId) is { } byWindows)
                {
                    return byWindows;
                }

                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(candidate, out var ianaId) && Find(ianaId) is { } byIana)
                {
                    return byIana;
                }
            }

            return null;
        });

        // for prefixes like Thunderbird's "/mozilla.org/20050126_1/America/New_York", only the last two segments are used
        static IEnumerable<string> Candidates(string id)
        {
            yield return id;
            var parts = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2)
            {
                yield return string.Join('/', parts[^2..]);
            }
        }

        static TimeZoneInfo? Find(string id)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Wall clock time → UTC. Times that do not exist due to DST (the hour skipped in spring) are pushed one hour later
    /// — throwing would make one event break reading the whole calendar.
    /// </summary>
    public static DateTimeOffset ToUtc(DateTime wall, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), TimeSpan.Zero);
    }

    private static (DateTime Wall, TimeZoneInfo Zone, bool AllDay)? ParseTime(
        string value, IReadOnlyDictionary<string, string> parameters, TimeZoneInfo local)
    {
        value = value.Trim();
        var dateOnly = value.Length == 8
                       || (parameters.TryGetValue("VALUE", out var kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase));
        if (dateOnly)
        {
            return DateTime.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)
                ? (date, local, true)
                : null;
        }

        var utc = value.EndsWith('Z');
        var core = utc ? value[..^1] : value;
        if (!DateTime.TryParseExact(core, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
        {
            return null;
        }

        if (utc)
        {
            return (wall, TimeZoneInfo.Utc, false);
        }

        var zone = parameters.TryGetValue("TZID", out var tzid) ? ResolveZone(tzid) ?? local : local;
        return (wall, zone, false);
    }

    /// <summary>
    /// An RFC 5545 duration — "PT15M", "-P1DT2H", "P1W", "+PT0S" — or null if it isn't one. Negative = before.
    /// </summary>
    public static TimeSpan? ParseDuration(string? text)
    {
        var value = (text ?? string.Empty).Trim().ToUpperInvariant();
        var sign = 1;
        if (value.StartsWith('-') || value.StartsWith('+'))
        {
            sign = value[0] == '-' ? -1 : 1;
            value = value[1..];
        }

        if (!value.StartsWith('P') || value.Length < 3)
        {
            return null;
        }

        var total = TimeSpan.Zero;
        var number = 0L;
        var digits = 0;
        var inTime = false;
        foreach (var c in value[1..])
        {
            if (c is >= '0' and <= '9')
            {
                number = Math.Min(number * 10 + (c - '0'), 100_000);
                digits++;
                continue;
            }

            if (c == 'T' && digits == 0 && !inTime)
            {
                inTime = true;
                continue;
            }

            TimeSpan? part = (c, inTime) switch
            {
                ('W', false) => TimeSpan.FromDays(7 * number),
                ('D', false) => TimeSpan.FromDays(number),
                ('H', true) => TimeSpan.FromHours(number),
                ('M', true) => TimeSpan.FromMinutes(number),
                ('S', true) => TimeSpan.FromSeconds(number),
                _ => null,
            };
            if (digits == 0 || part is null)
            {
                return null;   // a letter out of place
            }

            total += part.Value;
            number = 0;
            digits = 0;
        }

        return digits == 0 ? sign * total : null;
    }

    /// <summary>RFC 5545 line unfolding: a line starting with a space or tab is appended to the previous line.</summary>
    private static IEnumerable<string> Unfold(string text)
    {
        var sb = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.Length > 0 && raw[0] is ' ' or '\t')
            {
                sb.Append(raw, 1, raw.Length - 1);
                continue;
            }

            if (sb.Length > 0)
            {
                yield return sb.ToString();
            }

            sb.Clear().Append(raw.TrimEnd('\r'));
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    /// <summary>"DTSTART;TZID=Asia/Seoul:20260930T140000" → name, parameters, value. ':' and ';' inside quotes do not split.</summary>
    private static bool TrySplit(string line, out string name, out Dictionary<string, string> parameters, out string value)
    {
        name = string.Empty;
        value = string.Empty;
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var quoted = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                quoted = !quoted;
            }
            else if (line[i] == ':' && !quoted)
            {
                colon = i;
                break;
            }
        }

        if (colon <= 0)
        {
            return false;
        }

        value = line[(colon + 1)..];
        var head = SplitOutsideQuotes(line[..colon], ';');
        name = head[0].Trim().ToUpperInvariant();
        foreach (var p in head.Skip(1))
        {
            var eq = p.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                parameters[p[..eq].Trim()] = p[(eq + 1)..].Trim().Trim('"');
            }
        }

        return name.Length > 0;
    }

    private static List<string> SplitOutsideQuotes(string text, char separator)
    {
        var parts = new List<string>();
        var quoted = false;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                quoted = !quoted;
            }
            else if (text[i] == separator && !quoted)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>
    /// The calendar's own name (<c>X-WR-CALNAME</c>, which Google and Outlook write: "课程表"), for the lifted card's source row
    /// (10-11); null when it has none.
    /// </summary>
    public static string? CalendarName(string? text)
    {
        foreach (var line in Unfold(text ?? string.Empty))
        {
            if (line.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                break;   // it's in the calendar's head, before the events
            }

            if (line.StartsWith("X-WR-CALNAME", StringComparison.OrdinalIgnoreCase) && line.IndexOf(':', StringComparison.Ordinal) is var at and > 0
                && Unescape(line[(at + 1)..]).Trim() is { Length: > 0 } name)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>Unescapes TEXT values (\n \, \; \\); for multi-line values the island shortens it based on the first line.</summary>
    private static string Unescape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next is 'n' or 'N' ? ' ' : next);
            }
            else
            {
                sb.Append(value[i]);
            }
        }

        return sb.ToString().Trim();
    }

    internal static IcsRule ParseRule(string text, TimeZoneInfo local)
    {
        string freq = string.Empty;
        var interval = 1;
        int? count = null;
        DateTimeOffset? until = null;
        var byDay = new List<DayOfWeek>();
        var unsupported = false;

        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = part[..eq].ToUpperInvariant();
            var val = part[(eq + 1)..];
            switch (key)
            {
                case "FREQ":
                    freq = val.ToUpperInvariant();
                    break;
                case "INTERVAL":
                    interval = int.TryParse(val, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;
                    break;
                case "COUNT":
                    count = int.TryParse(val, NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : null;
                    break;
                case "UNTIL":
                    if (ParseTime(val, new Dictionary<string, string>(), local) is { } u)
                    {
                        // a date alone includes the whole of that day
                        until = ToUtc(u.AllDay ? u.Wall.AddDays(1).AddTicks(-1) : u.Wall, u.Zone);
                    }

                    break;
                case "BYDAY":
                    foreach (var day in val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (ParseDay(day) is { } d)
                        {
                            byDay.Add(d);
                        }
                        else
                        {
                            unsupported = true;   // ordinal weekdays like "1MO" or "-1FR"
                        }
                    }

                    break;
                case "WKST":
                    break;
                default:
                    if (key.StartsWith("BY", StringComparison.Ordinal))
                    {
                        unsupported = true;
                    }

                    break;
            }
        }

        if (freq is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY"))
        {
            unsupported = true;
        }

        if (byDay.Count > 0 && freq is "MONTHLY" or "YEARLY")
        {
            unsupported = true;
        }

        return new IcsRule(freq, interval, count, until, byDay, unsupported);
    }

    private static DayOfWeek? ParseDay(string token) => token.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday,
        _ => null,
    };

    /// <summary>A VALARM being read.</summary>
    private sealed class Alarm
    {
        public TimeSpan? Trigger { get; set; }

        public bool Off { get; set; }

        /// <summary>How long before the start it goes off; null when it doesn't count (off, after the start, not relative to the start).</summary>
        public TimeSpan? Before() => !Off && Trigger is { } t && t <= TimeSpan.Zero ? -t : null;
    }

    private sealed class Builder
    {
        public string Uid { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public (DateTime Wall, TimeZoneInfo Zone, bool AllDay)? Start { get; set; }

        public string? RuleText { get; set; }

        public List<DateTimeOffset> ExDates { get; } = [];

        public DateTimeOffset? RecurrenceId { get; set; }

        public bool Cancelled { get; set; }

        public (DateTime Wall, TimeZoneInfo Zone, bool AllDay)? End { get; set; }

        public TimeSpan? Duration { get; set; }

        public List<TimeSpan> Alarms { get; } = [];

        public IcsEvent? Build(int calendar)
        {
            if (Start is not { } start)
            {
                return null;   // an event without a start time cannot be announced
            }

            var length = End is { } end ? ToUtc(end.Wall, end.Zone) - ToUtc(start.Wall, start.Zone)
                : Duration ?? (start.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero);
            return new IcsEvent
            {
                Length = length > TimeSpan.Zero ? length : TimeSpan.Zero,
                Alarms = [.. Alarms.Distinct().Order()],
                Calendar = calendar,
                Uid = Uid,
                Summary = Summary,
                Start = start.Wall,
                Zone = start.Zone,
                AllDay = start.AllDay,
                Rule = RuleText is null || RecurrenceId is not null ? null : ParseRule(RuleText, start.Zone),
                ExDates = ExDates,
                RecurrenceId = RecurrenceId,
                Cancelled = Cancelled,
            };
        }
    }
}

/// <summary>Expands recurring events into the occurrences within a range.</summary>
public static class IcsSchedule
{
    /// <summary>Maximum occurrences to expand per event (so a broken rule cannot loop forever).</summary>
    private const int MaxSteps = 5000;

    /// <summary>
    /// Occurrences starting within [<paramref name="from"/>, <paramref name="to"/>] (in start order). Cancelled events, EXDATEs and
    /// occurrences modified by another VEVENT (RECURRENCE-ID) are excluded — a modified occurrence comes in separately as that VEVENT's single occurrence.
    /// </summary>
    public static IReadOnlyList<IcsOccurrence> Occurrences(IEnumerable<IcsEvent> events, DateTimeOffset from, DateTimeOffset to)
    {
        var list = events.ToList();
        var overridden = list
            .Where(e => e.RecurrenceId is not null && !string.IsNullOrEmpty(e.Uid))
            .Select(e => (e.Uid, e.RecurrenceId!.Value.UtcTicks))
            .ToHashSet();

        var result = new List<IcsOccurrence>();
        foreach (var e in list)
        {
            foreach (var start in Starts(e, from, to))
            {
                if (start < from || start > to)
                {
                    continue;
                }

                if (e.Cancelled
                    || e.ExDates.Any(x => x.UtcTicks == start.UtcTicks)
                    || (e.RecurrenceId is null && overridden.Contains((e.Uid, start.UtcTicks))))
                {
                    continue;
                }

                result.Add(new IcsOccurrence(e.Summary, start, e.AllDay, start + e.Length, e.Uid, e.Calendar, e.Alarms));
            }
        }

        return [.. result.OrderBy(o => o.Start)];
    }

    private static IEnumerable<DateTimeOffset> Starts(IcsEvent e, DateTimeOffset from, DateTimeOffset to)
    {
        if (e.Rule is not { Unsupported: false } rule)
        {
            yield return e.StartUtc;
            yield break;
        }

        var produced = 0;
        var first = FirstStep(e, rule, from);
        for (var step = first; step < first + MaxSteps; step++)
        {
            foreach (var wall in Candidates(e, rule, step))
            {
                if (wall < e.Start)
                {
                    continue;
                }

                var utc = IcsParser.ToUtc(wall, e.Zone);
                if ((rule.Until is { } until && utc > until) || (rule.Count is { } count && produced >= count))
                {
                    yield break;
                }

                produced++;
                yield return utc;
            }

            if (IcsParser.ToUtc(PeriodStart(e, rule, step), e.Zone) > to)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// So a daily event started years ago is not counted from the beginning, skip to the period just before the range when there is no COUNT
    /// (with COUNT, counting must start from the beginning to know which occurrence it is).
    /// </summary>
    private static int FirstStep(IcsEvent e, IcsRule rule, DateTimeOffset from)
    {
        if (rule.Count is not null)
        {
            return 0;
        }

        var span = from.UtcDateTime - e.StartUtc.UtcDateTime - TimeSpan.FromDays(2);
        if (span <= TimeSpan.Zero)
        {
            return 0;
        }

        var steps = rule.Freq switch
        {
            "DAILY" => span.TotalDays / rule.Interval,
            "WEEKLY" => span.TotalDays / (7.0 * rule.Interval),
            "MONTHLY" => span.TotalDays / (31.0 * rule.Interval),
            _ => span.TotalDays / (366.0 * rule.Interval),
        };
        return Math.Max(0, (int)steps - 2);
    }

    private static DateTime PeriodStart(IcsEvent e, IcsRule rule, int step) => rule.Freq switch
    {
        "DAILY" => e.Start.AddDays((double)step * rule.Interval),
        "WEEKLY" => WeekStart(e.Start).AddDays(7.0 * step * rule.Interval),
        "MONTHLY" => e.Start.AddMonths(step * rule.Interval),
        _ => e.Start.AddYears(step * rule.Interval),
    };

    private static IEnumerable<DateTime> Candidates(IcsEvent e, IcsRule rule, int step)
    {
        switch (rule.Freq)
        {
            case "DAILY":
                var day = e.Start.AddDays((double)step * rule.Interval);
                if (rule.ByDay.Count == 0 || rule.ByDay.Contains(day.DayOfWeek))
                {
                    yield return day;
                }

                break;
            case "WEEKLY":
                var week = WeekStart(e.Start).AddDays(7.0 * step * rule.Interval);
                IReadOnlyList<DayOfWeek> days = rule.ByDay.Count > 0 ? rule.ByDay : new[] { e.Start.DayOfWeek };
                foreach (var offset in days.Select(MondayOffset).Distinct().Order())
                {
                    yield return week.AddDays(offset);
                }

                break;
            case "MONTHLY":
                var month = e.Start.AddMonths(step * rule.Interval);
                if (month.Day == e.Start.Day)
                {
                    yield return month;   // events on the 31st skip months that end on the 30th (RFC 5545)
                }

                break;
            default:
                var year = e.Start.AddYears(step * rule.Interval);
                if (year.Day == e.Start.Day)
                {
                    yield return year;
                }

                break;
        }
    }

    /// <summary>Monday of that week, at the same time of day as the first occurrence.</summary>
    private static DateTime WeekStart(DateTime start) => start.AddDays(-MondayOffset(start.DayOfWeek));

    private static int MondayOffset(DayOfWeek day) => ((int)day + 6) % 7;
}
