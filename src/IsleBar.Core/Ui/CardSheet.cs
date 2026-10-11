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

    /// <summary>A list of files, each its own row: dragged out one by one, ✕ to take it out (the transit station, 10-10).</summary>
    Files,
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
    Eject,
    Clear,

    /// <summary>A game Steam has just installed: start it (<c>steam://rungameid/{id}</c>, user 10-11).</summary>
    Play,

    /// <summary>Copied files or an image: into the transit station, as the pill's "+" (TODO ④).</summary>
    Keep,
}

/// <summary>A button: words, or (media) an icon from Segoe Fluent Icons.</summary>
public sealed record SheetButton(SheetAction Action, string? Label, string? Glyph = null);

/// <summary>One sender's messages on a notification card: the name, then what they sent, oldest first (TODO 四, user 10-11).</summary>
public sealed record SheetGroup(string Name, IReadOnlyList<string> Lines);

/// <summary>A row of a file list: where the file is, its name, and whether it is no longer there.</summary>
public sealed record SheetFile(string Path, string Name, bool Gone);

/// <summary>The progress zone: share done, the words left and right above the bar (either may be empty), its colour.</summary>
public sealed record SheetProgress(double Fraction, string? Left, string? Right, SheetTone Tone = SheetTone.Plain)
{
    /// <summary>How far isn't known: a piece flows along the track, as on the pill (TODO ⑤ 掀起卡片适配, 10-11).</summary>
    public bool Flowing { get; init; }
}

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

    /// <summary>A notification card's messages by sender, the one who wrote last first (<see cref="CardSheets.BySender"/>).</summary>
    public IReadOnlyList<SheetGroup> Groups { get; init; } = [];

    /// <summary>The rows of <see cref="SheetLayout.Files"/>.</summary>
    public IReadOnlyList<SheetFile> Files { get; init; } = [];

    /// <summary>What a gone file's row says ("not found").</summary>
    public string? FileGone { get; init; }

    /// <summary>Several rows dragged at once, "{n}" the count: all files / all folders / both ("3个文件 / 3个文件夹 / 3个项目").</summary>
    public (string Files, string Folders, string Items) FilesDragged { get; init; } = ("{n}", "{n}", "{n}");
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

    /// <summary>
    /// Messages by sender (user 10-11): name to its first message 2 (<see cref="TitleBodyGap"/>), 4 between one sender's messages
    /// (tighter than paragraphs: a chat), 12 between senders (<see cref="Gap"/>). Names in the main colour, not bold (at most one
    /// bold thing per zone), messages grey — as the pill writes "name message".
    /// </summary>
    public const double MessageGap = 4;
    public const double Fade = 16;
    public const double NoteSize = 12, NoteLine = 16;
    public const double TextSize = 14, TextLine = 20;
    public const double NumberSize = 40, NumberLine = 48;

    /// <summary>The cover beside a song: 56 when there's a progress zone (the middle is 58 high), 72 without (96).</summary>
    public static double CoverSize(bool progress) => progress ? 56 : 72;

    /// <summary>
    /// A notification card's messages by sender (TODO 四, user 10-11, as phones group a chat app's): each name once, its messages
    /// oldest first as in the chat; the one who wrote last on top. The sender is the notification's title (QQ's: the nickname).
    /// </summary>
    public static IReadOnlyList<SheetGroup> BySender(ActivityState newest)
    {
        ArgumentNullException.ThrowIfNull(newest);
        var groups = new List<(string Name, List<string> Lines)>();
        foreach (var message in newest.Earlier.Append(newest))   // Earlier: as they came, oldest first
        {
            var name = message.Title ?? message.Name ?? string.Empty;
            var at = groups.FindIndex(g => g.Name == name);
            var group = at < 0 ? (name, new List<string>()) : groups[at];
            if (at >= 0)
            {
                groups.RemoveAt(at);
            }

            if (!string.IsNullOrWhiteSpace(message.Msg))
            {
                group.Item2.Add(message.Msg);
            }

            groups.Insert(0, group);   // wrote just now: on top
        }

        return [.. groups.Select(g => new SheetGroup(g.Name, g.Lines))];
    }

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
    /// <param name="browser">The default browser's name, for a copied address's "用 Edge 打开"; null = "browser".</param>
    public static CardSheet Build(Card card, DateTimeOffset now, LanguageStrings text, AgentTail? tail = null, string? browser = null)
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
            // an app's notifications, the ones merged into the card too ("+N" on the pill, user 10-10), by sender (user 10-11)
            ActivityKind.Notice when s.Stage == "expand" => Sheet(
                s.Name ?? text.CardSystem, null, SheetTone.Plain, Ago(Start(s), now, text), null, [],
                s.Open is null ? close : [new(SheetAction.Open, text.MenuOpen), .. close]) with { Groups = BySender(s) },
            ActivityKind.Notice when s.Glyph == NoticeGlyphs.Drive => Drive(card, text),
            ActivityKind.Notice when s.Glyph == NoticeGlyphs.Printer => Printer(s, text, close),
            ActivityKind.Notice when s.Glyph == NoticeGlyphs.Transit => Transit(s, text),
            // the calendar (10-10): the event's name, its state in the card's colour, its time "8:00 – 9:35"
            ActivityKind.Notice when s.Look is not null => Sheet(
                text.CardSystem, s.Title,
                s.State == SystemWatch.SystemNotice.WaitState ? SheetTone.Waiting : s.RunState == ActivityRunState.Done ? SheetTone.Ok : SheetTone.Plain,
                null, s.Msg ?? s.Title, [s.Name], close),
            ActivityKind.Notice when s.Subject is not null || s.Level is not null => SystemCard(s, text, close, browser),
            // "充电中 78%": a translation's " · " is a gap (TODO ⑤ 全岛排版)
            ActivityKind.Notice => Sheet(
                text.CardSystem, null, SheetTone.Plain, Ago(Start(s), now, text), TextGap.FromDots(s.Title), [s.Msg, s.Name], close),
            _ => Sheet(
                text.CardSystem, s.RunState == ActivityRunState.Error ? text.Fail : null, SheetTone.Bad, Ago(Start(s), now, text),
                s.Title is null ? s.Name : TextGap.FromDots(s.Title), [s.Msg], close),
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

    /// <summary>
    /// Every transfer alike (TODO ⑤ 掀起卡片适配, user 10-11): downloads, Explorer's copies, WinRAR / 7-Zip / Bandizip, exports,
    /// Steam, the printer. The source row names the program doing it (its icon beside, from <see cref="ActivityState.Tool"/>), the
    /// state word alone on the right; the file's name, a grey line under it (<see cref="ActivityState.Note"/>: the file being packed,
    /// a game's bytes, a print's pages). With a percentage the bar says "time left · speed … 40%"; without, it flows and says
    /// "how much · speed … time taken". Done: size and time taken, "Show in folder" (Steam: "Play").
    /// </summary>
    private static CardSheet Transfer(Card card, DateTimeOffset now, LanguageStrings text, IReadOnlyList<SheetButton> close)
    {
        var s = card.State;
        var copy = s.Stage == "copy";
        // the state in words ("复制中", "已复制", 10-10)
        var (state, tone) = s.RunState switch
        {
            ActivityRunState.Done => (s.Words ?? text.Done, SheetTone.Ok),
            ActivityRunState.Error => (text.Fail, SheetTone.Bad),
            _ when s.IsPausedTransfer => (text.Paused, SheetTone.Plain),
            _ => (s.Words ?? (copy ? text.CardRunning : text.Downloading), SheetTone.Plain),
        };

        var running = s.RunState == ActivityRunState.Running;
        var several = running && s.Downloads is { Count: > 1 };
        var paused = s.IsPausedTransfer || (s.Words is { } w && w == text.Paused);
        var speed = s.Speed is { } bytes && !paused ? $"{Bytes((long)bytes)}/s" : null;
        var lines = new List<string?>();
        SheetProgress? bar = null;
        if (running && s.Fraction is { } f)
        {
            lines.Add(s.Note);
            bar = new SheetProgress(f, TextGap.Join(s.Eta is { } eta ? Fill(text.CardLeft, Span(eta, text)) : null, speed), $"{f * 100:0}%");
        }
        else if (running)
        {
            lines.Add(s.Note);
            var amount = several ? text.FileItems.Replace("{0}", s.Downloads!.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                : s.Done is { } done ? Fill(copy ? text.CardWritten : text.CardDownloaded, Bytes(done))
                : null;
            bar = new SheetProgress(0, TextGap.Join(amount, speed), Fill(text.CardTook, Span(now - Start(s), text))) { Flowing = !paused };
        }
        else
        {
            lines.Add(TextGap.Join(s.Note, s.Took is { } took ? Fill(text.CardTook, Span(took, text)) : null));
        }

        var name = s.Name ?? (copy ? text.FileTransfer : text.Downloading);
        IReadOnlyList<SheetButton> buttons = card.Type == CardType.Status
            ? copy ? [new(SheetAction.Hide, text.CardHide)] : [new(SheetAction.OpenFolder, text.CardOpenFolder), new(SheetAction.Hide, text.CardHide)]
            : s.RunState == ActivityRunState.Done && SteamGame(card.Key) is not null ? [new(SheetAction.Play, text.CardPlay), .. close]
            : s.Open is { } open ? [new(SheetAction.OpenFolder, s.MoreCount > 0 || !HasExtension(open) ? text.CardOpenFolder : text.CardReveal), .. close]
            : close;
        var sheet = Sheet(s.AppName ?? (copy ? text.FileTransfer : text.CardDownloads), state, tone, null,
            several ? null : s.MoreCount > 0 ? $"{name} +{s.MoreCount}" : name,
            several ? [DownloadList(s.Downloads!, text)] : [.. lines], buttons) with { Progress = bar };
        return several ? sheet with { Markdown = true } : sheet;
    }

    /// <summary>The game of a Steam card ("steam:620" → 620), for its "Play"; null for any other card (and Steam's fallback).</summary>
    public static int? SteamGame(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.StartsWith("steam:", StringComparison.Ordinal)
               && int.TryParse(key.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    private static bool HasExtension(string path) => Path.HasExtension(path);

    /// <summary>
    /// One program's downloads (F2b; user 10-11: a card per program, so no section names): each file — its name when the browser has
    /// given it (Chrome/Edge's "Unconfirmed 123" has none) and how much came, "paused" after it. As Markdown: a list.
    /// </summary>
    private static string DownloadList(IReadOnlyList<DownloadLine> downloads, LanguageStrings text)
    {
        var md = new System.Text.StringBuilder();
        foreach (var d in downloads)
        {
            md.Append("- ").Append(TextGap.Join(d.Name is { } n ? Escape(n) : Fill(text.CardDownloaded, Bytes(d.Size)),
                d.Name is null ? null : Bytes(d.Size), d.Paused ? text.Paused : null)).Append('\n');
        }

        return md.ToString().TrimEnd();
    }

    /// <summary>
    /// A printer that needs someone (TODO 四, 10-11): as its transfer card — the printer's name, the problem orange on the right, the
    /// document and its pages, the bar standing where it stopped.
    /// </summary>
    private static CardSheet Printer(ActivityState s, LanguageStrings text, IReadOnlyList<SheetButton> close)
    {
        SheetProgress? bar = s.Fraction is { } f ? new SheetProgress(f, null, $"{f * 100:0}%", SheetTone.Waiting) : null;
        return Sheet(s.AppName ?? text.CardSystem, s.Title, SheetTone.Waiting, null, s.Msg, [s.Note], close) with { Progress = bar };
    }

    /// <summary>
    /// Windows' own news (TODO ⑤ 掀起卡片适配, user 10-11), laid out as the transfers: what it is about in the source row (电源 / 蓝牙 /
    /// 网络 / 专注 / 剪贴板 / 性能), the state word alone on the right (orange when someone must act: a low battery); a percentage as the
    /// one big number with a grey caption ("78%  约45分钟", "92%  chrome"), else the subject and a grey line; its button opens where
    /// it is dealt with (Windows Settings, Task Manager) — a copied address, files or an image get theirs from the bar.
    /// </summary>
    private static CardSheet SystemCard(ActivityState s, LanguageStrings text, IReadOnlyList<SheetButton> close, string? browser)
    {
        IReadOnlyList<SheetButton> buttons = s.Open switch
        {
            null when s.Glyph is NoticeGlyphs.Files or NoticeGlyphs.Image => [new(SheetAction.Keep, text.CardAddToTransit), .. close],
            null => close,
            _ when s.Open.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) => [new(SheetAction.Open, text.CardOpenSettings), .. close],
            "taskmgr" => [new(SheetAction.Open, text.CardTaskManager), .. close],
            // a copied address (TODO ④): in the default browser, as the pill's ↗
            _ => [new(SheetAction.Open, text.OpenWith.Replace("{a}", browser ?? text.CardBrowser, StringComparison.Ordinal)), .. close],
        };
        var tone = s.RunState == ActivityRunState.Error ? SheetTone.Waiting : SheetTone.Plain;
        if (s.Level is { } level)
        {
            return new CardSheet(
                s.AppName ?? text.CardSystem, s.Words, s.Words is null ? SheetTone.Plain : tone, null, SheetLayout.Number,
                null, [], null, $"{Math.Clamp(level, 0, 100)}%", s.Note, null, buttons, Adaptive: false);
        }

        return Sheet(s.AppName ?? text.CardSystem, s.Words, tone, null, string.IsNullOrEmpty(s.Subject) ? null : s.Subject, [s.Note], buttons);
    }

    /// <summary>"45秒", "2分钟", "1小时5分钟".</summary>
    public static string Span(TimeSpan t, LanguageStrings text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var seconds = (int)Math.Max(0, t.TotalSeconds);
        return seconds < 60 ? Fill(text.CardSeconds, seconds.ToString(CultureInfo.InvariantCulture))
            : seconds < 3600 ? text.Minutes.Replace("{0}", (seconds / 60).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            : text.CardHours.Replace("{a}", (seconds / 3600).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{b}", (seconds / 60 % 60).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
    /// <summary>
    /// The USB card (TODO ④, user 10-10): "KINGSTON (E:)", the space used as a bar, "Open" and "Eject" while it's in. "Eject failed"
    /// (red) says who holds it; "Not safely removed" (orange) says files may be incomplete; "Removed" / "Ejected" are just said.
    /// </summary>
    private static CardSheet Drive(Card card, LanguageStrings text)
    {
        var s = card.State;
        var plugged = card.Type == CardType.Status;
        var plain = plugged && s.RunState != ActivityRunState.Error;
        var bar = plugged && s.Total is > 0 && s.Done is { } used
            ? new SheetProgress((double)used / s.Total.Value, Bytes(used), Bytes(s.Total.Value))
            : null;
        var tone = s.RunState switch
        {
            ActivityRunState.Error => SheetTone.Bad,
            ActivityRunState.Done => SheetTone.Ok,
            _ => s.State == SystemWatch.SystemNotice.WaitState ? SheetTone.Waiting : SheetTone.Plain,
        };
        return Sheet(text.NoticeDrive, plain ? null : s.Title, tone, null, plain ? $"{s.Title} {s.Msg}" : s.Msg, plain ? [] : [s.Name],
                plugged ? [new(SheetAction.Open, text.MenuOpen), new(SheetAction.Eject, text.CardEject)] : [])
            with { Progress = bar };
    }

    /// <summary>
    /// The transit station (TODO ④, user 10-10): every file a row — its name, "not found" in grey when it's gone, ✕ — each dragged
    /// out on its own; "Clear" under them. No state word: the pill already says how many.
    /// </summary>
    private static CardSheet Transit(ActivityState s, LanguageStrings text)
    {
        var gone = new HashSet<string>(s.Gone ?? [], StringComparer.OrdinalIgnoreCase);
        return Sheet(text.NoticeTransit, null, SheetTone.Plain, null, null, [], [new(SheetAction.Clear, text.CardClear)]) with
        {
            Layout = SheetLayout.Files,
            FileGone = text.CardFileGone,
            FilesDragged = (text.NoticeTransitFiles, text.NoticeTransitFolders, text.NoticeTransitItems),
            Files = [.. (s.Files ?? []).Select(p => new SheetFile(p, TransitShelf.NameOf(p), gone.Contains(p)))],
        };
    }

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

    /// <summary>"812 KB", "12.3 MB", "1.20 GB".</summary>
    public static string Bytes(long bytes) => bytes switch
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
