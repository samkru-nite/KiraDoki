using IsleBar.App.Interop;
using IsleBar.App.Media;
using IsleBar.Core.Configuration;
using IsleBar.Core.Island;
using IsleBar.Core.Localization;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace IsleBar.App.IslandUi;

/// <summary>What a pill needs from the window that owns it (settings, colours, the agents' logos, covers, …).</summary>
internal interface IPillHost
{
    IsleBarSettings Settings { get; }
    ThemeColors Theme { get; }
    LanguageStrings Text { get; }
    string Language { get; }

    /// <summary>A swap or lift is flying: a new long title rests with "…" until it has landed (it is being snapshotted).</summary>
    bool Flying { get; }

    ImageSource? ClaudeLogo { get; }
    ImageSource? CodexLogo { get; }

    /// <summary>The loaded cover of a music card (the pill's 24 one), or null.</summary>
    (BitmapImage Image, Windows.UI.Color? Tint)? Cover(Card? card);

    /// <summary>A timer's stopwatch with its turning hand.</summary>
    UIElement Dial(TimerDial dial, Windows.UI.Color color, bool paused);

    /// <summary>What an agent at work last said in this turn (its session record), or null.</summary>
    string? AgentSaid(ActivityState state);

    /// <summary>A finished agent's last answer (its session record), or null.</summary>
    string? AgentAnswer(ActivityState state);

    SolidColorBrush Brush(Windows.UI.Color color);

    AudioMeter Meter { get; }
}

/// <summary>
/// One big pill drawing one card (TODO 7.9 F3, plan B, 10-09): the drawing that lived in MainWindow, moved here unchanged so the
/// list can draw each card with a real pill (step 2) instead of snapshots of the taskbar's. Everything that moves on its own
/// (scrolling title, rolling digits, the bars, the flowing piece) is this pill's own.
/// </summary>
public sealed partial class PillView : UserControl
{
    /// <summary>Big pill 220x32 (TODO 7.9 D final sizes).</summary>
    internal const double BaseWidth = 220;
    internal const double BaseHeight = 32;

    /// <summary>Left edge of the card icon and of the card text on a plain card (TODO 7.9 D).</summary>
    internal const double IconStart = 8;
    internal const double TextStart = 32;

    /// <summary>Text from x 32 to 208 on a plain card (TODO 7.9 D).</summary>
    private const double CardTextWidth = 176;

    /// <summary>The progress track: 16 in from each end of the big pill, 188 long (TODO 7.9 D).</summary>
    internal const double ProgressInset = 16;
    internal const double ProgressLength = BaseWidth - (2 * ProgressInset);

    public PillView()
    {
        InitializeComponent();
        // the title's scroll moves by "Translation", which must be switched on before anything stops or sets it: since the lap holds
        // first (10-08), a card swap during that hold stopped it while still off → "parameter error", every swap a hard cut (user 10-08)
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(MarqueePanel, true);
    }

    internal IPillHost Host { get; set; } = null!;

    private SolidColorBrush BrushOf(Windows.UI.Color color) => Host.Brush(color);

    private static PillColours? _colours;

    /// <summary>
    /// The pills' colours, worked out from the taskbar beside them (<see cref="PillPalette"/>, TODO 丙④). Until the taskbar is
    /// read: the theme's usual taskbar.
    /// </summary>
    internal static PillColours Colours => _colours ??= PillPalette.For(ThemeColors.FromSystem().Light
        ? new Rgb(0xDF, 0xE6, 0xF3)
        : new Rgb(0x20, 0x20, 0x20));

    /// <summary>The taskbar beside the pills is <paramref name="taskbar"/>: works their colours out again. False when unchanged.</summary>
    internal static bool UseTaskbar(Windows.UI.Color taskbar)
    {
        var next = PillPalette.For(new Rgb(taskbar.R, taskbar.G, taskbar.B));
        if (_colours == next)
        {
            return false;
        }

        _colours = next;
        return true;
    }

    internal static Windows.UI.Color ColourOf(Rgb c) => Windows.UI.Color.FromArgb(255, c.R, c.G, c.B);

    /// <summary>The pill's colour at rest.</summary>
    internal static Windows.UI.Color RestFill() => ColourOf(Colours.Rest);

    /// <summary>The pills' edge line (solid).</summary>
    internal static Windows.UI.Color EdgeFill() => ColourOf(Colours.Edge);

    // ---------------- One card ----------------

    /// <summary>
    /// Draws <paramref name="state"/> (the card <paramref name="card"/>'s, or the drop hint's) — text, icon, cover, bars. The caller
    /// sets the lead icon's mode first (MainWindow.UpdateLeadIcon). <paramref name="plainIcon"/>: the drop hint (no card colours).
    /// </summary>
    internal void Render(ActivityState state, Card? card, bool plainIcon)
    {
        _card = card;
        IslandContent.Visibility = Visibility.Visible;

        LeadIcon.Glyph = state.IsPaused ? "" : GlyphFor(state);   // paused timer = pause icon
        LeadIcon.Foreground = BrushOf(Host.Theme.Icon);   // only small icons are orange / green / red (user 10-10): the border tells it here
        ShowLeadDial(plainIcon ? null : state, Host.Theme.Icon);
        // Gaps like every other card (user 10-08): 8 between the icon and the text, 8 between the text and the now-playing bars.
        // The cover is 22, 3 past the 16 icon slot on each side, so with a cover the text starts 3 later (it sat 5 from the cover);
        // the bars get 8 too (was 5, to match that 5). Else 12 to the pill's edge (TODO 7.9 D).
        var playing = state.Kind == ActivityKind.Music && state.Stage != "paused";
        var hasCover = state.Kind == ActivityKind.Music && Host.Cover(card) is not null;
        LeadSlot.Margin = new Thickness(8, 0, hasCover ? 11 : 8, 0);
        // one app's notifications merged: "+N" on the right, grey (user 10-10)
        var more = state.Kind == ActivityKind.Notice ? state.Earlier.Count : 0;
        MoreLabel.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoreLabel.Text = more > 0 ? "+" + more.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        MoreLabel.Foreground = BrushOf(Host.Theme.Hint);
        _inform = card is { Type: CardType.Inform };
        _tailGap = playing || more > 0;
        PlaceRight();
        // compressing: the document outline gets zipper teeth (the drop hint uses the same outline without them)
        ZipTeeth.Opacity = state.Stage == "copy" && state.RunState == ActivityRunState.Running
                           && state.Glyph == FileOperationWatcher.CompressGlyph ? 1 : 0;
        ShowAgentMark(state);
        ShowAlbumArt(state.Kind == ActivityKind.Music);
        ShowAppMark(state);
        ShowLeadPause(state.Kind == ActivityKind.Music && state.Stage == "paused");
        SetEqualizer(state.Kind == ActivityKind.Music && state.Stage != "paused");
        var now = DateTimeOffset.UtcNow;
        // An agent card shows just the session's name (user 10-10). Without a name, "Claude done"
        var name = state.Name;
        if (state.Kind == ActivityKind.Timer)
        {
            ShowTimerText(state, now);
        }
        else if (state is { Kind: ActivityKind.Notice, Stage: "interrupted", Agent: not null } && !string.IsNullOrEmpty(name))
        {
            ShowIslandText(state, string.Empty, name);   // interrupted: just the session's name, like the agent's other stages (user 10-10)
        }
        else if (state is { Kind: ActivityKind.Notice, Look: not null })
        {
            // the calendar (user 10-10): "Ended Maths" — the words first, the event's name grey after a gap (its time is on the
            // lifted card only)
            ShowIslandText(state, Head(state.Title, state.Msg), state.Msg ?? string.Empty, More(state));   // the transit station's "+N" (10-11)
        }
        else if (state is { Kind: ActivityKind.Transfer, Words: { } words })
        {
            // Explorer's copies (10-10): "复制中 DSC02580.ARW +271" — the state first, the rest grey after a gap
            ShowIslandText(state, Head(words, name), name ?? string.Empty, More(state));
        }
        else if (state.Kind == ActivityKind.Notice)
        {
            // brief notice: "充电中 78%" — the title is the key part (bold), the rest dimmed after a gap; a translation's " · " is a
            // break too ("Charging · {p}%")
            // a notification of an app's: "张伟 在吗" — its icon names the app (user 10-11: "… QQ" went)
            var tail = state.Stage == "expand" ? state.Msg ?? string.Empty : TextGap.Join(state.Msg, state.Name);
            ShowIslandText(state, Head(TextGap.FromDots(state.Title), tail), tail);
        }
        else if (state.Kind == ActivityKind.Music)
        {
            // music: title is the key part, artist dimmed after a gap
            ShowIslandText(state, state.Msg is null ? string.Empty : Head(name, state.Msg), state.Msg ?? name ?? string.Empty);
        }
        else if (state.Kind == ActivityKind.AgentWorking)
        {
            // at work: what it last said in this turn, else the session's name, else "Claude working" (user 10-10)
            var line = OneLine(Host.AgentSaid(state)) ?? name;
            _wordsState = state;
            ShowIslandText(state, line is null ? PrefixFor(state, now) : string.Empty, line is null ? string.Empty : Steady(card, line, now));
        }
        else if (state.Kind == ActivityKind.AgentDone && FirstSentence(Host.AgentAnswer(state)) is { } first)
        {
            ShowIslandText(state, string.Empty, first);   // done: its answer's first sentence — usually the outcome (user 10-10)
        }
        else
        {
            // a download whose name the browser hasn't given yet ("Unconfirmed 123"), or Explorer's progress window (it names no file)
            name ??= state.Kind != ActivityKind.Transfer ? null : state.Stage == "copy" ? Host.Text.FileTransfer : Host.Text.Downloading;
            ShowIslandText(state, PrefixFor(state, now), name ?? string.Empty, More(state));
        }
        IslandText.Foreground = BrushOf(ForegroundFor(state));
    }

    // ---------------- An agent at work: its words ----------------

    /// <summary>Each sentence stays at least this long; what comes meanwhile waits, and only the newest is shown (user 10-10).</summary>
    private static readonly TimeSpan WordsAtLeast = TimeSpan.FromSeconds(3);

    /// <summary>Half of the change: the old words fade out in this, the new ones fade in in this (user 10-10: ~0.2 s in all).</summary>
    private static readonly TimeSpan WordsFade = TimeSpan.FromMilliseconds(100);

    private string? _wordsKey;
    private string _words = string.Empty;
    private DateTimeOffset _wordsSince;
    private bool _wordsFading;
    private ActivityState? _wordsState;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _wordsTimer;
    private DateTimeOffset _wordsFadeAt;

    /// <summary>
    /// The words to show for <paramref name="wanted"/>: a new card shows them at once; on the same card a change waits until the
    /// shown words had <see cref="WordsAtLeast"/>, then the old fade out and the new fade in (<see cref="ChangeWords"/>).
    /// </summary>
    private string Steady(Card? card, string wanted, DateTimeOffset now)
    {
        if (_wordsFading && now - _wordsFadeAt > TimeSpan.FromSeconds(1))
        {
            // a change that never finished (its fade-in never came): show the words again rather than an empty pill
            TaskbarHost.Log("pill: words change stuck — shown again");
            _wordsTimer?.Stop();
            _wordsTimer = null;
            _wordsFading = false;
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MarqueeHost).Opacity = 1;
        }

        if (card?.Key != _wordsKey)
        {
            _wordsKey = card?.Key;
            _words = wanted;
            _wordsSince = now;
            return wanted;
        }

        if (wanted != _words && !_wordsFading && now - _wordsSince >= WordsAtLeast)
        {
            ChangeWords(wanted);
        }

        return _words;
    }

    private void ChangeWords(string next)
    {
        if (!Animations.On || _frozen || Host.Flying)
        {
            _words = next;
            _wordsSince = DateTimeOffset.UtcNow;
            return;
        }

        _wordsFading = true;
        _wordsFadeAt = DateTimeOffset.UtcNow;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MarqueeHost);
        FadeTo(visual, 0);
        // held in a field: a timer only a local held was collected before it fired — the words faded out and never came back (10-10)
        _wordsTimer?.Stop();
        var swap = _wordsTimer = DispatcherQueue.CreateTimer();
        swap.Interval = WordsFade;
        swap.IsRepeating = false;
        swap.Tick += (timer, _) =>
        {
            timer.Stop();
            _wordsTimer = null;
            _wordsFading = false;
            TaskbarHost.Log($"pill: words changed ({next.Length} chars)");
            _words = next;
            _wordsSince = DateTimeOffset.UtcNow;
            if (_card?.Key == _wordsKey && _wordsState is { } state)
            {
                ShowIslandText(state, string.Empty, next);
            }

            FadeTo(visual, 1);
        };
        swap.Start();
    }

    private static void FadeTo(Microsoft.UI.Composition.Visual visual, float to)
    {
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, to);
        fade.Duration = WordsFade;
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>
    /// The first sentence of an answer (user 10-10: the outcome, where answers put it): the first line with words in it — headings,
    /// list marks, quotes, tables and code fences skipped — up to 。！？!? or a full stop before a space. Bold and code marks out,
    /// a closing 。 dropped. Null when there is none.
    /// </summary>
    private static string? FirstSentence(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        foreach (var raw in answer.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith('|'))
            {
                continue;
            }

            line = line.TrimStart('#', '>', '-', '*', '+', ' ').Trim();
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^\d+[.)、]\s*", string.Empty);   // "1. " "2) " "3、"
            if (OneLine(line) is not { } words)
            {
                continue;
            }

            for (var i = 0; i < words.Length; i++)
            {
                var c = words[i];
                if (c is '。' or '！' or '？' or '!' or '?' || (c == '.' && (i + 1 == words.Length || words[i + 1] == ' ')))
                {
                    words = words[..(c is '。' or '.' ? i : i + 1)];
                    break;
                }
            }

            return words.Length == 0 ? null : words;
        }

        return null;
    }

    /// <summary>A said text on one line: Markdown marks and line breaks out, spaces squeezed. Null when nothing is left.</summary>
    private static string? OneLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var plain = text.Replace("**", string.Empty, StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal);
        var line = string.Join(' ', plain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(w => w is not ("#" or "##" or "###" or "-" or "*" or ">")));
        return line.Length == 0 ? null : line;
    }

    /// <summary>No card (nothing to show): the text, cover, bars and dial go; the icon back at its place.</summary>
    internal void Clear()
    {
        SetEqualizer(false);
        ShowAlbumArt(false);
        ShowLeadPause(false);
        ShowLeadDial(null, Host.Theme.Icon);
        StopMarquee();
        IslandContent.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// A pill in the list: the list draws the shape (it grows out of the circle) and the border, so this one's own surface, glass, veil and
    /// border don't show — the surface keeps its colour for the scrolling title's end fades. The close button as at rest.
    /// </summary>
    internal void UseAsListed(Windows.UI.Color fill, Windows.UI.Color chip, double chipRadius, string closeText, Windows.UI.Color closeInk)
    {
        PillSurface.Fill = BrushOf(fill);
        PillSurface.Opacity = 0;
        DimVeil.Visibility = Visibility.Collapsed;
        GlowRing.Visibility = Visibility.Collapsed;
        CloseChip.Fill = BrushOf(chip);
        CloseChip.Radius = chipRadius;
        CloseLabel.Text = closeText;
        CloseLabel.Foreground = BrushOf(closeInk);
        OpenIcon.Foreground = CloseLabel.Foreground;
    }

    /// <summary>A list pill's button under the pointer: its chip lights up as the big pill's does (user 10-10).</summary>
    internal void LightButton(bool on) => CloseChip.Fill = BrushOf(ColourOf(on ? Colours.ChipHover : Colours.Chip));

    /// <summary>The icon slot as a card has it: the font icon, the logos hidden until the card asks.</summary>
    internal void ShowCardLead()
    {
        ClaudeGlyphImage.Source = Host.ClaudeLogo;
        CodexGlyph.Source = Host.CodexLogo;
        ClaudeGlyphImage.Visibility = Visibility.Collapsed;
        CodexGlyph.Visibility = Visibility.Collapsed;
        LeadIcon.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Holds still (dimmed under the list or a lifted card, user 10-10): a long title rests with "…", the music bars stop, the
    /// flowing piece and a timer's hand stand. Words and numbers still change. Off again: they move from the next draw on.
    /// </summary>
    internal bool Frozen
    {
        get => _frozen;
        set
        {
            if (_frozen == value)
            {
                return;
            }

            _frozen = value;
            _leadDialKey = null;   // the dial again, turning or standing
            if (value)
            {
                RestMarquee();
                StopMeterBars();
                StopLoopBars();
                StopFlow();
            }
        }
    }

    private bool _frozen;

    /// <summary>The card this pill last drew.</summary>
    private Card? _card;

    // ---------------- Scrolling title · now-playing bars ----------------

    /// <summary>Scroll speed (DIP/s) and the pause at each lap — relaxed, like music apps.</summary>
    private const double MarqueeSpeed = 30;
    private static readonly TimeSpan MarqueeHold = TimeSpan.FromSeconds(2);

    /// <summary>Gap between the two title copies (must equal IslandTextCopy's left margin in XAML so the seam is invisible).</summary>
    private const double MarqueeGap = 40;

    private string? _marqueeText;
    private ActivityKind _marqueeKind;
    private bool _equalizerOn;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _meterTimer;
    private readonly LevelBars _levelBars = new();

    private void StopLoopBars()
    {
        _equalizerOn = false;
        foreach (var bar in new[] { Eq0, Eq1, Eq2, Eq3 })
        {
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(bar).StopAnimation("Scale");
        }
    }

    /// <summary>
    /// Bars follow the sound (setting "music bars follow the sound"): 30 times a second the four band levels from the
    /// loopback capture become heights via Core's LevelBars (fast rise, slow fall). Only runs while the music bars show.
    /// </summary>
    private void StartMeterBars()
    {
        _meterTimer = DispatcherQueue.CreateTimer();
        _meterTimer.Interval = TimeSpan.FromMilliseconds(33);
        _meterTimer.Tick += (_, _) =>
        {
            var heights = _levelBars.Update(Host.Meter.Levels());
            var bars = new[] { Eq0, Eq1, Eq2, Eq3 };
            for (var i = 0; i < bars.Length; i++)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(bars[i]);
                visual.CenterPoint = new System.Numerics.Vector3((float)bars[i].Width / 2, (float)bars[i].Height / 2, 0f);
                visual.Scale = new System.Numerics.Vector3(1f, heights[i], 1f);
            }
        };
        _meterTimer.Start();
    }

    internal void StopMeterBars()
    {
        _meterTimer?.Stop();
        _meterTimer = null;
    }

    private string? _equalizerTint = "none";

    /// <summary>
    /// Island text. <b>If a music title is wider than its slot, scroll it left</b> (user feedback 09-30: should loop seamlessly like Galaxy phones).
    /// Two copies of the title sit side by side and scroll endlessly by "one copy width + gap" — the second copy lands exactly where the first was,
    /// so jumping back to the start leaves no visible seam. Each lap pauses 2s when the first character is at the front.
    /// The animation runs on the compositor (Composition) — a Storyboard appeared to stop after one lap (user feedback 09-30).
    /// Everything else is truncated with "…". If the text is unchanged it doesn't restart (this is called every 250ms).
    /// </summary>
    /// <param name="suffix">Kept whole after the name, which is shortened to make room (e.g. "  +2" more downloads).</param>
    private void ShowIslandText(ActivityState state, string prefix, string name, string suffix = "")
    {
        ShowClockDigits(null);   // the timer digit slot is only for timers
        var width = AvailableTextWidth();
        ClipMarquee(width);
        var full = prefix + name;
        // Music is just playing in the background, so its title uses normal weight — only alerts (done, permission) are bold. Long all-caps titles in bold filled the slot
        // and grabbed attention (user feedback 09-30: "STUPID IN LOVE" was too loud, while the "Claude done" text weight was just right)
        _islandStrong = state.Kind != ActivityKind.Music;
        UseFontsFor(full);   // before measuring — changing the font changes the width
        var measure = IslandMeasure();

        // Long names scroll through once to show the whole thing: music titles, session names in Claude alerts.
        // (Transfers are truncated with "…" since the changing percentage would restart the scroll every time)
        var flows = state.Kind is ActivityKind.Music or ActivityKind.AgentDone or ActivityKind.AgentPermission or ActivityKind.AgentWorking or ActivityKind.Notice;
        if (flows && measure.Measure(full) > width)
        {
            _marqueePrefix = prefix;
            _marqueeName = name;
            _marqueeKind = state.Kind;
            if (_marqueeText != full)
            {
                _marqueeText = full;
                // an agent at work doesn't scroll on its own (user 10-10): its words change every few seconds, a lap would always be
                // running in the corner of the eye and be out of date by its end — "…", a hover scrolls one lap
                var still = state.Kind == ActivityKind.AgentWorking;
                if (Host.Flying)
                {
                    // a snapshot is about to be taken: it showed both copies unclipped, over the equalizer and past the pill
                    // (user 10-07) — rest with "…" now, the lap starts once the card has landed
                    LapAfterFlight = !still;
                    ShowMarqueeRest();
                }
                else if (_frozen || still)
                {
                    ShowMarqueeRest();   // holding still: "…", the lap waits for a hover
                }
                else
                {
                    StartMarquee();   // new title: one lap
                }
            }
            else if (!_marqueeRunning)
            {
                ShowMarqueeRest();   // when paused, show "…" (redo every time since color/width may have changed)
            }
        }
        else
        {
            StopMarquee();
            var (head, rest) = TextFit.FitParts(prefix, name, width - (suffix.Length > 0 ? measure.Measure(suffix) : 0), measure);
            SetStyledText(IslandText, head, rest + suffix);
        }

        IslandTextCopy.Foreground = IslandText.Foreground;

        PlaceText();
    }

    /// <summary>
    /// Up and down: the 20 line box centred in the 32 pill (6 above), moved by <see cref="InkOffset"/> so the glyphs themselves sit
    /// in the middle — centring the line box left the text looking low (user 10-06, TODO 7.9 D).
    /// <para>Left and right (user 10-07, fixed 10-10 — the "left" and "icon and text together" choices went): the icon stays at 8.
    /// Nothing on the right: the text's middle on the pill's (110) — centring inside the text area alone (first try) put the text 10
    /// right of it and left the icon stranded. Something on both sides (an inform card's button, a song's bars): the text's middle
    /// halfway between them — the text area's middle, 8 to each (user 10-10, measured: "middle of what the button leaves" ignored the icon and
    /// left the text 26 nearer the icon). Text too long to fit stays left-aligned and scrolls.</para>
    /// </summary>
    internal void PlaceText()
    {
        Canvas.SetTop(MarqueePanel, ((BaseHeight - IslandText.LineHeight) / 2) + InkOffset);
        double text = 0;
        if (_marqueeText is null)
        {
            MarqueePanel.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = MarqueePanel.DesiredSize.Width;
            var area = AvailableTextWidth();
            // where the text area really starts: 32, or 35 after a cover (it was taken as 32 — short song titles sat 3 right, 10-10)
            var areaLeft = LeadSlot.Margin.Left + LeadSlot.Width + LeadSlot.Margin.Right;
            // something on the right too (the button, the now-playing bars): the text area's middle — 8 to each side, so halfway
            // between them (108.5 beside the ↗, 86.5 beside "Close", 111 between cover and bars); else the pill's middle
            // (from the known sizes, not the area's ActualWidth — that lags a layout pass behind a change)
            var more = MoreLabel.Visibility == Visibility.Visible ? MoreWidth() + 8 : 0;   // "+N" before the button too (10-11)
            double? areaRight = CloseZone.Visibility == Visibility.Visible ? BaseWidth - CloseZone.Width - more
                : Equalizer.Visibility == Visibility.Visible ? BaseWidth - Equalizer.Margin.Right - EqualizerWidth - 8
                : MoreLabel.Visibility == Visibility.Visible ? BaseWidth - MoreLabel.Margin.Right - MoreWidth() - 8
                : null;
            var middle = areaRight is { } right ? (areaLeft + right) / 2 : BaseWidth / 2;
            var start = middle - (width / 2);
            text = Math.Clamp(Math.Round((start - areaLeft) * 2) / 2, 0, Math.Max(0, area - width));   // half DIPs; never past the text area
        }

        Canvas.SetLeft(MarqueePanel, text);
    }

    /// <summary>The now-playing bars: four 3 wide, 2 apart (PillView.xaml).</summary>
    private const double EqualizerWidth = (4 * 3) + (3 * 2);

    /// <summary>The "+N" label's width as it reads now (measured: its text just changed, the layout may not have run yet).</summary>
    private double MoreWidth()
    {
        MoreLabel.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return MoreLabel.DesiredSize.Width;
    }

    /// <summary>How far to move the text so its glyphs are centred (DIP, measured by MainWindow.MeasureInkAsync; the same for every pill).</summary>
    internal static double InkOffset { get; set; }

    private string _marqueePrefix = string.Empty;
    private string _marqueeName = string.Empty;
    private bool _marqueeRunning;

    /// <summary>On hover, scroll the title one more lap (if it isn't already scrolling and is long enough to scroll).</summary>
    internal void ReplayMarquee()
    {
        if (_marqueeText is not null && !_marqueeRunning && !_frozen)
        {
            StartMarquee();
        }
    }

    /// <summary>
    /// Island text is painted two ways: the key part (e.g. "42%", "Claude done", "已复制", track title) bold, the rest (e.g. "6s",
    /// the session's name, the file's name) dimmed, <see cref="TextGap.Width"/> between parts — no " · " (TODO ⑤ 全岛排版, user
    /// 10-11). The prefix's first part is the key part; without a prefix (agent names, a done card's sentence) the whole rest is.
    /// </summary>
    private void SetStyledText(TextBlock block, string prefix, string rest)
    {
        // "45%␟6s␟" + name → bold "45%" / dimmed "6s", "name";  "" + name → bold "name"
        var head = prefix.Trim().Trim(TextGap.Mark).Trim();
        var whole = head.Length == 0;
        var text = whole ? rest.Trim() : TextGap.Join([.. TextGap.Parts(head), .. TextGap.Parts(rest)]);
        block.Inlines.Clear();
        GapRuns.Add(block.Inlines, text, block.FontSize, (i, words) => whole || i == 0
            ? _islandStrong
                ? new Microsoft.UI.Xaml.Documents.Run { Text = words, FontFamily = _islandBold, FontWeight = Microsoft.UI.Text.FontWeights.Medium }
                : new Microsoft.UI.Xaml.Documents.Run { Text = words }
            : new Microsoft.UI.Xaml.Documents.Run { Text = words, Foreground = BrushOf(Host.Theme.Hint) });
    }

    /// <summary>The key part, followed by a break when something comes after it.</summary>
    private static string Head(string? key, string? after)
        => string.IsNullOrWhiteSpace(after) ? key ?? string.Empty : (key ?? string.Empty) + TextGap.Break;

    /// <summary>"+3" more downloads / files after the name, after a gap; empty when there are none.</summary>
    private static string More(ActivityState state)
        => state.MoreCount > 0 ? TextGap.Break + "+" + state.MoreCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>
    /// Resting look: end with "…" so text isn't chopped off abruptly at the edge of the slot (user feedback 09-30: suggested using an ellipsis).
    /// </summary>
    private void ShowMarqueeRest()
    {
        var measure = IslandMeasure();
        var (head, rest) = TextFit.FitParts(_marqueePrefix, _marqueeName, AvailableTextWidth(), measure);
        SetStyledText(IslandText, head, rest);
        IslandTextCopy.Visibility = Visibility.Collapsed;
        PlaceText();
    }

    /// <summary>
    /// Timer: "Timer 18:42" — label bold, time normal weight (like "Pomodoro 18:42" in the mockup). A named timer uses its name.
    /// When finished: "Timer done".
    /// </summary>
    private void ShowTimerText(ActivityState state, DateTimeOffset now)
    {
        StopMarquee();
        ClipMarquee(AvailableTextWidth());
        var label = string.IsNullOrWhiteSpace(state.Name) ? Host.Text.Timer : state.Name!;
        UseFontsFor(label);
        var left = TimerParser.Elapsed(state, now) ?? TimerRemaining(state, now);   // a stopwatch counts up
        var running = TimerParser.IsStopwatch(state) || (left is { } l && l > TimeSpan.Zero);
        IslandText.Inlines.Clear();
        // "计时器 完成": the label bold, a gap, the word
        GapRuns.Add(IslandText.Inlines, running ? label : TextGap.Join(label, Host.Text.Done), IslandText.FontSize, (i, words) => i == 0
            ? new Microsoft.UI.Xaml.Documents.Run { Text = words, FontFamily = _islandBold, FontWeight = Microsoft.UI.Text.FontWeights.Medium }
            : new Microsoft.UI.Xaml.Documents.Run { Text = words });
        ShowClockDigits(running ? FormatClock(left!.Value) : null);
        IslandText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        PlaceText();
    }

    private string _clockShown = string.Empty;

    /// <summary>
    /// Draws the timer digits one by one. <b>Each digit slot is fixed to the widest digit</b> — drawing them as one string
    /// made the text jiggle sideways when narrow digits like "1" appeared. Only changed digits are set again. null hides them.
    /// </summary>
    private void ShowClockDigits(string? clock)
    {
        if (clock is null)
        {
            ClockDigits.Visibility = Visibility.Collapsed;
            _clockShown = string.Empty;
            return;
        }

        ClockDigits.Visibility = Visibility.Visible;
        if (clock == _clockShown && ClockDigits.Children.Count == clock.Length)
        {
            return;
        }

        var rebuild = ClockDigits.Children.Count != clock.Length;
        if (rebuild)
        {
            ClockDigits.Children.Clear();
            var digitWidth = DigitWidth();
            foreach (var c in clock)
            {
                var cell = new TextBlock
                {
                    FontSize = IslandText.FontSize,
                    FontFamily = IslandText.FontFamily,
                    CharacterSpacing = IslandText.CharacterSpacing,
                    LineHeight = IslandText.LineHeight,
                    LineStackingStrategy = IslandText.LineStackingStrategy,
                    Foreground = IslandText.Foreground,
                    TextAlignment = TextAlignment.Center,
                };
                if (char.IsDigit(c))
                {
                    cell.Width = digitWidth;
                }

                ClockDigits.Children.Add(cell);
            }
        }

        for (var i = 0; i < clock.Length; i++)
        {
            var cell = (TextBlock)ClockDigits.Children[i];
            var text = clock[i].ToString();
            cell.Foreground = IslandText.Foreground;
            if (cell.Text == text)
            {
                continue;
            }

            cell.Text = text;   // a hard cut (user 10-10, TODO 4.7 / F5: the roll down, then a fade, were too busy every second)
        }

        _clockShown = clock;
    }

    /// <summary>Widest digit width (current font and size).</summary>
    private double DigitWidth()
    {
        var probe = new TextBlock { FontSize = IslandText.FontSize, FontFamily = IslandText.FontFamily, CharacterSpacing = IslandText.CharacterSpacing };
        double widest = 0;
        for (var d = '0'; d <= '9'; d++)
        {
            probe.Text = d.ToString();
            probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, probe.DesiredSize.Width);
        }

        return Math.Ceiling(widest);
    }

    /// <summary>
    /// One lap. It first holds with the resting look ("…", no fades), the same as when it stands still — and as the swap's snapshot
    /// shows it, so nothing changes as a card lands (it switched from "…" to the lap's look right then: the "…", the fade flashed,
    /// user 10-08). The whole title and the fades come in as it starts to move.
    /// </summary>
    private void StartMarquee()
    {
        var myRun = ++_marqueeRun;
        _marqueeRunning = true;
        SetMarqueeFade(false);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MarqueePanel);
        visual.StopAnimation("Translation.X");
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
        ShowMarqueeRest();
        _marqueeHoldTimer?.Stop();
        var hold = _marqueeHoldTimer = DispatcherQueue.CreateTimer();   // held in a field, like the words' timer (10-10)
        hold.Interval = _marqueeKind == ActivityKind.Notice ? TimeSpan.FromSeconds(1) : MarqueeHold;   // short notices can't wait 2 s
        hold.IsRepeating = false;
        hold.Tick += (_, _) =>
        {
            if (myRun != _marqueeRun || !_marqueeRunning || _marqueeText is null)
            {
                return;   // stopped, rested for a flight, or another lap took over
            }

            SetStyledText(IslandText, _marqueePrefix, _marqueeName);
            SetStyledText(IslandTextCopy, _marqueePrefix, _marqueeName);
            IslandTextCopy.Visibility = Visibility.Visible;
            IslandText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            PlaceText();
            RunMarquee(myRun, IslandText.DesiredSize.Width + MarqueeGap);
        };
        hold.Start();
    }

    /// <summary>The ends' fades are the pill colour: repaint them after it changed (hover, theme).</summary>
    internal void RefreshFades() => SetMarqueeFade(_marqueeRunning);

    /// <summary>
    /// While scrolling, softly cover both ends of the text slot with the pill background (so text isn't chopped at the edge, like music apps).
    /// Off when resting, since it ends in "…" then.
    /// </summary>
    private void SetMarqueeFade(bool on)
    {
        FadeLeft.Visibility = FadeRight.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on || PillSurface.Fill is not SolidColorBrush fill)
        {
            return;
        }

        var solid = fill.Color;
        var clear = Windows.UI.Color.FromArgb(0, solid.R, solid.G, solid.B);
        FadeLeft.Fill = Gradient(solid, clear);
        FadeRight.Fill = Gradient(clear, solid);
    }

    private static LinearGradientBrush Gradient(Windows.UI.Color from, Windows.UI.Color to)
    {
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0.5), EndPoint = new Windows.Foundation.Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop { Color = from, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = to, Offset = 1 });
        return brush;
    }

    private int _marqueeRun;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _marqueeHoldTimer;

    private void RunMarquee(int myRun, double distance)
    {
        // Each lap has a number — stopping an earlier lap (e.g. a scrolling music title) delivered its completion signal late
        // and stopped the new lap (a done alert) instead (measured on PC 09-30). Only react to completion of our own number.
        SetMarqueeFade(true);
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(MarqueePanel, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MarqueePanel);
        var compositor = visual.Compositor;
        visual.StopAnimation("Translation.X");
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);

        // The scroll (the pause before it is StartMarquee's) → ends when the next copy's first character reaches the front.
        // The end and start positions look identical, so the jump back is invisible
        // (user feedback 09-30: Galaxy scrolls and stops when the first character comes back to the front).
        var total = TimeSpan.FromSeconds(distance / MarqueeSpeed);
        var slide = compositor.CreateScalarKeyFrameAnimation();
        slide.InsertKeyFrame(0f, 0f);
        slide.InsertKeyFrame(1f, (float)-distance, compositor.CreateLinearEasingFunction());
        slide.Duration = total;
        // Only one lap — scroll once when a track first appears, then stay still once the first character is back at the front (Galaxy style,
        // user feedback 09-30: asked why it kept moving). Hovering plays one more lap (ReplayMarquee).
        slide.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Count;
        slide.IterationCount = 1;
        // Left fade only while the text moves — leaving it on while still (first 2s and at the end) covered the first character
        // and made it look clipped (user feedback 09-30: the first character was still cut off when a title first appeared).
        var fadeVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(FadeLeft);
        var edge = (float)Math.Min(0.4 / total.TotalSeconds, 0.05);
        var fadeIn = compositor.CreateScalarKeyFrameAnimation();
        fadeIn.InsertKeyFrame(0f, 0f);
        fadeIn.InsertKeyFrame(Math.Min(edge, 0.5f), 1f);
        fadeIn.InsertKeyFrame(1f - edge, 1f);
        fadeIn.InsertKeyFrame(1f, 0f);
        fadeIn.Duration = total;
        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        visual.StartAnimation("Translation.X", slide);
        fadeVisual.StartAnimation("Opacity", fadeIn);
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (myRun != _marqueeRun || !_marqueeRunning || _marqueeText is null)
            {
                return;
            }

            _marqueeRunning = false;
            SetMarqueeFade(false);
            visual.StopAnimation("Translation.X");
            visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
            ShowMarqueeRest();   // lap finished → "…"
        });
    }

    /// <summary>A new title's lap was held back while a transition flew; it runs once the card has landed.</summary>
    internal bool LapAfterFlight { get; set; }

    /// <summary>
    /// Stops a lap mid-scroll and shows the "…" look, keeping the title (a snapshot doesn't see the scroll offset nor the clip:
    /// it showed the whole unscrolled title running past the slot — user 10-07).
    /// </summary>
    internal void RestMarquee()
    {
        if (!_marqueeRunning)
        {
            return;
        }

        _marqueeRun++;   // the lap's own end mustn't act later
        _marqueeRunning = false;
        SetMarqueeFade(false);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MarqueePanel);
        visual.StopAnimation("Translation.X");
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
        ShowMarqueeRest();
    }

    private void StopMarquee()
    {
        ShowClockDigits(null);
        if (_marqueeText is null)
        {
            return;
        }

        _marqueeText = null;
        _marqueeRunning = false;
        SetMarqueeFade(false);
        IslandTextCopy.Visibility = Visibility.Collapsed;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MarqueePanel);
        visual.StopAnimation("Translation.X");
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
    }

    /// <summary>Album art on the big pill (22) when the shown card is music and its cover is loaded.</summary>
    private void ShowAlbumArt(bool on)
    {
        var cover = on ? Host.Cover(_card) : null;
        AlbumTint = cover?.Tint;
        AlbumArt.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
        if (cover is { } c)
        {
            if (!ReferenceEquals(AlbumArtBrush.ImageSource, c.Image))
            {
                AlbumArtBrush.ImageSource = c.Image;
            }

            LeadIcon.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Dominant color of the shown album (null if none or grayscale → normal color).</summary>
    internal Windows.UI.Color? AlbumTint { get; private set; }

    /// <summary>
    /// Claude/Codex alerts (done, permission) show that agent's mark (spark / Codex mark from the user's PC) instead of ✓/🔔 —
    /// with a small ✓ next to ⌄, two symbols side by side looked flimsy (user feedback 09-30). Done/permission is signalled by the border and text color.
    /// </summary>
    private void ShowAgentMark(ActivityState state)
    {
        if (state.Kind is not (ActivityKind.AgentDone or ActivityKind.AgentPermission or ActivityKind.AgentWorking)
            && !(state.Kind == ActivityKind.Notice && state.Agent is not null))
        {
            return;   // every stage of an agent's card shows its logo (user 10-07), its notices too
        }

        var codex = state.AgentLabel == "Codex";
        if ((codex ? Host.CodexLogo : Host.ClaudeLogo) is null)
        {
            return;   // no mark on this PC → keep ✓/🔔
        }

        ClaudeGlyphImage.Source = Host.ClaudeLogo;   // the makers' colours here: the ring tells the stage (user 10-07)
        CodexGlyph.Source = Host.CodexLogo;

        ClaudeGlyphImage.Visibility = codex ? Visibility.Collapsed : Visibility.Visible;
        CodexGlyph.Visibility = codex ? Visibility.Visible : Visibility.Collapsed;
        LeadIcon.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// A notification's app icon (TODO 四, user 10-11: QQ's penguin, not the bell; every app's). Made when the app changes — a
    /// picture of this pill's own (<see cref="AppIcon"/>: one shared between windows brought the app down), there at once; the bell
    /// for an app that has none.
    /// </summary>
    private void ShowAppMark(ActivityState state)
    {
        // a notification, or a transfer of an app's own (剪映's export, 10-11)
        var app = state is { AppId: { Length: > 0 } id } && (state is { Kind: ActivityKind.Notice, Stage: "expand" } || state.Kind == ActivityKind.Transfer) ? id : null;
        if (app != _appMarkFor)
        {
            _appMarkFor = app;
            AppMark.Source = AppIcon.Now(app);   // at once: loaded later, the bell showed for a frame first (10-11)
        }

        var shown = app is not null && AppMark.Source is not null;
        AppMark.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (shown)
        {
            LeadIcon.Visibility = Visibility.Collapsed;
        }
    }

    private string? _appMarkFor;

    /// <summary>Turns the now-playing bars on/off. The animation is built once when turned on (the compositor runs it).</summary>
    private void SetEqualizer(bool on)
    {
        Equalizer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var fillKey = $"{AlbumTint}|{Host.Theme.Icon}";   // repaint only when the color changes (this is called every 250ms)
        if (on && _equalizerTint != fillKey)
        {
            // the brushes are built only here — built on every call before, 4 times a second, and thrown away (review 10-03)
            Brush brush = BrushOf(Host.Theme.Icon);
            if (AlbumTint is { } tint)
            {
                var top = Windows.UI.Color.FromArgb(255, (byte)Math.Min(255, tint.R + 40), (byte)Math.Min(255, tint.G + 40), (byte)Math.Min(255, tint.B + 40));
                var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1) };
                gradient.GradientStops.Add(new GradientStop { Color = top, Offset = 0 });
                gradient.GradientStops.Add(new GradientStop { Color = tint, Offset = 1 });
                brush = gradient;
            }

            _equalizerTint = fillKey;
            Eq0.Fill = Eq1.Fill = Eq2.Fill = Eq3.Fill = brush;
        }

        if (_frozen)
        {
            return;   // shown, standing
        }

        // Follow the real sound (setting) — a 30 Hz timer reads the speaker meter and sets the bar heights
        var meter = on && Host.Settings.AudioBars;
        if (meter != (_meterTimer is not null))
        {
            if (meter)
            {
                StopLoopBars();
                StartMeterBars();
            }
            else
            {
                StopMeterBars();
            }
        }

        if (meter)
        {
            return;
        }

        if (on && !_equalizerOn)
        {
            _equalizerOn = true;

            // The compositor (Composition) animates the bar sizes — a XAML Storyboard woke the UI and render threads every frame
            // and used 3–6% CPU for four bars (measured 09-30: music island 6–9% → 3% with bars hidden). Compositor animations don't wake app code.
            var patterns = new[]
            {
                (Values: new[] { 0.35f, 1.0f, 0.5f, 0.85f, 0.3f }, Seconds: 1.1),
                (Values: new[] { 0.9f, 0.4f, 1.0f, 0.55f, 0.8f }, Seconds: 0.9),
                (Values: new[] { 0.5f, 0.8f, 0.3f, 1.0f, 0.45f }, Seconds: 1.3),
                (Values: new[] { 0.75f, 0.35f, 0.9f, 0.4f, 1.0f }, Seconds: 1.0),
            };
            var bars = new[] { Eq0, Eq1, Eq2, Eq3 };
            for (var i = 0; i < bars.Length; i++)
            {
                var bar = bars[i];
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(bar);
                var compositor = visual.Compositor;
                visual.CenterPoint = new System.Numerics.Vector3((float)bar.Width / 2, (float)bar.Height / 2, 0f);   // centered pivot (09-30 G2)
                var sine = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.37f, 0f), new System.Numerics.Vector2(0.63f, 1f));
                var animation = compositor.CreateVector3KeyFrameAnimation();
                var values = patterns[i].Values;
                for (var k = 0; k <= values.Length; k++)
                {
                    var v = values[k % values.Length];
                    animation.InsertKeyFrame((float)k / values.Length, new System.Numerics.Vector3(1f, v, 1f), sine);
                }

                animation.Duration = TimeSpan.FromSeconds(patterns[i].Seconds);
                animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
                animation.DelayTime = TimeSpan.FromMilliseconds(120 * i);   // stagger each bar — so they don't pump as one block
                visual.StartAnimation("Scale", animation);
            }
        }
        else if (!on && _equalizerOn)
        {
            StopLoopBars();
        }
    }

    // ---------------- The icon: timer dial, paused song ----------------

    /// <summary>
    /// The big pill's timer icon: the same turning dial as the small one (user 10-07: only the small one turned). Built again only
    /// when the timer itself changes (the pill redraws a timer every tick; the compositor turns the hand in between).
    /// </summary>
    private void ShowLeadDial(ActivityState? state, Windows.UI.Color color)
    {
        var now = DateTimeOffset.UtcNow;
        var dial = state is null ? null : TimerDial.Of(state, _card?.Type == CardType.Inform, now);
        if (_frozen && dial is not null)
        {
            dial = dial with { Turn = 0 };   // the hand stands where it is
        }

        var key = dial is null || state is null ? null : $"{state.SourcePath}|{state.Due}|{state.Total}|{state.Left}|{state.T0}|{_card?.Type}|{color}|{RestFill()}|{_frozen}";
        if (dial is not null)
        {
            LeadIcon.Visibility = Visibility.Collapsed;
        }

        if (key == _leadDialKey)
        {
            return;
        }

        _leadDialKey = key;
        LeadDial.Children.Clear();
        if (dial is not null)
        {
            LeadDial.Children.Add(Host.Dial(dial, color, state!.IsPaused));
        }
    }

    private string? _leadDialKey;

    /// <summary>
    /// A paused song on the big pill, drawn like its small icon (user 10-07): the cover under a veil of pill colour with two bars, or
    /// without a cover a pill-coloured notch in the glyph's corner holding them.
    /// </summary>
    private void ShowLeadPause(bool on)
    {
        var cover = on && AlbumArt.Visibility == Visibility.Visible;
        var key = !on ? null : $"{cover}|{RestFill()}|{Host.Theme.Foreground}";
        if (key == _leadPauseKey)
        {
            return;
        }

        _leadPauseKey = key;
        LeadPause.Children.Clear();
        if (!on)
        {
            return;
        }

        var pill = RestFill();
        var ink = BrushOf(Host.Theme.Foreground);
        if (cover)
        {
            // the cover is 22 centred on the 16 slot: the veil the same size, the bars scaled from the 16 icon's
            LeadPause.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 22, Height = 22, Margin = new Thickness(-3), Fill = BrushOf(Windows.UI.Color.FromArgb(148, pill.R, pill.G, pill.B)) });
            var bars = PauseBars(ink, 22, 2.4, 8.2, 7.4, 12.2);
            bars.Margin = new Thickness(-3);
            LeadPause.Children.Add(bars);
            return;
        }

        AddPauseNotch(LeadPause, BrushOf(pill), ink);
    }

    private string? _leadPauseKey;

    /// <summary>Paused: a pill-coloured disc cuts the icon's bottom-right corner and the two bars sit in it.</summary>
    internal static void AddPauseNotch(Grid box, Brush pill, Brush ink)
    {
        box.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 9, Height = 9, Fill = pill, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -2, -2),
        });
        var bars = PauseBars(ink, 9, 1.5, 5, 2.2, 5.3);
        bars.HorizontalAlignment = HorizontalAlignment.Right;
        bars.VerticalAlignment = VerticalAlignment.Bottom;
        bars.Margin = new Thickness(0, 0, -2, -2);
        box.Children.Add(bars);
    }

    /// <summary>Two rounded bars in a <paramref name="size"/> square, at x <paramref name="x1"/> and <paramref name="x2"/>, centred vertically.</summary>
    internal static FrameworkElement PauseBars(Brush ink, double size, double width, double height, double x1, double x2)
    {
        var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
        foreach (var x in new[] { x1, x2 })
        {
            var bar = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = width, Height = height, RadiusX = width / 2, RadiusY = width / 2, Fill = ink };
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, (size - height) / 2);
            canvas.Children.Add(bar);
        }

        return canvas;
    }

    // ---------------- Progress ----------------

    /// <summary>
    /// Progress: a 2 high track on the bottom edge, the done part filled (TODO 7.9 D). Timers use it too (the ring on the right
    /// is gone: the text area runs to 208). Unknown size: a short piece flows along the track.
    /// </summary>
    internal void UpdateProgress(ActivityState? state)
    {
        var now = DateTimeOffset.UtcNow;
        double? fraction = state is null ? null
            : state.Kind == ActivityKind.Timer ? (TimerRemaining(state, now) > TimeSpan.Zero ? TimerFraction(state, now) : null)
            : state.Fraction;
        var flowing = state is { Kind: ActivityKind.Transfer, RunState: ActivityRunState.Running }
                      && (state.IsIndeterminate || (state.Stage is "download" or "copy" && state.Fraction is null));
        ProgressTrackBar.Visibility = fraction is not null || flowing ? Visibility.Visible : Visibility.Collapsed;
        if (state is null || (fraction is null && !flowing))
        {
            ProgressLine.Width = 0;
            StopFlow();
            return;
        }

        var light = ThemeColors.FromSystem().Light;
        ProgressTrackBar.Fill = BrushOf(light ? Windows.UI.Color.FromArgb(255, 0xD6, 0xD6, 0xD6) : Windows.UI.Color.FromArgb(255, 0x46, 0x46, 0x46));
        var fg = ForegroundFor(state);
        ProgressLine.Background = BrushOf(state.RunState switch
        {
            ActivityRunState.Error => Host.Theme.Bad,
            _ => Windows.UI.Color.FromArgb(217, fg.R, fg.G, fg.B),   // the text colour at 85%
        });
        if (flowing && _frozen)
        {
            ProgressLine.Width = 0;   // holding still: just the track
            StopFlow();
            return;
        }

        if (flowing)
        {
            ProgressLine.Width = 0;
            StartFlow(ProgressLine.Background, ProgressInset, ProgressLength);
            return;
        }

        StopFlow();
        ProgressLine.Width = Math.Max(1, ProgressLength * fraction!.Value);
    }

    internal const double FlowSeconds = 1.6;   // one pass of the flowing piece across the track

    /// <summary>Track width the running flow animation was built for; -1 = stopped.</summary>
    internal double FlowWidth { get; private set; } = -1;

    /// <summary>
    /// The flowing bar runs on the compositor — it used to be moved from the 250ms tick, only 4 frames a second, so it stuttered.
    /// Called every tick: only the color and position are refreshed; the animation restarts only when the track width changes.
    /// </summary>
    private void StartFlow(Brush brush, double left, double track)
    {
        FlowPiece.Background = brush;
        FlowTrack.Margin = new Thickness(left, 0, 0, 0);
        FlowTrack.Visibility = Visibility.Visible;
        if (Math.Abs(track - FlowWidth) < 0.5)
        {
            return;
        }

        FlowWidth = track;
        var piece = track / 4;
        FlowTrack.Width = track;
        FlowTrack.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, track, 2) };   // the piece slides in and out at the ends
        FlowPiece.Width = piece;

        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(FlowPiece, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(FlowPiece);
        var compositor = visual.Compositor;
        var linear = compositor.CreateLinearEasingFunction();
        var slide = compositor.CreateScalarKeyFrameAnimation();
        slide.InsertKeyFrame(0f, (float)-piece, linear);
        slide.InsertKeyFrame(1f, (float)track, linear);
        slide.Duration = TimeSpan.FromSeconds(FlowSeconds);
        slide.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation("Translation.X", slide);
    }

    private void StopFlow()
    {
        if (FlowWidth < 0)
        {
            return;
        }

        FlowWidth = -1;
        FlowTrack.Visibility = Visibility.Collapsed;
        try { Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(FlowPiece).StopAnimation("Translation.X"); } catch (ArgumentException) { }
    }

    // ---------------- Close button ----------------

    /// <summary>
    /// An inform card's button on the right (TODO 7.3, A4; moved right 10-08): "Close" — or, on a Claude card that can reach its
    /// chat, a round ↗ "open" (user 10-10). Every other card has none.
    /// </summary>
    internal void ShowClose(PillButton button)
    {
        CloseZone.Visibility = button == PillButton.None ? Visibility.Collapsed : Visibility.Visible;
        var open = button is PillButton.Open or PillButton.Keep;
        OpenIcon.Glyph = button == PillButton.Keep ? KeepGlyph : OpenGlyph;
        CloseZone.Width = ButtonGap + (open ? OpenChip : CloseChipWidth) + ButtonInset;
        CloseLabel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        OpenIcon.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PlaceRight();
        if (button == PillButton.Close && _closeInkFor != CloseLabel.Text)
        {
            _closeInkFor = CloseLabel.Text;
            CentreCloseInk();
        }
    }

    private string? _closeInkFor;
    private bool _inform;
    private bool _tailGap;

    /// <summary>
    /// The right side's gaps (pill layout rules, user 10-10; "+N" beside the ↗ 10-11): 8 from the text to the bars or the "+N";
    /// else none before a button (its zone starts with its own clickable 8); else 12 to the pill's edge. The "+N" is 12 from the
    /// edge, or right against the button's zone — "text 8 +N 8 ↗".
    /// </summary>
    private void PlaceRight()
    {
        var button = CloseZone.Visibility == Visibility.Visible;
        IslandContent.Margin = new Thickness(0, 0, _tailGap ? 8 : button || _inform ? 0 : 12, 0);
        MoreLabel.Margin = new Thickness(0, 0, button ? 0 : 12, 0);
    }

    /// <summary>
    /// The button's sizes (user 10-10): 8 from the text to the chip — the same 8 as icon to text — the chip 5 in from the pill's edge
    /// (22 high, concentric with its end). The clickable zone is all of it, gap included, the pill's whole height.
    /// </summary>
    private const double ButtonGap = 8;
    private const double ButtonInset = 5;
    private const double CloseChipWidth = 66;
    private const double OpenChip = 22;
    private const string OpenGlyph = "";
    private const string KeepGlyph = "";   // + : into the transit station (a stand-in, user 10-11: all icons get redrawn together later)
    /// <summary>
    /// "Close" centred by its glyphs, not its line box (user 10-07: it sat off-centre up and down): its own inked rows are found in
    /// a snapshot and the label is moved so their middle is the zone's middle, rounded to 0.5. Again when the text changes.
    /// </summary>
    private async void CentreCloseInk()
    {
        try
        {
            CloseLabel.UpdateLayout();
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(CloseLabel);
            var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await bitmap.GetPixelsAsync());
            int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
            int top = -1, bottom = -1;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[(((y * width) + x) * 4) + 3] > 64)
                    {
                        top = top < 0 ? y : top;
                        bottom = y;
                        break;
                    }
                }
            }

            if (top < 0 || CloseLabel.ActualHeight <= 0)
            {
                return;
            }

            var scale = height / CloseLabel.ActualHeight;
            var inkMiddle = (top + bottom + 1) / 2.0 / scale;
            CloseShift.Y = Math.Round(((CloseLabel.ActualHeight / 2) - inkMiddle) * 2) / 2;
            TaskbarHost.Log($"close ink: rows {top}–{bottom} of {height} px → y {CloseShift.Y}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // async void: nothing may escape
        {
            TaskbarHost.Log("close ink: measure failed — " + ex.Message);
        }
    }

    // ---------------- Words ----------------

    /// <summary>Prefix text. Differs by kind, and its length varies by language and progress, so it's included when truncating the name.</summary>
    internal string PrefixFor(ActivityState state, DateTimeOffset now) => state.Kind switch
    {
        ActivityKind.Timer => TimerRemaining(state, now) is { } left && left > TimeSpan.Zero
            ? FormatClock(left) + TextGap.Break   // paused shows as the left ⏸ icon + dimmed text (no emoji, it renders as a blue box)
            : Host.Text.Done + TextGap.Break,
        ActivityKind.Music => "",   // paused shows as the left ⏸ icon + dimmed text
        // Just the session's name, every stage (user 10-10): "Claude done · " took ~90 of the ~106 dip and hid the name. The logo
        // says which agent, the border (and the button) which stage. No name: "Claude done" etc. as before.
        ActivityKind.AgentDone => string.IsNullOrEmpty(state.Name) ? $"{state.AgentLabel} {Host.Text.AgentDone}" : "",
        ActivityKind.AgentPermission => string.IsNullOrEmpty(state.Name) ? $"{state.AgentLabel} {Host.Text.NeedsAnswer}" : "",
        ActivityKind.AgentWorking => string.IsNullOrEmpty(state.Name) ? $"{state.AgentLabel} {Host.Text.Working}" : "",
        _ => state.RunState switch
        {
            ActivityRunState.Done => Host.Text.Done + TextGap.Break,
            ActivityRunState.Error => Host.Text.Fail + TextGap.Break,
            _ when state.IsPausedTransfer => Host.Text.Paused + TextGap.Break,
            // browser download: speed (summed over all of them); the bar shows how far, when the total is known. The amount
            // received said little without a total, so the first second just shows the name
            _ when state.Stage is "download" or "copy" => state.Speed is { } speed ? $"{FormatBytes((long)speed)}/s" + TextGap.Break : "",
            _ when state.IsIndeterminate => (state.IsFinishing ? Host.Text.Finishing : Host.Text.Prep) + TextGap.Break,
            _ => $"{state.Fraction * 100:0}%{TransferEta(state, now)}" + TextGap.Break,
        },
    };

    /// <summary>"820 KB" · "12.3 MB" · "1.24 GB".</summary>
    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
    };

    private static TimeSpan? TimerRemaining(ActivityState state, DateTimeOffset now) => TimerParser.Remaining(state, now);

    private string? _speedPath;
    private readonly List<(DateTimeOffset At, long Done)> _speedSamples = [];

    /// <summary>
    /// Transfer remaining time "1m 20s" after a break. Speed is measured from the amount moved in the last 5 seconds (same as the Python version).
    /// Empty text if the speed isn't known yet.
    /// </summary>
    private string TransferEta(ActivityState state, DateTimeOffset now)
    {
        if (state.Done is not { } done || state.Total is not > 0)
        {
            return string.Empty;
        }

        if (state.SourcePath != _speedPath)
        {
            _speedPath = state.SourcePath;
            _speedSamples.Clear();
        }

        if (_speedSamples.Count == 0 || _speedSamples[^1].Done != done || now - _speedSamples[^1].At > TimeSpan.FromSeconds(1))
        {
            _speedSamples.Add((now, done));
        }

        _speedSamples.RemoveAll(sample => now - sample.At > TimeSpan.FromSeconds(5));
        if (_speedSamples.Count < 2)
        {
            return string.Empty;
        }

        var seconds = (_speedSamples[^1].At - _speedSamples[0].At).TotalSeconds;
        var speed = seconds > 0 ? (_speedSamples[^1].Done - _speedSamples[0].Done) / seconds : 0;
        return speed > 0 ? TextGap.Break + Host.Text.FormatDuration((state.Total.Value - done) / speed) : string.Empty;
    }

    /// <summary>Timer bar: fraction of time elapsed (stops in place when paused).</summary>
    private static double? TimerFraction(ActivityState state, DateTimeOffset now) => TimerParser.Fraction(state, now);

    /// <summary>Remaining time "12:34" or "1:02:03".</summary>
    private static string FormatClock(TimeSpan left)
    {
        var total = (int)Math.Ceiling(left.TotalSeconds);
        return total >= 3600
            ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
    }

    /// <summary>Left icon. The text uses no emoji (blue boxes, overlaps), so the icon conveys kind, direction and result.</summary>
    private static string GlyphFor(ActivityState state) => state.Kind switch
    {
        ActivityKind.AgentPermission => "",                       // bell
        ActivityKind.Error => "",                                 // error
        ActivityKind.Transfer => state.RunState switch
        {
            ActivityRunState.Done => "",                          // ✓
            ActivityRunState.Error => "",
            _ when state.IsPausedTransfer => "",                  // pause (download stopped growing)
            _ when state.Stage == "copy" => state.Glyph ?? "",     // Explorer: copy / unzip / compress, chosen by the watcher
            _ => state.Direction == TransferDirection.Incoming ? "" : "",   // receive ↓ / send ↑
        },
        ActivityKind.AgentDone => "",
        ActivityKind.Timer => "",                                 // stopwatch
        ActivityKind.Notice => state.Glyph ?? "",                   // icon chosen by the notice (bell if none)
        ActivityKind.Music => state.Stage == "paused" ? "" : "",
        ActivityKind.AgentWorking => TaskIcons.Chat,   // same as its small icon (it was the magnifier — a card flying down changed face)
        _ => "",
    };

    /// <summary>Card text: the normal colour, dimmed when paused — the card's type shows in its icon and border (TODO 7.9 D).</summary>
    private Windows.UI.Color ForegroundFor(ActivityState state)
        => state.IsPaused || state.IsPausedTransfer || (state.Kind == ActivityKind.Music && state.Stage == "paused") ? Host.Theme.Hint : Host.Theme.Foreground;

    /// <summary>Width of the card text (DIP): the text column, 176 on a plain card (108 beside the close zone).</summary>
    internal double AvailableTextWidth()
        => IslandContent.ActualWidth > 0 ? IslandContent.ActualWidth : CardTextWidth;

    /// <summary>Clips the text area to <paramref name="width"/> — reusing the clip (a new geometry on every draw before).</summary>
    private void ClipMarquee(double width)
    {
        var rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, width), Math.Max(1, MarqueeHost.ActualHeight));
        if (MarqueeHost.Clip is RectangleGeometry clip)
        {
            if (clip.Rect != rect)
            {
                clip.Rect = rect;
            }
        }
        else
        {
            MarqueeHost.Clip = new RectangleGeometry { Rect = rect };
        }
    }

    // ---------------- Fonts ----------------

    /// <summary>Font for the bold part of the island text (track title, "42%", etc.) — set by UseFontsFor.</summary>
    private FontFamily _islandBold = new("Segoe UI");

    private TextBlockMeasure? _measure;
    private FontFamily? _measureBold;

    /// <summary>The pill text's measurer, kept while the bold font stays the same (one per draw before — review 10-03).</summary>
    private TextBlockMeasure IslandMeasure()
    {
        var bold = _islandStrong ? _islandBold : null;
        if (_measure is null || !ReferenceEquals(_measureBold, bold))
        {
            (_measure, _measureBold) = (new TextBlockMeasure(IslandText, bold), bold);
        }

        return _measure;
    }

    /// <summary>Whether to bold the key part of the island text (alerts, progress) — music uses normal weight.</summary>
    private bool _islandStrong = true;

    /// <summary>Island text font, matched to the characters in the text (Latin, Hangul, kana) — UiFonts.Stack.</summary>
    internal void UseFontsFor(string text)
    {
        (var family, _islandBold) = AppFonts.For(text, Host.Language);
        if (!ReferenceEquals(IslandText.FontFamily, family))
        {
            IslandText.FontFamily = IslandTextCopy.FontFamily = family;   // WinUI's Grid has no FontFamily — set it on the text elements directly
        }
    }
}

/// <summary>The button on the right of a card (<see cref="PillView.ShowClose"/>).</summary>
internal enum PillButton
{
    None,
    Close,
    Open,
    /// <summary>Round "+": a copied file or image goes into the transit station.</summary>
    Keep,
}
