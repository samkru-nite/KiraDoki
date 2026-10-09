namespace IsleBar.Core.Configuration;

/// <summary>
/// Just the keys we know from the settings file (islebar.json).
/// Unknown keys are preserved as-is by <see cref="ConfigStore"/>, so they are not lost even if absent here.
/// </summary>
public sealed class IsleBarSettings
{
    /// <summary>"auto" or a language code from <see cref="Localization.LanguageCatalog"/>.</summary>
    public string Lang { get; set; } = "auto";

    /// <summary>
    /// The agent to launch (<see cref="Launch.AgentKind"/>). <b>Chosen in settings</b>, not in the
    /// quick-launch menu — it is not something changed often (decision 2026-09-29).
    /// </summary>
    public string Agent { get; set; } = Launch.AgentKind.Claude;

    /// <summary>Claude launch options.</summary>
    public LaunchOptions Values { get; set; } = new();

    /// <summary>
    /// Codex launch options. <b>Remembered separately</b> — the two agents use different models
    /// (Claude <c>opus[1m]</c> / Codex <c>gpt-5.6-sol</c>), and effort/remote control do not exist for Codex.
    /// </summary>
    public LaunchOptions CodexValues { get; set; } = new();

    /// <summary>Working folder. Empty string means home.</summary>
    public string Folder { get; set; } = "";

    /// <summary>
    /// Recently used working folders. Remembered so they can be picked right from the bar
    /// (project slash commands only show up when launched from that folder).
    /// </summary>
    public List<string> RecentFolders { get; set; } = [];

    /// <summary>File search (Everything) on/off.</summary>
    public bool Files { get; set; } = true;


    /// <summary>File search: how many results to list (3–50; the list scrolls) — user 10-04.</summary>
    public int FileResults { get; set; } = Search.FileSearchQuery.DefaultCount;

    /// <summary>File search order: "name" (as Everything shows it, folders first) · "modified" · "size" — chosen in the results list (user 10-04).</summary>
    public string FileSort { get; set; } = Search.FileSearchQuery.SortName;

    /// <summary>
    /// Folders file search hides, one rule per line (<see cref="Search.FileExcludes"/>; editable in settings — user 10-04).
    /// Null = the built-in list, so a person who never edited it gets later improvements to it.
    /// </summary>
    public List<string>? FileExclude { get; set; }

    /// <summary>The rules in effect: the person's list, or the built-in one.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> FileExcludeLines => FileExclude ?? [.. Search.FileExcludes.Defaults];

    /// <summary>
    /// Folders watched for downloads in progress (browsers' .crdownload/.part, Baidu Netdisk's .baiduyun.p.downloading), one per
    /// line — for browsers set to save elsewhere or to ask each time. Empty = the system Downloads folder.
    /// </summary>
    public List<string> DownloadFolders { get; set; } = [];

    /// <summary>Tab order ("claude" · "files" · "web"). The first one is the default mode.</summary>
    public List<string> ModeOrder { get; set; } = [.. Localization.BarModes.DefaultOrder.Select(Localization.BarModes.Key)];

    /// <summary>
    /// Modes removed from the Tab order (user feedback 09-30: wanted to be able to remove and add Tab-order entries). File search has long been toggled via <see cref="Files"/>, so
    /// only Claude and web go here. Not all can be turned off (the settings window prevents turning off the last one).
    /// </summary>
    public List<string> ModesOff { get; set; } = [];

    /// <summary>Pomodoro focus, short break, long break (minutes). User feedback 09-30: asked how to change the durations.</summary>
    public int PomodoroFocus { get; set; } = 25;

    public int PomodoroBreak { get; set; } = 5;

    public int PomodoroLong { get; set; } = 15;

    /// <summary>Where on the taskbar the bar sits: "left" (default) · "center" · "tray" · "manual" (TODO 7.1, 10-05). See <see cref="Ui.BarSlot"/>.</summary>
    public string Position { get; set; } = "left";

    /// <summary>For "manual": distance from the taskbar's left end, in dip.</summary>
    public int ManualOffset { get; set; } = 12;

    /// <summary>Corners of the big and small pills: "round" (half the height, like Windows' search box — default) · "small" (8 dip). TODO 7.9 D, 10-06.</summary>
    public string PillCorners { get; set; } = "round";

    /// <summary>Side of the big pill the small pill sits on: "right" (default) · "left". TODO 7.9 D, 10-06.</summary>
    public string BallSide { get; set; } = "right";

    /// <summary>How a task icon and the big pill swap places: "line" (default) · "arc" · "flip" (<see cref="Ui.SwapMotion"/>).</summary>
    public string SwapAnimation { get; set; } = "line";

    /// <summary>Card and icon movements end with a small bounce (user 10-07): the swap and, since 10-08, the lifted card. Saved as "swap_bounce".</summary>
    public bool Bounce { get; set; } = true;

    /// <summary>How a card lifts out of the big pill: "morph" (default) · "slide" (<see cref="Ui.LiftMotion"/>, TODO 7.9 F, user 10-08).</summary>
    public string LiftAnimation { get; set; } = "morph";

    /// <summary>The ⋯ list: how far apart its pills set off, ms: "20" (default) · "40" (<see cref="Ui.ListMotion"/>; user 10-09 tries
    /// both, the other goes).</summary>
    public string ListStagger { get; set; } = "20";

    /// <summary>Card text on the big pill (user 10-07): "left" (default) · "group" = icon and text centred together · "text" = only the
    /// text centred, the icon stays. Centred only when it fits; text that has to scroll is left-aligned.</summary>
    public string CardTextAlign { get; set; } = "left";

    /// <summary>Music bars follow the actual sound (speaker peak meter) instead of a canned loop. Off by default.</summary>
    public bool AudioBars { get; set; }

    /// <summary>Quick timers in the right-click menu (minutes).</summary>
    public List<int> QuickTimers { get; set; } = [5, 25];

    /// <summary>Recent questions, oldest → newest. At most <see cref="History.QuestionHistory.Max"/>.</summary>
    public List<string> History { get; set; } = [];

    /// <summary>Claude model alias list (button order). Auto-updated from the model docs.</summary>
    public List<string> Models { get; set; } = [.. LaunchOptionDefs.DefaultModels];

    /// <summary>
    /// Codex model list. <b>Not auto-updated</b> since there is no doc to fetch — edit it in settings.
    /// </summary>
    public List<string> CodexModels { get; set; } = [.. Launch.CodexProfile.Models];

    /// <summary>When the alias list was last checked (Unix seconds). 0 = never checked.</summary>
    public double ModelsChecked { get; set; }

    // ---- System notices (09-30). All on by default — turn off only the unneeded ones in settings. ----

    /// <summary>Charger plugged/unplugged, low battery.</summary>
    public bool NotifyPower { get; set; } = true;

    /// <summary>Bluetooth device connected/disconnected.</summary>
    public bool NotifyBluetooth { get; set; } = true;

    /// <summary>Internet lost/reconnected.</summary>
    public bool NotifyNetwork { get; set; } = true;

    /// <summary>During a Windows focus session.</summary>
    public bool NotifyFocus { get; set; } = true;

    /// <summary>Copy confirmation (masked if it looks like a password).</summary>
    public bool NotifyClipboard { get; set; } = true;

    /// <summary>When CPU/memory stays high for a while.</summary>
    public bool NotifyLoad { get; set; } = true;

    /// <summary>Microphone/camera in use.</summary>
    public bool NotifyPrivacy { get; set; } = true;

    /// <summary>
    /// Pulls Windows notifications (KakaoTalk, mail, etc.) onto the island. Reads all notification content, so off by default (opt-in feature, 09-30).
    /// </summary>
    public bool NotifyToasts { get; set; }

    /// <summary>
    /// Show Windows notifications only on the bar: turns off Windows' own pop-up banners (per app) while <see cref="NotifyToasts"/>
    /// pulls them onto the bar, and puts them back when turned off or when the bar quits (user 10-01). Off by default.
    /// </summary>
    public bool HideToastBanners { get; set; }

    /// <summary>
    /// Also show what plays in a browser (YouTube, Bilibili…) on the music isle. Off by default — browser playback was filtered out
    /// on purpose (user feedback 09-30); this puts it back for those who want it (10-04).
    /// </summary>
    public bool BrowserMedia { get; set; }

    /// <summary>
    /// No chime while a full-screen app (game, video, presentation) is in front (10-03). Off by default — some people wait for an
    /// agent while watching or playing and want to hear it. Do Not Disturb is always respected regardless.
    /// </summary>
    public bool MuteChimeFullscreen { get; set; }

    /// <summary>ICS URL for calendar notices. Empty string turns calendar notices off.</summary>
    public string CalendarIcs { get; set; } = "";

    /// <summary>
    /// Command that receives files dropped onto the pill (<c>"executable" args…</c>; file paths are appended). If empty,
    /// dropped files are attached to the prompt instead.
    /// </summary>
    public string DropCommand { get; set; } = "";

    /// <summary>Style (design C1, 09-30): 1-pixel highlight line along the top of the pill + faint border. Most noticeable on a dark taskbar.</summary>
    public bool StyleGlass { get; set; }


    /// <summary>Launch options for the given agent.</summary>
    public LaunchOptions ValuesFor(string? agent)
        => agent == Launch.AgentKind.Codex ? CodexValues : Values;

    /// <summary>Model list for the given agent.</summary>
    public List<string> ModelsFor(string? agent)
        => agent == Launch.AgentKind.Codex ? CodexModels : Models;

    public IsleBarSettings Clone() => new()
    {
        Lang = Lang,
        Agent = Agent,
        Values = Values.Clone(),
        CodexValues = CodexValues.Clone(),
        Folder = Folder,
        RecentFolders = [.. RecentFolders],
        Files = Files,
        FileResults = FileResults,
        FileSort = FileSort,
        FileExclude = FileExclude is null ? null : [.. FileExclude],
        DownloadFolders = [.. DownloadFolders],
        ModeOrder = [.. ModeOrder],
        ModesOff = [.. ModesOff],
        PomodoroFocus = PomodoroFocus,
        PomodoroBreak = PomodoroBreak,
        PomodoroLong = PomodoroLong,
        QuickTimers = [.. QuickTimers],
        Position = Position,
        ManualOffset = ManualOffset,
        PillCorners = PillCorners,
        BallSide = BallSide,
        SwapAnimation = SwapAnimation,
        Bounce = Bounce,
        LiftAnimation = LiftAnimation,
        ListStagger = ListStagger,
        CardTextAlign = CardTextAlign,
        AudioBars = AudioBars,
        History = [.. History],
        Models = [.. Models],
        CodexModels = [.. CodexModels],
        ModelsChecked = ModelsChecked,
        NotifyPower = NotifyPower,
        NotifyBluetooth = NotifyBluetooth,
        NotifyNetwork = NotifyNetwork,
        NotifyFocus = NotifyFocus,
        NotifyClipboard = NotifyClipboard,
        NotifyLoad = NotifyLoad,
        NotifyPrivacy = NotifyPrivacy,
        NotifyToasts = NotifyToasts,
        HideToastBanners = HideToastBanners,
        BrowserMedia = BrowserMedia,
        MuteChimeFullscreen = MuteChimeFullscreen,
        CalendarIcs = CalendarIcs,
        DropCommand = DropCommand,
        StyleGlass = StyleGlass,
    };
}
