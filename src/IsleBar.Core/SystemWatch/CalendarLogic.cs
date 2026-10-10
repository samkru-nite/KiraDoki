namespace IsleBar.Core.SystemWatch;

/// <summary>What the one calendar card shows (TODO ③ calendar, user 10-10).</summary>
public enum CalendarLookKind
{
    /// <summary>🟠 pinned: one of the event's own reminders went off — "Maths · 8:00", counting down to the start. Can be closed.</summary>
    Remind,

    /// <summary>🟠 pinned: an event with a reminder "at the start" started — stays on the big pill until closed by hand.</summary>
    Held,

    /// <summary>🟠 debut: an event started — on the big pill <see cref="CalendarLogic.DebutLength"/>, then <see cref="Now"/>.</summary>
    Start,

    /// <summary>⚪ status card in its cell: "Maths · until 9:35" (no ring: user 10-10).</summary>
    Now,

    /// <summary>🟢 debut: the event ended — "Maths · ended" for <see cref="CalendarLogic.DebutLength"/>.</summary>
    End,

    /// <summary>🟢 pinned: the next event of the same calendar starts within <see cref="CalendarLogic.GapLimit"/> — "In 8 min · English".</summary>
    Gap,
}

/// <summary>One look of the calendar card.</summary>
/// <param name="Event">The event it is about (for <see cref="CalendarLookKind.Gap"/>, the next one).</param>
/// <param name="Tag">This look of this event — what closing the card remembers (a closed look doesn't come back; the next one does).</param>
/// <param name="At">When this look began (orders the card among the others).</param>
/// <param name="From">When the wait began — the reminder going off, the last event ending (<see cref="CalendarLookKind.Remind"/>, <see cref="CalendarLookKind.Gap"/>), else null.</param>
public sealed record CalendarLook(CalendarLookKind Kind, IcsOccurrence Event, string Tag, DateTimeOffset At, DateTimeOffset? From = null)
{
    /// <summary>The look takes the big pill for a moment (a debut) instead of staying in its cell or pinned.</summary>
    public bool Debuts => Kind is CalendarLookKind.Start or CalendarLookKind.End;
}

/// <summary>
/// The calendar's rules (user 10-10, TODO ③ calendar): <b>one card all day</b> that only changes looks. Classes and other events
/// are treated alike. Worked out from the time, the events and the looks closed by hand alone — nothing else is remembered, so the
/// same answer comes out whenever the app starts.
/// <list type="number">
/// <item>Changes (an event starting or ending) are <b>moments</b>: each takes the big pill for <see cref="DebutLength"/>, one after
/// another in time order — two at once (one class ends as the next starts) each get their full time, no cut-off is worked out.</item>
/// <item>Between moments, the steadiest thing wins: an event that started with an "at the start" reminder (held until closed) →
/// a reminder that went off → the event under way → a gap before the same calendar's next event → nothing.</item>
/// <item>Overlapping events (user 10-10, choice B): an event that starts while the main card's event is still under way gets <b>a
/// card of its own</b> for its whole life (reminder, start, under way, end — gone after its end); else it goes on the main card.
/// So there is a second card only while two events really overlap (<see cref="Cards"/>).</item>
/// </list>
/// All-day events are left out.
/// </summary>
public static class CalendarLogic
{
    /// <summary>How long each start / end shows on the big pill (user 10-10: the calendar's debuts are 6 s).</summary>
    public static readonly TimeSpan DebutLength = TimeSpan.FromSeconds(6);

    /// <summary>The longest gap before the same calendar's next event that still shows as a gap (user 10-10).</summary>
    public static readonly TimeSpan GapLimit = TimeSpan.FromMinutes(25);

    /// <summary>An event with no end but a reminder at its start stays held this long at most when nobody closes it.</summary>
    public static readonly TimeSpan HeldMoment = TimeSpan.FromMinutes(10);

    /// <summary>How far back moments are looked for (a chain of 6 s moments never gets near).</summary>
    private static readonly TimeSpan Lookback = TimeSpan.FromMinutes(10);

    /// <summary>Range of occurrences the caller should expand around now: events under way can have started days ago, reminders go off days ahead.</summary>
    public static readonly TimeSpan ExpandBefore = TimeSpan.FromDays(2);

    /// <inheritdoc cref="ExpandBefore"/>
    public static readonly TimeSpan ExpandAfter = TimeSpan.FromDays(8);

    /// <summary>
    /// Every calendar card now: the main one (<see cref="MainCard"/>) and one per overlapping event (its id), each with its look.
    /// Cards with nothing to show are left out. <paramref name="closed"/> = the <see cref="CalendarLook.Tag"/>s closed by hand.
    /// </summary>
    public static IReadOnlyList<(string Card, CalendarLook Look)> Cards(
        IEnumerable<IcsOccurrence> occurrences, DateTimeOffset now, IReadOnlySet<string>? closed = null)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        var timed = Timed(occurrences);
        closed ??= new HashSet<string>();

        // an event goes on the main card unless the main card's event is still under way when it starts
        var main = new List<IcsOccurrence>();
        var own = new List<IcsOccurrence>();
        foreach (var o in timed)
        {
            (main.Any(m => m.Start <= o.Start && o.Start < m.End) ? own : main).Add(o);
        }

        var cards = new List<(string, CalendarLook)>();
        if (LookIn(main, now, closed) is { } look)
        {
            cards.Add((MainCard, look));
        }

        foreach (var o in own)
        {
            if (LookIn([o], now, closed) is { } its)
            {
                cards.Add((o.Id, its));
            }
        }

        return cards;
    }

    /// <summary>The main card's name in <see cref="Cards"/>.</summary>
    public const string MainCard = "";

    /// <summary>The main card's look now (<see cref="Cards"/>); null = none.</summary>
    public static CalendarLook? Look(IEnumerable<IcsOccurrence> occurrences, DateTimeOffset now, IReadOnlySet<string>? closed = null)
        => Cards(occurrences, now, closed).FirstOrDefault(c => c.Card == MainCard).Look;

    /// <summary>The look of one card, from its events.</summary>
    private static CalendarLook? LookIn(List<IcsOccurrence> timed, DateTimeOffset now, IReadOnlySet<string> closed)
    {
        // the moments, played one after another
        var cursor = DateTimeOffset.MinValue;
        foreach (var moment in Moments(timed, now - Lookback, now).Where(m => !closed.Contains(m.Tag)))
        {
            var from = moment.At > cursor ? moment.At : cursor;
            cursor = from + DebutLength;
            if (now >= from && now < cursor)
            {
                return moment;
            }
        }

        return Steady(timed, now, closed);
    }

    /// <summary>Minutes left before <paramref name="start"/>, rounded up (at least 1).</summary>
    public static int MinutesLeft(DateTimeOffset start, DateTimeOffset now)
        => Math.Max(1, (int)Math.Ceiling((start - now).TotalMinutes));

    private static List<IcsOccurrence> Timed(IEnumerable<IcsOccurrence> occurrences)
        => [.. occurrences.Where(o => !o.AllDay).DistinctBy(o => o.Id).OrderBy(o => o.Start).ThenBy(o => o.Id, StringComparer.Ordinal)];

    /// <summary>A reminder at the start (under a minute before it counts too).</summary>
    private static bool AtStart(IcsOccurrence o) => o.Alarms?.Any(a => a < TimeSpan.FromMinutes(1)) == true;

    /// <summary>The card's event under way at <paramref name="at"/> (one card's events never overlap; ties: the one that ends first).</summary>
    private static IcsOccurrence? Main(List<IcsOccurrence> timed, DateTimeOffset at)
    {
        IcsOccurrence? best = null;
        foreach (var o in timed)
        {
            if (o.Start <= at && at < o.End && (best is not { } b || o.End < b.End))
            {
                best = o;   // timed is in start order, so a tie on the end keeps the earlier start
            }
        }

        return best;
    }

    /// <summary>Starts and ends within [from, to], in the order they show: by time, an end before a start at the same time.</summary>
    private static IEnumerable<CalendarLook> Moments(List<IcsOccurrence> timed, DateTimeOffset from, DateTimeOffset to)
    {
        var moments = new List<(CalendarLook Look, int Order)>();
        foreach (var o in timed)
        {
            // a start: on the card if it is the one the card follows (an event with no end always is); held instead with a reminder at the start
            if (o.Start >= from && o.Start <= to && !AtStart(o) && (o.End <= o.Start || Main(timed, o.Start)?.Id == o.Id))
            {
                moments.Add((new CalendarLook(CalendarLookKind.Start, o, "start:" + o.Id, o.Start), 1));
            }

            // an end: of the event the card followed until then
            if (o.End > o.Start && o.End >= from && o.End <= to && Main(timed, o.End - TimeSpan.FromTicks(1))?.Id == o.Id)
            {
                moments.Add((new CalendarLook(CalendarLookKind.End, o, "end:" + o.Id, o.End), 0));
            }
        }

        return moments.OrderBy(m => m.Look.At).ThenBy(m => m.Order).ThenBy(m => m.Look.Event.Id, StringComparer.Ordinal).Select(m => m.Look);
    }

    private static CalendarLook? Steady(List<IcsOccurrence> timed, DateTimeOffset now, IReadOnlySet<string> closed)
    {
        foreach (var o in timed)
        {
            var until = o.End > o.Start ? o.End : o.Start + HeldMoment;
            if (AtStart(o) && o.Start <= now && now < until && !closed.Contains("held:" + o.Id))
            {
                return new CalendarLook(CalendarLookKind.Held, o, "held:" + o.Id, o.Start);
            }
        }

        foreach (var o in timed.Where(o => o.Start > now))
        {
            // the latest of its reminders that went off (closing it waits for the next one)
            var went = o.Alarms?.Where(a => a >= TimeSpan.FromMinutes(1) && o.Start - a <= now).Order().FirstOrDefault() ?? TimeSpan.Zero;
            var tag = "remind:" + o.Id + ":" + ((int)went.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (went > TimeSpan.Zero && !closed.Contains(tag))
            {
                return new CalendarLook(CalendarLookKind.Remind, o, tag, o.Start - went, o.Start - went);
            }
        }

        if (Main(timed, now) is { } main)
        {
            return closed.Contains("now:" + main.Id) ? null : new CalendarLook(CalendarLookKind.Now, main, "now:" + main.Id, main.Start);
        }

        // a gap: after the last event that ended, the same calendar's next one starts within the limit
        IcsOccurrence? last = null;
        foreach (var o in timed)
        {
            if (o.End > o.Start && o.End <= now && (last is not { } l || o.End >= l.End))
            {
                last = o;
            }
        }

        if (last is not { } ended)
        {
            return null;
        }

        foreach (var next in timed)
        {
            if (next.Calendar == ended.Calendar && next.Start >= ended.End && next.Start > now)
            {
                var tag = "gap:" + next.Id;
                return next.Start - ended.End <= GapLimit && !closed.Contains(tag)
                    ? new CalendarLook(CalendarLookKind.Gap, next, tag, ended.End, ended.End)
                    : null;   // timed is in start order: the first one after is the next
            }
        }

        return null;
    }
}
