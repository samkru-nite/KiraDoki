using System.Globalization;

namespace IsleBar.Core.Localization;

/// <summary>
/// All UI strings for one language. A straight port of the Python version's <c>_T</c> dictionary, so
/// placeholders keep the Python shape (<c>{}</c>, <c>{m}</c>, <c>{s:02d}</c>) —
/// to allow side-by-side comparison with the original. Formatting goes only through the Format* methods below.
/// </summary>
public sealed class LanguageStrings
{
    public required string Finishing { get; init; }
    public required string Paused { get; init; }
    public required string Options { get; init; }
    public required string Settings { get; init; }
    public required string Quit { get; init; }

    /// <summary>Launch failure notice. The error message goes in <c>{}</c> — use <see cref="FormatLaunchFail"/>.</summary>
    public required string LaunchFail { get; init; }

    public required string Done { get; init; }
    public required string Fail { get; init; }
    public required string Prep { get; init; }

    /// <summary>1 minute or more. <c>{m}</c> minutes, <c>{s:02d}</c> seconds (two digits) — use <see cref="FormatDuration"/>.</summary>
    public required string MinSec { get; init; }

    /// <summary>Under 1 minute. <c>{s}</c> seconds — use <see cref="FormatDuration"/>.</summary>
    public required string Sec { get; init; }

    public required string Lang { get; init; }
    public required string Auto { get; init; }
    public required string Folder { get; init; }
    public required string FolderChange { get; init; }
    public required string FolderHome { get; init; }

    public required OptionText Session { get; init; }
    public required OptionText Model { get; init; }
    public required OptionText Effort { get; init; }
    public required OptionText Perm { get; init; }

    /// <summary>Remote control is an on/off toggle, so it has no choices.</summary>
    public required string Rc { get; init; }

    /// <summary>Title of "Agent" (Claude / Codex picker) in the settings window.</summary>
    public required string Agent { get; init; }


    /// <summary>File search result right-click menu (user 10-04).</summary>
    public required string MenuOpen { get; init; }

    public required string DownloadFoldersLabel { get; init; }
    public required string DownloadFoldersHint { get; init; }
    public required string Downloading { get; init; }
    public required string FileTransfer { get; init; }
    public required string ResetDefaults { get; init; }




    /// <summary>Timer label on the island ("Timer 18:42" — like "Pomodoro 18:42" in the preview).</summary>
    public required string Timer { get; init; }

    // ---- System notices (09-30). Placeholders: {p} percent · {m} minutes · {h} hours · {n} count · {} name.
    //      Formatted only via SystemWatch.NoticeText. ----

    /// <summary>When the charger is plugged in: "Charging · {p}%".</summary>
    public required string NoticeCharging { get; init; }

    /// <summary>When the charger is unplugged: "On battery · {p}%".</summary>
    public required string NoticeOnBattery { get; init; }

    /// <summary>Low battery / Bluetooth device battery: "Battery {p}%".</summary>
    public required string NoticeBatteryLevel { get; init; }

    /// <summary>Time remaining (under 1 hour): "about {m} min".</summary>
    public required string NoticeAboutMin { get; init; }

    /// <summary>Time remaining (1 hour or more): "about {h} h {m} min".</summary>
    public required string NoticeAboutHourMin { get; init; }

    /// <summary>Bluetooth "{} · Connected" — {} = device name.</summary>
    public required string NoticeConnected { get; init; }

    /// <summary>Bluetooth "{} · Disconnected".</summary>
    public required string NoticeDisconnected { get; init; }

    public required string NoticeOffline { get; init; }
    public required string NoticeOnline { get; init; }
    public required string NoticeFocus { get; init; }

    /// <summary>"Copied · {}" — {} = start of the copied text (masked if it looks like a password).</summary>
    public required string NoticeCopied { get; init; }

    public required string NoticeCopiedImage { get; init; }

    /// <summary>A single file.</summary>
    public required string NoticeCopiedFile { get; init; }

    /// <summary>Several files: "{n} files copied".</summary>
    public required string NoticeCopiedFiles { get; init; }

    public required string NoticeCpu { get; init; }
    public required string NoticeMemory { get; init; }

    /// <summary>Calendar "In {m} min" (followed by " · event name").</summary>
    public required string NoticeEventSoon { get; init; }

    /// <summary>Calendar event starting: "Now" (preceded by "event name · ").</summary>
    public required string NoticeEventNow { get; init; }

    public required string NoticeMicInUse { get; init; }
    public required string NoticeCameraInUse { get; init; }

    // ---- System notice on/off toggles in the settings window ----

    /// <summary>Group title "Island notices" in the settings window.</summary>
    public required string NotifySection { get; init; }

    public required string NotifyPower { get; init; }
    public required string NotifyBluetooth { get; init; }
    public required string NotifyNetwork { get; init; }
    public required string NotifyFocus { get; init; }
    public required string NotifyClipboard { get; init; }
    public required string NotifyLoad { get; init; }
    public required string NotifyPrivacy { get; init; }

    /// <summary>Setting: pull in Windows notifications (optional, off by default).</summary>
    public required string NotifyToasts { get; init; }

    /// <summary>Settings toggle: hide Windows' own notification pop-ups, show them only on the bar.</summary>
    public required string HideToastBanners { get; init; }

    /// <summary>Settings toggle: also show browser playback (videos) on the music isle.</summary>
    public required string BrowserMedia { get; init; }

    /// <summary>An agent hit its plan's usage limit; {a} = agent name.</summary>
    public required string UsageLimit { get; init; }

    /// <summary>Early warning at 90% of a usage window; {a} = agent, {p} = percent.</summary>
    public required string UsageHigh { get; init; }

    /// <summary>An agent's turn ended on an error (not the usage limit); {a} = agent.</summary>
    public required string AgentStopped { get; init; }

    /// <summary>"{a} interrupted": the session ended while the agent was still working or waiting (TODO 7.9 D, 10-06).</summary>
    public required string AgentInterrupted { get; init; }

    /// <summary>Why it stopped: authentication failed.</summary>
    public required string StopLogin { get; init; }

    /// <summary>Why it stopped: billing / account / plan problem.</summary>
    public required string StopBilling { get; init; }

    /// <summary>Why it stopped: overloaded servers.</summary>
    public required string StopBusy { get; init; }

    /// <summary>Why it stopped: any other error.</summary>
    public required string StopError { get; init; }

    /// <summary>Settings switch: no chime while a full-screen app (game, video) is in front.</summary>
    public required string MuteChimeFullscreen { get; init; }

    /// <summary>When the usage limit lifts; {} = local time (HH:mm).</summary>
    public required string ResetsAt { get; init; }

    /// <summary>Settings: connect / disconnect Claude Code and Codex (writes or removes IsleBar's hooks).</summary>
    public required string ConnectAgents { get; init; }

    /// <summary>Store build's first-start card: "Show what Claude Code / Codex is doing here — click to connect".</summary>
    public required string ConnectAgentsCard { get; init; }

    /// <summary>Title of the ICS URL input for calendar notices.</summary>
    public required string CalendarIcs { get; init; }

    /// <summary>Hint for the ICS input (shown when empty).</summary>
    public required string CalendarIcsHint { get; init; }

    /// <summary>Command that receives dropped files (settings window).</summary>
    public required string DropCommand { get; init; }

    /// <summary>Drop command description.</summary>
    public required string DropCommandHint { get; init; }



    /// <summary>"Appearance" section in the settings window.</summary>
    public required string StyleSection { get; init; }

    // Advanced/power options (10-01). Not required — an English default keeps every language compiling; the Korean catalog
    // overrides these. (A small power-options surface, so other languages fall back to English until translated.)

    /// <summary>"Advanced" section heading in the settings window.</summary>
    public string AdvancedSection { get; init; } = "Advanced";

    /// <summary>Style: highlight line + faint border.</summary>
    public required string StyleGlass { get; init; }

    /// <summary>Pomodoro (right-click menu, island label).</summary>
    public required string Pomodoro { get; init; }

    /// <summary>Stopwatch (right-click menu, island label).</summary>
    public required string Stopwatch { get; init; }

    /// <summary>Label for the Pomodoro focus stage.</summary>
    public required string Focus { get; init; }

    /// <summary>Label for the Pomodoro break stage.</summary>
    public required string Break { get; init; }

    /// <summary>"{0} min" — timer length in the right-click menu.</summary>
    public required string Minutes { get; init; }

    /// <summary>Settings window: Pomodoro lengths row.</summary>
    public required string PomodoroMinutes { get; init; }

    /// <summary>Settings window: quick timer list.</summary>
    public required string QuickTimersLabel { get; init; }

    /// <summary>Settings: where on the taskbar the bar sits (10-05).</summary>
    public required string PositionLabel { get; init; }

    /// <summary>Position choice: the left end.</summary>
    public required string PositionLeft { get; init; }

    /// <summary>Position choice: the middle of the screen.</summary>
    public required string PositionCenter { get; init; }

    /// <summary>Position choice: left of the tray.</summary>
    public required string PositionTray { get; init; }

    /// <summary>Position choice: set by hand.</summary>
    public required string PositionManual { get; init; }

    /// <summary>Settings: the manual position, distance from the left end (dip).</summary>
    public required string ManualOffsetLabel { get; init; }

    /// <summary>Setting: corners of the pills (TODO 7.9 D).</summary>
    public required string CornersLabel { get; init; }

    /// <summary>Corners choice: half the height (like Windows' search box).</summary>
    public required string CornersRound { get; init; }

    /// <summary>Corners choice: small rounded corners (8).</summary>
    public required string CornersSmall { get; init; }

    /// <summary>Setting: which side of the big pill the small pill sits on.</summary>
    public required string BallSideLabel { get; init; }

    /// <summary>Small pill side: right of the big pill.</summary>
    public required string BallSideRight { get; init; }

    /// <summary>Small pill side: left of the big pill.</summary>
    public required string BallSideLeft { get; init; }

    /// <summary>Setting: how a task icon and the big pill swap places (E4, user 10-07), and its three choices.</summary>
    public required string SwapAnimLabel { get; init; }

    public required string SwapAnimLine { get; init; }

    public required string SwapAnimArc { get; init; }

    public required string SwapAnimFlip { get; init; }

    /// <summary>Setting: card and icon movements overshoot a little and settle (user 10-07).</summary>
    public required string SwapBounce { get; init; }
    public required string Transparency { get; init; }

    /// <summary>Settings: how a card lifts out of the big pill (TODO 7.9 F).</summary>
    public required string LiftAnimLabel { get; init; }

    public required string LiftAnimMorph { get; init; }

    public required string LiftAnimSlide { get; init; }

    /// <summary>Settings: how far apart the ⋯ list's pills set off (TODO 7.9 F3).</summary>
    public required string ListStaggerLabel { get; init; }

    /// <summary>The ⋯ list: beside the card the big pill is showing.</summary>
    public required string OnBigPill { get; init; }

    /// <summary>The close zone on an inform card (TODO 7.3): closes the card ("I've seen it").</summary>
    public required string CloseCard { get; init; }

    /// <summary>Right-click on the big pill: keep its card in the small pill (甲⑤).</summary>
    public required string Pin { get; init; }

    /// <summary>Right-click on a pinned card: let it follow the usual rules again.</summary>
    public required string Unpin { get; init; }







    /// <summary>Setting: card text left-aligned or centred on the big pill (user 10-07).</summary>
    public required string CardTextLabel { get; init; }

    /// <summary>Card text choice: left-aligned.</summary>
    public required string CardTextLeft { get; init; }

    /// <summary>Card text choice: icon and text centred together, when it fits (long text stays left-aligned and scrolls).</summary>
    public required string CardTextCenterGroup { get; init; }

    /// <summary>Card text choice: only the text centred, the icon stays at the left.</summary>
    public required string CardTextCenterText { get; init; }

    /// <summary>Settings: diagnostics section heading.</summary>
    public required string DiagSection { get; init; }

    /// <summary>Diagnostics: is the bar inside the taskbar.</summary>
    public required string DiagEmbed { get; init; }

    /// <summary>Diagnostics: working.</summary>
    public required string DiagOk { get; init; }

    /// <summary>Diagnostics: attaching failed, the bar floats above the taskbar.</summary>
    public required string DiagFloating { get; init; }

    /// <summary>Diagnostics: no room at the chosen position, the bar is hidden.</summary>
    public required string DiagNoRoom { get; init; }

    /// <summary>Settings: music bars follow the actual sound.</summary>
    public required string AudioBars { get; init; }

    /// <summary>Island: "Claude working · session".</summary>
    public required string Working { get; init; }

    /// <summary>Pill word for an agent waiting on a permission prompt or question (orange): "Claude needs you · name".</summary>
    public required string NeedsAnswer { get; init; }

    /// <summary>"done" after an agent's name ("Claude done") — lower-case like <see cref="Working"/>, unlike the stand-alone <see cref="Done"/> (10-01).</summary>
    public required string AgentDone { get; init; }

    // ---- the lifted card (TODO 7.9 F2): buttons, state words, notes. "{}" = the value ----
    public required string CardOpenChat { get; init; }
    public required string CardHide { get; init; }
    public required string CardPause { get; init; }
    public required string CardResume { get; init; }
    public required string CardAgain { get; init; }
    public required string CardOpenFolder { get; init; }
    public required string CardPrivacy { get; init; }

    /// <summary>After a timer's big number: the time left.</summary>
    public required string CardRemaining { get; init; }
    public required string CardStarted { get; init; }
    public required string CardEnds { get; init; }
    public required string CardJustNow { get; init; }
    public required string CardMinutesAgo { get; init; }

    /// <summary>An agent at work: "running 3 min" ({} = whole minutes).</summary>
    public required string CardRunningFor { get; init; }
    public required string CardRunning { get; init; }
    public required string CardEnded { get; init; }
    public required string CardPlaying { get; init; }
    public required string CardInUse { get; init; }
    public required string CardDownloaded { get; init; }

    /// <summary>Source names on the card's top row.</summary>
    public required string CardMusic { get; init; }
    public required string CardSystem { get; init; }
    public required string CardDownloads { get; init; }

    /// <summary>A download's source when several Chromium browsers are open and its temp file can't tell which (F2b).</summary>
    public required string CardBrowser { get; init; }
    public required string CardMicrophone { get; init; }
    public required string CardCamera { get; init; }

    public string FormatLaunchFail(string message)
        => LaunchFail.Replace("{}", message ?? string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Remaining/elapsed time display. <see cref="MinSec"/> for 60 seconds or more, otherwise <see cref="Sec"/>.
    /// Negative values are treated as 0.
    /// </summary>
    public string FormatDuration(double seconds)
    {
        var total = seconds > 0 && !double.IsNaN(seconds) ? (long)seconds : 0;
        if (total < 60)
        {
            return Sec.Replace("{s}", total.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        var minutes = total / 60;
        var rest = total % 60;
        return MinSec
            .Replace("{m}", minutes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{s:02d}", rest.ToString("00", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Model option matching the current alias list. Only the first choice (the translated "Default") is kept; the rest use
    /// each alias with its first letter capitalized — so the buttons follow automatically as aliases are added or removed.
    /// </summary>
    public OptionText ModelFor(IReadOnlyList<string> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var choices = new List<string>(models.Count + 1) { Model.Choices[0] };
        foreach (var alias in models)
        {
            choices.Add(Capitalize(alias));
        }

        return new OptionText(Model.Title, choices);
    }

    /// <summary>Same as Python's <c>str.capitalize()</c>: first letter upper-case, the rest lower-case.</summary>
    internal static string Capitalize(string value)
        => string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    /// <summary>Finds a choice option's strings by option name. rc has no choices, so null.</summary>
    public OptionText? OptionFor(string option) => option switch
    {
        Configuration.LaunchOptionDefs.Session => Session,
        Configuration.LaunchOptionDefs.Model => Model,
        Configuration.LaunchOptionDefs.Effort => Effort,
        Configuration.LaunchOptionDefs.Perm => Perm,
        _ => null,
    };

    /// <summary>Option title used in the pin menu (including rc).</summary>
    public string TitleFor(string option)
        => option == Configuration.LaunchOptionDefs.Rc ? Rc : OptionFor(option)?.Title ?? option;
}
