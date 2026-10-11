using System.Globalization;
using System.Net.Http;
using IsleBar.Core.Island;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// Calendar notices (TODO ③ calendar, user 10-10): <b>one card all day</b> (a second one only while two events overlap) that changes looks — reminder, started, under way, ended,
/// gap — worked out by <see cref="CalendarLogic"/>. Fetches every calendar in the settings (the "secret iCal address" of
/// Google/Outlook calendars) every 10 minutes and works the look out again every second. If a fetch fails (offline, bad address),
/// that calendar keeps its last events.
/// Closing the card (<see cref="Close"/>) remembers that look until the end of the day: the next one comes as usual.
/// The addresses contain a secret token, so they are never logged anywhere. No UI thread needed.
/// <para>Test switch: a file <c>%LOCALAPPDATA%\IsleBar\calendar-test.ics</c> is read as one more calendar, again whenever it
/// changes (to try the card with events a few minutes ahead — Google updates its address slowly). Delete it when done.</para>
/// </summary>
internal sealed class CalendarWatcher : NoticeWatcher
{
    /// <summary>The board key of the main card (its card key is <c>system:calendar</c>); an overlapping event's is this + ":" + its id.</summary>
    private const string CardKey = "calendar";

    private static readonly TimeSpan FetchInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ExpandEvery = TimeSpan.FromMinutes(1);
    private static readonly HttpClient Http = CreateClient();

    private static readonly string TestFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IsleBar", "calendar-test.ics");

    private readonly string[] _urls;
    private IReadOnlyList<IcsEvent> _testEvents = [];

    /// <summary>Each calendar's own name (the test file's last), for the lifted card's source row; null = "日历".</summary>
    private readonly string?[] _names;
    private DateTime _testStamp;
    private readonly IReadOnlyList<IcsEvent>[] _events;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly HashSet<string> _closed = new(StringComparer.Ordinal);
    private IReadOnlyList<IcsOccurrence> _occurrences = [];
    private DateTimeOffset _expandedAt = DateTimeOffset.MinValue;
    private DateTime _closedDay;
    private string? _lastLook;
    private Timer? _tick;
    private bool _started;

    /// <param name="calendars">The addresses, one per line (<see cref="SystemWatchOptions.Calendars"/>).</param>
    public CalendarWatcher(Func<Core.Localization.LanguageStrings> strings, string calendars)
        : base(strings)
    {
        _urls = Normalize(calendars);
        _events = [.. _urls.Select(_ => (IReadOnlyList<IcsEvent>)[])];   // + the test file's, kept apart
        _names = new string?[_urls.Length + 1];
    }

    /// <summary>The addresses this watcher uses, normalized and one per line (to compare whether the setting changed).</summary>
    public string Urls => string.Join('\n', _urls);

    public override void Start() => Guard(() =>
    {
        if (_started || _urls.Length == 0)
        {
            return;
        }

        _started = true;
        for (var i = 0; i < _urls.Length; i++)
        {
            var index = i;
            _ = Task.Run(() => FetchLoopAsync(index));
        }

        _tick = new Timer(_ => Guard(Tick), null, TickInterval, TickInterval);
    });

    /// <summary>
    /// The card was closed by hand: this look stays away (a reminder until the next reminder, a gap until the next event starts,
    /// one under way until it ends…). True when the card was the calendar's.
    /// </summary>
    public bool Close(ActivityState card)
    {
        if (card.SourcePath?.StartsWith(SystemNotice.SourcePrefix + CardKey, StringComparison.Ordinal) != true || card.Look is not { } look)
        {
            return false;
        }

        lock (_gate)
        {
            _closed.Add(look);
        }

        Interop.AppLog.Write("calendar: closed " + look.Split(':')[0]);   // the kind only: the rest names the event
        Guard(Tick);
        return true;
    }

    protected override void Stop()
    {
        _stop.Cancel();
        _tick?.Dispose();
        _stop.Dispose();
    }

    private async Task FetchLoopAsync(int index)
    {
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var text = await Http.GetStringAsync(_urls[index], token).ConfigureAwait(false);
                var events = IcsParser.Parse(text, calendar: index);
                if (events.Count > 0 || text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_gate)
                    {
                        _events[index] = events;   // keep the previous events if the response is bogus (e.g. a login page)
                        _names[index] = IcsParser.CalendarName(text);
                        _expandedAt = DateTimeOffset.MinValue;
                    }

                    Interop.AppLog.Write($"calendar: #{index + 1} fetched, {events.Count} events, "
                                         + $"{events.Count(e => e.Alarms.Count > 0)} with reminders");
                }

                Guard(Tick);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException or UriFormatException)
            {
                // Retry on the next round (timeouts arrive as OperationCanceledException)
                Interop.AppLog.Write($"calendar: #{index + 1} fetch failed ({ex.GetType().Name}), keeps its last events");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // an unexpected parse failure (e.g. an RRULE UNTIL at year 9999) ended the loop silently — the calendar stopped
                // refreshing until a restart (review 10-03). Keep the last events and try again next time.
                Interop.AppLog.Write("calendar: refresh failed (" + ex.GetType().Name + ")");   // not the message: it can quote a calendar line
            }

            try
            {
                await Task.Delay(FetchInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        ReadTestFile();
        IReadOnlyList<IcsOccurrence> occurrences;
        HashSet<string> closed;
        lock (_gate)
        {
            if (now - _expandedAt >= ExpandEvery)
            {
                _occurrences = IcsSchedule.Occurrences(_events.SelectMany(e => e).Concat(_testEvents),
                    now - CalendarLogic.ExpandBefore, now + CalendarLogic.ExpandAfter);
                _expandedAt = now;
            }

            if (_closedDay != DateTime.Today)
            {
                _closedDay = DateTime.Today;   // what was closed yesterday can't come back: forget it
                _closed.Clear();
            }

            occurrences = _occurrences;
            closed = [.. _closed];
        }

        // the main card, plus one per overlapping event (user 10-10, choice B) — swapped in at once, so nothing flickers
        var s = Text;
        var cards = CalendarLogic.Cards(occurrences, now, closed);
        Board.ReplaceAll(cards.Select(c => new KeyValuePair<string, ActivityState>(
            c.Card == CalendarLogic.MainCard ? CardKey : CardKey + ":" + c.Card, Named(CardFor(c.Look, now, s), c.Look.Event.Calendar))));

        var looks = string.Join(" + ", cards.Select(c => c.Look.Kind.ToString()));
        var tags = string.Join("|", cards.Select(c => c.Look.Tag));
        if (tags != _lastLook)
        {
            _lastLook = tags;
            Interop.AppLog.Write("calendar: look " + (looks.Length == 0 ? "none" : looks));
        }
    }

    /// <summary>The test switch's calendar (see the class notes), read again when the file changes.</summary>
    private void ReadTestFile()
    {
        var stamp = File.Exists(TestFile) ? File.GetLastWriteTimeUtc(TestFile) : default;
        if (stamp == _testStamp)
        {
            return;
        }

        _testStamp = stamp;
        var test = stamp == default ? null : File.ReadAllText(TestFile);
        var events = IcsParser.Parse(test, calendar: _urls.Length);
        lock (_gate)
        {
            _testEvents = events;
            _names[_urls.Length] = IcsParser.CalendarName(test);
            _expandedAt = DateTimeOffset.MinValue;
        }

        Interop.AppLog.Write($"calendar: test file {(stamp == default ? "gone" : $"read, {events.Count} events")}");
    }

    /// <summary>The card for a look: its words, colour and type. No progress bar (user 10-10: "in 8 min" already says it).</summary>
    /// <remarks>
    /// The words come first and the event's name after, grey (user 10-10: a long name must not push "ended" out of sight; no " · "
    /// between them): <see cref="ActivityState.Title"/> = the words, <see cref="ActivityState.Msg"/> = the name,
    /// <see cref="ActivityState.Name"/> = its time "8:00 – 9:35" (only on the lifted card).
    /// </remarks>
    /// <summary>The lifted card's source row: the event's calendar by its own name ("课程表"), else "日历" (10-11).</summary>
    private ActivityState Named(ActivityState card, int calendar)
    {
        lock (_gate)
        {
            card.AppName = calendar >= 0 && calendar < _names.Length ? _names[calendar] : null;
        }

        card.AppName ??= Text.CardCalendar;
        return card;
    }

    private static ActivityState CardFor(CalendarLook look, DateTimeOffset now, Core.Localization.LanguageStrings s)
    {
        var o = look.Event;
        var (words, stage, state) = look.Kind switch
        {
            CalendarLookKind.Remind => (NoticeText.EventWhen(s, o.Start, now, CultureInfo.CurrentCulture), SystemNotice.KeepStage, SystemNotice.WaitState),
            CalendarLookKind.Held => (s.NoticeEventNow, SystemNotice.KeepStage, SystemNotice.WaitState),
            CalendarLookKind.Start => (s.NoticeEventNow, SystemNotice.HoldStage, SystemNotice.WaitState),
            CalendarLookKind.Now => (s.NoticeEventOngoing, SystemNotice.HoldStage, "run"),
            CalendarLookKind.End => (s.NoticeEventEnded, SystemNotice.HoldStage, "done"),
            _ => (NoticeText.EventSoon(s, CalendarLogic.MinutesLeft(o.Start, now)), SystemNotice.KeepStage, "done"),
        };

        var span = o.Until > o.Start ? Clock(o.Start) + " – " + Clock(o.Until) : Clock(o.Start);
        var card = SystemNotice.Make(words, NoticeGlyphs.Calendar, look.At, msg: Summary(o));
        card.Name = span;
        card.Stage = stage;
        card.State = state;
        card.Look = look.Tag;
        card.DebutFor = look.Debuts ? CalendarLogic.DebutLength : null;
        return card;
    }

    private static string Summary(IcsOccurrence o)
        => string.IsNullOrWhiteSpace(o.Summary) ? Clock(o.Start) : o.Summary;

    private static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    /// <summary>The addresses from the settings, one per line: normalized (<see cref="NormalizeUrl"/>), invalid ones and repeats left out.</summary>
    internal static string[] Normalize(string? calendars)
        => [.. (calendars ?? string.Empty).Split('\n').Select(NormalizeUrl).Where(u => u.Length > 0).Distinct()];

    /// <summary><c>webcal://</c> becomes https. Trims whitespace. Empty string (= off) if not http(s).</summary>
    internal static string NormalizeUrl(string? url)
    {
        var value = (url ?? string.Empty).Trim();
        if (value.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value["webcal://".Length..];
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? value
            : string.Empty;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 20 * 1024 * 1024,   // room even for calendars with years of events
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IsleBar/1.0");
        return client;
    }
}
