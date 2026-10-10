using System.Globalization;
using IsleBar.Core.Island;
using IsleBar.Core.Localization;
using IsleBar.Core.SystemWatch;

namespace IsleBar.Core.Ui;

/// <summary>What a lifted card's middle zone holds (TODO 7.9 F2): words, one big number, or a cover beside a title.</summary>
public enum SheetLayout
{
    Text,
    Number,
    Media,
}

/// <summary>Colour of the state word: plain, or the same green / orange / red as the pills.</summary>
public enum SheetTone
{
    Plain,
    Ok,
    Waiting,
    Bad,
}

/// <summary>What a card's button does (the window hands it back to the bar).</summary>
public enum SheetAction
{
    Close,
    Hide,
    OpenChat,
    Open,
    OpenFolder,
    TimerPause,
    TimerResume,
    TimerCancel,
    TimerAgain,
    MediaPrevious,
    MediaPlayPause,
    MediaNext,
    PrivacySettings,
}

/// <summary>A button: words, or (media) an icon from Segoe Fluent Icons.</summary>
public sealed record SheetButton(SheetAction Action, string? Label, string? Glyph = null);

/// <summary>The progress zone: share done, the words left and right above the bar (either may be empty), its colour.</summary>
public sealed record SheetProgress(double Fraction, string? Left, string? Right, SheetTone Tone = SheetTone.Plain);

/// <summary>
/// <b>The inside of a lifted card</b> (TODO 7.9 F2, preview https://claude.ai/artifact/G4DcS2xPmx8jmgtMscdiMT): four zones top to
/// bottom — source row, middle, progress (optional), buttons (optional). Built from the card alone, so it can be checked without a
/// window; <see cref="CardSheets.Build"/> holds every card's rules in one place.
/// </summary>
public sealed record CardSheet(
    string Source,
    string? State,
    SheetTone Tone,
    string? Time,
    SheetLayout Layout,
    string? Title,
    IReadOnlyList<string> Body,
    string? Raw,
    string? Number,
    string? NumberCaption,
    SheetProgress? Progress,
    IReadOnlyList<SheetButton> Buttons,
    bool Adaptive)
{
    /// <summary>The body is one Markdown text (an agent's answer), laid out by <see cref="CardMarkdown"/> (F2b, user 10-08).</summary>
    public bool Markdown { get; init; }

    /// <summary>One grey 12 line under the body (an agent at work: its latest step, under what it last said — user 10-09).</summary>
    public string? Footnote { get; init; }
}

public static class CardSheets
{
    /// <summary>
    /// Sizes and type, in dip (user 10-08): only three sizes of type — 12/16 notes, 14/20 content, 40/48 the one big number —
    /// at most one bold thing per zone, everything left-aligned (a button's words centre in the button).
    /// </summary>
    public const double Padding = 16;
    public const double SourceHeight = 16;
    public const double Gap = 12;
    public const double ButtonHeight = 32;
    public const double ButtonGap = 8;
    public const double BarHeight = 4;
    public const double BarLabelGap = 6;
    public const double TitleBodyGap = 2;

    /// <summary>
    /// A card's title and the text under it, every stage alike (user 10-10): 2 apart it read as the text's first line. Now the title's
    /// Han characters are really bold (see SheetView.SetWords) the weight parts them, so a little over the paragraphs' 6 is enough
    /// (12 looked too loose). Tried and dropped: a hairline between, a grey 12 title (the same look as a section name).
    /// </summary>
    public const double TitleGap = 8;
    public const double ParagraphGap = 8;
    public const double Fade = 16;
    public const double NoteSize = 12, NoteLine = 16;
    public const double TextSize = 14, TextLine = 20;
    public const double NumberSize = 40, NumberLine = 48;

    /// <summary>The cover beside a song: 56 when there's a progress zone (the middle is 58 high), 72 without (96).</summary>
    public static double CoverSize(bool progress) => progress ? 56 : 72;

    /// <summary>The middle zone's height in a card <paramref name="height"/> high (dip).</summary>
    public static double MiddleHeight(CardSheet sheet, double height)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var h = height - (2 * Padding) - SourceHeight - Gap;
        if (sheet.Progress is { } p)
        {
            h -= Gap + BarHeight + (p.Left is null && p.Right is null ? 0 : NoteLine + BarLabelGap);
        }

        if (sheet.Buttons.Count > 0)
        {
            h -= Gap + ButtonHeight;
        }

        return h;
    }

    /// <summary>
    /// Every card's inside (user 10-08). Fixed 3:2: timer, music, mic / camera. Adaptive (3:2 when the middle fits, else 3:4):
    /// agent done / needs you / working (user 10-09: what it says shouldn't be cut) / stopped, notifications, copied, downloads.
    /// </summary>
    /// <param name="tail">An agent's last answer / latest step, read by the App from its session record (F2b); null = none.</param>
    public static CardSheet Build(Card card, DateTimeOffset now, LanguageStrings text, AgentTail? tail = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(text);
        var s = card.State;
        var close = CloseFor(card, text);
        return s.Kind switch
        {
            ActivityKind.AgentDone => Agent(s, text.Done, SheetTone.Ok, Ago(s.UpdatedAt, now, text), [tail?.Answer ?? s.Msg], [Chat(text), .. close])
                with { Markdown = tail?.Answer is not null },
            ActivityKind.AgentPermission => Agent(s, Cap(text.NeedsAnswer), SheetTone.Waiting, Ago(s.UpdatedAt, now, text), [tail?.Step ?? s.Msg], [Chat(text), .. close]),
            ActivityKind.AgentWorking => Working(s, now, text, tail),
            ActivityKind.Error or ActivityKind.Notice when s.Agent is not null => AgentStopped(s, now, text, close),
            ActivityKind.Timer => Timer(card, now, text),
            ActivityKind.Music => Music(s, text),
            ActivityKind.Transfer => Transfer(card, now, text, close),
            ActivityKind.Notice when s.Stage == "privacy" => Privacy(s, now, text),
            // the newest notification, then the ones merged into the card before it, newest first (user 10-10: "+N" on the pill)
            ActivityKind.Notice when s.Stage == "expand" => Sheet(
                s.Name ?? text.CardSystem, null, SheetTone.Plain, Ago(Start(s), now, text), s.Title,
                [s.Msg, .. s.Earlier.Reverse().Select(e => string.Join(" · ", new[] { e.Title, e.Msg }.Where(t => !string.IsNullOrWhiteSpace(t))))],
                [new(SheetAction.Open, text.MenuOpen), .. close]),
            // the calendar (10-10): the event's name, its state in the card's colour, its time "8:00 – 9:35"
            ActivityKind.Notice when s.Look is not null => Sheet(
                text.CardSystem, s.Title,
                s.State == SystemWatch.SystemNotice.WaitState ? SheetTone.Waiting : s.RunState == ActivityRunState.Done ? SheetTone.Ok : SheetTone.Plain,
                null, s.Msg ?? s.Title, [s.Name], close),
            ActivityKind.Notice => Sheet(
                text.CardSystem, null, SheetTone.Plain, Ago(Start(s), now, text), s.Title, [s.Msg, s.Name], close),
            _ => Sheet(
                text.CardSystem, s.RunState == ActivityRunState.Error ? text.Fail : null, SheetTone.Bad, Ago(Start(s), now, text),
                s.Title ?? s.Name, [s.Msg], close),
        };
    }

    // ---------------- each kind ----------------

    private static CardSheet Agent(ActivityState s, string state, SheetTone tone, string? time, string?[] body, IReadOnlyList<SheetButton> buttons)
        => Sheet(Maker(s), state, tone, time, s.Name ?? $"{s.AgentLabel} {state}", body, buttons);

    /// <summary>
    /// At work (user 10-09): what it last said in this turn (Markdown, like an answer), its latest step in grey under it. Nothing
    /// said yet: just the step.
    /// </summary>
    private static CardSheet Working(ActivityState s, DateTimeOffset now, LanguageStrings text, AgentTail? tail)
        => tail?.Said is { } said
            ? Agent(s, Cap(text.Working), SheetTone.Plain, Running(s, now, text), [said], [Chat(text)]) with { Markdown = true, Footnote = tail.Step }
            : Agent(s, Cap(text.Working), SheetTone.Plain, Running(s, now, text), [tail?.Step ?? s.Msg], [Chat(text)]);

    /// <summary>Usage limit, stopped, interrupted: the reason is the title, then what it says and which chat.</summary>
    private static CardSheet AgentStopped(ActivityState s, DateTimeOffset now, LanguageStrings text, IReadOnlyList<SheetButton> close)
        => Sheet(Maker(s), s.RunState == ActivityRunState.Error ? text.Fail : null, SheetTone.Bad, Ago(Start(s), now, text),
            s.Title, [s.Msg, s.Name], [Chat(text), .. close]);

    private static CardSheet Timer(Card card, DateTimeOffset now, LanguageStrings text)
    {
        var s = card.State;
        var stopwatch = TimerParser.IsStopwatch(s);
        var left = TimerParser.Remaining(s, now);
        var over = !stopwatch && left is { } l && l <= TimeSpan.Zero;
        var total = s.Total is > 0 ? TimeSpan.FromSeconds(s.Total.Value) : (TimeSpan?)null;
        var shown = stopwatch ? TimerParser.Elapsed(s, now) ?? TimeSpan.Zero
            : over ? total ?? TimeSpan.Zero
            : left ?? TimeSpan.Zero;

        var start = s.T0 is { } t0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(t0 * 1000)) : (DateTimeOffset?)null;
        var end = s.Due is { } due ? DateTimeOffset.FromUnixTimeMilliseconds((long)(due * 1000)) : (DateTimeOffset?)null;
        SheetProgress? bar = stopwatch ? null : new SheetProgress(
            over ? 1 : TimerParser.Fraction(s, now) ?? 0,
            start is { } a ? Fill(text.CardStarted, Clock(a)) : null,
            end is { } b && !s.IsPaused ? Fill(text.CardEnds, Clock(b)) : null,
            over ? SheetTone.Ok : SheetTone.Plain);

        IReadOnlyList<SheetButton> buttons = over
            ? [new(SheetAction.TimerAgain, text.CardAgain), new(SheetAction.Close, text.CloseCard)]
            : [s.IsPaused ? new(SheetAction.TimerResume, text.CardResume) : new(SheetAction.TimerPause, text.CardPause), new(SheetAction.TimerCancel, text.CloseCard)];

        return new CardSheet(
            string.IsNullOrWhiteSpace(s.Name) ? text.Timer : s.Name,
            over ? text.CardEnded : s.IsPaused ? text.Paused : text.CardRunning,
            over ? SheetTone.Ok : SheetTone.Plain,
            total is { } t ? Length(t, text) : null,
            SheetLayout.Number,
            null,
            [],
            null,
            Digits(shown),
            stopwatch || over ? null : text.CardRemaining,
            bar,
            buttons,
            Adaptive: false);
    }

    private static CardSheet Music(ActivityState s, LanguageStrings text)
    {
        var playing = s.Stage != "paused";
        SheetProgress? bar = s.Total is > 0 && s.Done is { } done
            ? new SheetProgress(Math.Clamp((double)done / s.Total.Value, 0, 1), Digits(TimeSpan.FromMilliseconds(done)), Digits(TimeSpan.FromMilliseconds(s.Total.Value)))
            : null;
        return new CardSheet(
            string.IsNullOrWhiteSpace(s.AppName) ? text.CardMusic : s.AppName, playing ? text.CardPlaying : text.Paused, SheetTone.Plain, null, SheetLayout.Media,
            s.Name, s.Msg is { } artist ? [artist] : [], null, null, null, bar,
            [
                new(SheetAction.MediaPrevious, null, ""),
                new(SheetAction.MediaPlayPause, null, playing ? "" : ""),
                new(SheetAction.MediaNext, null, ""),
            ],
            Adaptive: false);
    }

    /// <summary>Downloads and Explorer's copies: no bar for a browser download — its total size is never known (10-04).</summary>
    private static CardSheet Transfer(Card card, DateTimeOffset now, LanguageStrings text, IReadOnlyList<SheetButton> close)
    {
        var s = card.State;
        var copy = s.Stage == "copy";
        var (state, tone) = s.RunState switch
        {
            ActivityRunState.Done => (text.Done, SheetTone.Ok),
            ActivityRunState.Error => (text.Fail, SheetTone.Bad),
            _ when s.IsPausedTransfer => (text.Paused, SheetTone.Plain),
            _ => (copy ? text.CardRunning : text.Downloading, SheetTone.Plain),
        };

        var running = s.RunState == ActivityRunState.Running;
        var several = !copy && running && s.Downloads is { Count: > 1 };
        var lines = new List<string?>();
        if (!copy && s.Done is { } bytes && s.Fraction is null && running && !several)
        {
            // one download: where from, how much came, how fast (F2b: the source, 10-09)
            var source = s.Downloads is [{ } only] ? only.Source ?? text.CardBrowser : null;
            lines.Add((source is null ? string.Empty : source + " · ") + Fill(text.CardDownloaded, Bytes(bytes))
                      + (s.Speed is { } speed ? $" · {Bytes((long)speed)}/s" : string.Empty));
        }

        SheetProgress? bar = s.Fraction is { } f && running ? new SheetProgress(f, null, $"{f * 100:0}%") : null;
        var name = s.Name ?? (copy ? text.FileTransfer : text.Downloading);
        IReadOnlyList<SheetButton> buttons = card.Type == CardType.Status
            ? copy ? [new(SheetAction.Hide, text.CardHide)] : [new(SheetAction.OpenFolder, text.CardOpenFolder), new(SheetAction.Hide, text.CardHide)]
            : s.Open is not null ? [new(SheetAction.Open, text.MenuOpen), .. close] : close;
        var sheet = Sheet(copy ? text.FileTransfer : text.CardDownloads, state, tone, Ago(Start(s), now, text),
            several ? null : s.MoreCount > 0 ? $"{name} +{s.MoreCount}" : name,
            several ? [DownloadList(s.Downloads!, text)] : [.. lines], buttons) with { Progress = bar };
        return several ? sheet with { Markdown = true } : sheet;
    }

    /// <summary>
    /// Several downloads (F2b, 10-08 "每个来源一行"): per source a grey line with its summed speed ("Edge · 3.2 MB/s", or paused),
    /// then each of its files — its name when the browser has given it (Chrome/Edge's "Unconfirmed 123" has none) and how much came.
    /// Written as Markdown so the card lays it out like an answer: section names and a list.
    /// </summary>
    private static string DownloadList(IReadOnlyList<DownloadLine> downloads, LanguageStrings text)
    {
        var md = new System.Text.StringBuilder();
        foreach (var group in downloads.GroupBy(d => d.Source ?? text.CardBrowser))
        {
            var speeds = group.Where(d => d.Speed is not null).Select(d => d.Speed!.Value).ToList();
            var how = group.All(d => d.Paused) ? text.Paused : speeds.Count > 0 ? $"{Bytes((long)speeds.Sum())}/s" : null;
            md.Append("## ").Append(Escape(group.Key)).Append(how is null ? string.Empty : " · " + how).Append('\n');
            foreach (var d in group)
            {
                md.Append("- ").Append(d.Name is { } n ? Escape(n) + " · " : string.Empty).Append(Fill(text.CardDownloaded, Bytes(d.Size)))
                  .Append(d.Paused && !group.All(x => x.Paused) ? " · " + text.Paused : string.Empty).Append('\n');
            }

            md.Append('\n');
        }

        return md.ToString().TrimEnd();
    }

    /// <summary>A file name as Markdown text: its marks escaped, so "my_*draft*.zip" stays as it is.</summary>
    private static string Escape(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '#' or '|' or '~')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Mic / camera in use: one program — its name is the title; several — one line each, newest first, as a list (F2b, 10-09).
    /// Adaptive: a long list grows the card instead of being cut.
    /// </summary>
    private static CardSheet Privacy(ActivityState s, DateTimeOffset now, LanguageStrings text)
    {
        var apps = s.Apps is { Count: > 1 } many ? many : null;
        var sheet = Sheet(s.Glyph == NoticeGlyphs.Camera ? text.CardCamera : text.CardMicrophone, text.CardInUse, SheetTone.Plain,
            Ago(s.UpdatedAt, now, text), apps is null ? s.Name : null,
            apps is null ? [] : [string.Join('\n', apps.Select(a => "- " + a))],
            [new(SheetAction.PrivacySettings, text.CardPrivacy), new(SheetAction.Hide, text.CardHide)]);
        return sheet with { Markdown = apps is not null };
    }

    // ---------------- parts ----------------

    private static CardSheet Sheet(string source, string? state, SheetTone tone, string? time, string? title, IEnumerable<string?> body, IReadOnlyList<SheetButton> buttons)
        => new(source, state, state is null ? SheetTone.Plain : tone, time, SheetLayout.Text, title,
            body.Where(b => !string.IsNullOrWhiteSpace(b) && b != title).Select(b => b!).Distinct().ToList(),
            null, null, null, null, buttons, Adaptive: true);

    /// <summary>Close (告知 / 待处理), hide (状态); a temporary card has none — it goes when it's put back.</summary>
    private static IReadOnlyList<SheetButton> CloseFor(Card card, LanguageStrings text) => card.Type switch
    {
        CardType.Temporary => [],
        CardType.Status => [new(SheetAction.Hide, text.CardHide)],
        _ => [new(SheetAction.Close, text.CloseCard)],
    };

    private static SheetButton Chat(LanguageStrings text) => new(SheetAction.OpenChat, text.CardOpenChat);

    private static string Maker(ActivityState s) => s.AgentLabel == "Codex" ? "Codex" : "Claude Code";

    private static DateTimeOffset Start(ActivityState s)
        => s.T0 is { } t0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(t0 * 1000)) : s.UpdatedAt;

    /// <summary>"just now", "2 min ago" within the hour, then the clock "14:32" (user 10-08, as Windows' notification centre).</summary>
    public static string? Ago(DateTimeOffset at, DateTimeOffset now, LanguageStrings text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (at == default)
        {
            return null;
        }

        var gone = now - at;
        return gone < TimeSpan.FromMinutes(1) ? text.CardJustNow
            : gone < TimeSpan.FromHours(1) ? Fill(text.CardMinutesAgo, ((int)gone.TotalMinutes).ToString(CultureInfo.InvariantCulture))
            : Clock(at);
    }

    private static string Running(ActivityState s, DateTimeOffset now, LanguageStrings text)
    {
        var minutes = Math.Max(1, (int)(now - Start(s)).TotalMinutes);
        return Fill(text.CardRunningFor, minutes.ToString(CultureInfo.InvariantCulture));
    }

    private static string Length(TimeSpan t, LanguageStrings text)
        => t.TotalMinutes >= 1 && t.Seconds == 0 ? text.Minutes.Replace("{0}", ((int)t.TotalMinutes).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) : Digits(t);

    private static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"18:42" or "1:02:03".</summary>
    public static string Digits(TimeSpan t)
    {
        var total = (int)Math.Ceiling(Math.Max(0, t.TotalSeconds));
        return total >= 3600
            ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
    }

    private static string Bytes(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
    };

    private static string Fill(string format, string value) => format.Replace("{}", value, StringComparison.Ordinal);

    /// <summary>"working" → "Working": the agent words are lower-case to follow a name on the pill; here they stand alone.</summary>
    private static string Cap(string word)
        => word.Length == 0 ? word : char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..];
}
