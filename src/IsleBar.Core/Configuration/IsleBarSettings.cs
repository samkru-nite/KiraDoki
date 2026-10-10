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
    /// Folders watched for downloads in progress (browsers' .crdownload/.part, Baidu Netdisk's .baiduyun.p.downloading), one per
    /// line — for browsers set to save elsewhere or to ask each time. Empty = the system Downloads folder.
    /// </summary>
    public List<string> DownloadFolders { get; set; } = [];

    /// <summary>Pomodoro focus, short break, long break (minutes). User feedback 09-30: asked how to change the durations.</summary>
    public int PomodoroFocus { get; set; } = 25;

    public int PomodoroBreak { get; set; } = 5;

    public int PomodoroLong { get; set; } = 15;

    /// <summary>Where on the taskbar the bar sits: "left" (default) · "center" · "tray" · "manual" (TODO 7.1, 10-05). See <see cref="Ui.BarSlot"/>.</summary>
    public string Position { get; set; } = "left";

    /// <summary>For "manual": distance from the taskbar's left end, in dip.</summary>
    public int ManualOffset { get; set; } = 12;

    /// <summary>Music bars follow the actual sound (speaker peak meter) instead of a canned loop. Off by default.</summary>
    public bool AudioBars { get; set; }

    /// <summary>Quick timers in the right-click menu (minutes).</summary>
    public List<int> QuickTimers { get; set; } = [5, 25];

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

    /// <summary>The calendars' ICS addresses (each its own calendar: a gap only counts within one). None = calendar notices off.</summary>
    public List<string> Calendars { get; set; } = [];


    public IsleBarSettings Clone() => new()
    {
        Lang = Lang,
        DownloadFolders = [.. DownloadFolders],
        PomodoroFocus = PomodoroFocus,
        PomodoroBreak = PomodoroBreak,
        PomodoroLong = PomodoroLong,
        QuickTimers = [.. QuickTimers],
        Position = Position,
        ManualOffset = ManualOffset,
        AudioBars = AudioBars,
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
        Calendars = [.. Calendars],
    };
}
