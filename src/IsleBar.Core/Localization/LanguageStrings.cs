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
    public required string Settings { get; init; }
    public required string Quit { get; init; }


    public required string Done { get; init; }
    public required string Fail { get; init; }
    public required string Prep { get; init; }

    /// <summary>1 minute or more. <c>{m}</c> minutes, <c>{s:02d}</c> seconds (two digits) — use <see cref="FormatDuration"/>.</summary>
    public required string MinSec { get; init; }

    /// <summary>Under 1 minute. <c>{s}</c> seconds — use <see cref="FormatDuration"/>.</summary>
    public required string Sec { get; init; }

    public required string Lang { get; init; }
    public required string Auto { get; init; }




    /// <summary>File search result right-click menu (user 10-04).</summary>
    public required string MenuOpen { get; init; }

    public required string DownloadFoldersLabel { get; init; }
    public required string DownloadFoldersHint { get; init; }
    public required string Downloading { get; init; }
    public required string FileTransfer { get; init; }




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

    /// <summary>Files cut (Ctrl+X in File Explorer), a single one.</summary>
    public required string NoticeCutFile { get; init; }

    /// <summary>Several files cut: "{n} files cut".</summary>
    public required string NoticeCutFiles { get; init; }

    /// <summary>A chat app's new message whose words it doesn't give (WeChat only flashes its taskbar button, 10-11).</summary>
    public required string NoticeNewMessage { get; init; }

    public required string NoticeCpu { get; init; }
    public required string NoticeMemory { get; init; }

    /// <summary>Calendar "In {m} min" (a reminder within the hour, a gap; the event name follows, grey).</summary>
    public required string NoticeEventSoon { get; init; }

    /// <summary>Calendar event started: "Started" (the event name follows, grey).</summary>
    public required string NoticeEventNow { get; init; }

    /// <summary>Calendar event under way: "Ongoing" (the event name follows, grey).</summary>
    public required string NoticeEventOngoing { get; init; }

    /// <summary>Calendar reminder for an event tomorrow: "Tomorrow {t}" ({t} = its start time).</summary>
    public required string NoticeEventTomorrow { get; init; }
    public required string NoticeDrive { get; init; }
    public required string NoticeDriveRemoved { get; init; }
    public required string NoticeDriveSafe { get; init; }
    public required string NoticeDriveBusy { get; init; }

    /// <summary>The transit station card (TODO ④, 10-10): its name; one file / "{n} files"; the drop hint.</summary>
    public required string NoticeTransit { get; init; }
    public required string NoticeTransitOne { get; init; }
    public required string NoticeTransitFiles { get; init; }

    /// <summary>Several folders / files and folders dragged out of the transit list at once ("3个文件夹" / "3个项目", 10-11).</summary>
    public required string NoticeTransitFolders { get; init; }
    public required string NoticeTransitItems { get; init; }
    public required string NoticeTransitDrop { get; init; }
    public required string NoticeTransitCannot { get; init; }
    public required string CardAddToTransit { get; init; }
    public required string TransitPicture { get; init; }
    public required string FileCopy { get; init; }
    public required string FileMove { get; init; }
    public required string FileUnzip { get; init; }
    public required string FileZip { get; init; }
    public required string FileCopied { get; init; }
    public required string FileMoved { get; init; }
    public required string FileUnzipped { get; init; }
    public required string FileZipped { get; init; }

    /// <summary>A video editor writing its export (Jianying, TODO 四 10-11), and done.</summary>
    public required string FileExport { get; init; }
    public required string FileExported { get; init; }
    /// <summary>Steam: a game's download went through — a new one ("已安装") or an update ("已更新").</summary>
    public required string GameInstalled { get; init; }
    public required string GameUpdated { get; init; }
    /// <summary>The printer card: printing, printed, and what stops it (paper out, a jam, offline, toner, something to see to, an error).</summary>
    public required string Printing { get; init; }
    public required string Printed { get; init; }
    public required string PrintPaperOut { get; init; }
    public required string PrintPaperJam { get; init; }
    public required string PrintOffline { get; init; }
    public required string PrintToner { get; init; }
    public required string PrintAttention { get; init; }
    public required string PrintError { get; init; }
    public required string FileItems { get; init; }

    /// <summary>The lifted transit card: its button that empties it, and a file no longer where it was.</summary>
    public required string CardClear { get; init; }
    public required string CardFileGone { get; init; }
    public required string NoticeDriveUnsafe { get; init; }
    public required string CardDriveBusy { get; init; }
    public required string CardDriveBusyBy { get; init; }
    public required string CardDriveUnsafe { get; init; }
    public required string CardEject { get; init; }

    /// <summary>Calendar event over: "Ended" (the event name follows, grey).</summary>
    public required string NoticeEventEnded { get; init; }

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







    // Advanced/power options (10-01). Not required — an English default keeps every language compiling; the Korean catalog
    // overrides these. (A small power-options surface, so other languages fall back to English until translated.)



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

    /// <summary>Settings: where on the taskbar the bar sits (10-05).</summary>
    public required string PositionLabel { get; init; }

    /// <summary>Position choice: the left end.</summary>
    public required string PositionLeft { get; init; }

    /// <summary>Position choice: the middle of the screen.</summary>
    public required string PositionCenter { get; init; }

    /// <summary>Position choice: left of the tray.</summary>

    /// <summary>Position choice: set by hand.</summary>
    public required string PositionManual { get; init; }

    /// <summary>Settings: the manual position, distance from the left end (dip).</summary>
    public required string ManualOffsetLabel { get; init; }
















    /// <summary>The close zone on an inform card (TODO 7.3): closes the card ("I've seen it").</summary>
    public required string CloseCard { get; init; }

    /// <summary>The tooltip on the round "open" button of a done / interrupted card (user 10-10): "Open in {a}" — {a} = the app.</summary>
    public required string OpenWith { get; init; }

    /// <summary>Right-click on the big pill: keep its card in the small pill (甲⑤; "收起", was "固定" till 10-11).</summary>
    public required string Tuck { get; init; }

    /// <summary>Right-click on a tucked card: let it follow the usual rules again.</summary>
    public required string Untuck { get; init; }

    /// <summary>Right-click on a card: it stays on the big pill for as long as it lasts ("置顶", user 10-11).</summary>
    public required string Pin { get; init; }

    /// <summary>Right-click on a card pinned to the big pill: it follows the usual rules again.</summary>
    public required string Unpin { get; init; }











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

    /// <summary>Time left on a transfer: "剩余2分钟" ({} = a length).</summary>
    public required string CardLeft { get; init; }

    /// <summary>How long a transfer has taken / took: "用时3分钟".</summary>
    public required string CardTook { get; init; }

    /// <summary>An export without a percentage: how much is written so far.</summary>
    public required string CardWritten { get; init; }

    /// <summary>A game download: bytes so far ({a}) of all ({b}).</summary>
    public required string CardBytesOf { get; init; }

    /// <summary>A print: pages printed ({a}) of all ({b}).</summary>
    public required string CardPagesOf { get; init; }

    /// <summary>A length under a minute.</summary>
    public required string CardSeconds { get; init; }

    /// <summary>A length of an hour or more: hours ({a}) and minutes ({b}).</summary>
    public required string CardHours { get; init; }

    /// <summary>A finished game download's button: starts the game (Steam).</summary>
    public required string CardPlay { get; init; }

    /// <summary>A finished transfer's button: shows the file in its folder.</summary>
    public required string CardReveal { get; init; }

    /// <summary>A lifted system card's source row: power and battery (TODO ⑤ 掀起卡片适配, 10-11).</summary>
    public required string CardPower { get; init; }

    /// <summary>A lifted system card's source row: Bluetooth.</summary>
    public required string CardBluetooth { get; init; }

    /// <summary>A lifted system card's source row: the network.</summary>
    public required string CardNetwork { get; init; }

    /// <summary>A lifted system card's source row: focus sessions.</summary>
    public required string CardFocus { get; init; }

    /// <summary>A lifted system card's source row: the clipboard.</summary>
    public required string CardClipboard { get; init; }

    /// <summary>A lifted system card's source row: CPU / memory load.</summary>
    public required string CardPerformance { get; init; }

    /// <summary>The state word of a low battery card.</summary>
    public required string CardLowBattery { get; init; }

    /// <summary>The state word of a busy CPU card.</summary>
    public required string CardCpuHigh { get; init; }

    /// <summary>The state word of a full memory card.</summary>
    public required string CardMemoryHigh { get; init; }

    /// <summary>A system card's button: opens its page in Windows Settings.</summary>
    public required string CardOpenSettings { get; init; }

    /// <summary>The load card's button: opens Task Manager.</summary>
    public required string CardTaskManager { get; init; }

    /// <summary>What was copied when it is an image.</summary>
    public required string CardImage { get; init; }

    /// <summary>Under a list cut at 200 files: how many more there are (the copy card, 10-11).</summary>
    public required string CardMoreFiles { get; init; }

    /// <summary>The tooltip of a copied file's ✕: it leaves the clipboard, the file isn't deleted.</summary>
    public required string CardRemoveFromClipboard { get; init; }

    /// <summary>A lifted calendar card's source row when its calendar has no name of its own (10-11).</summary>
    public required string CardCalendar { get; init; }

    /// <summary>Source names on the card's top row.</summary>
    public required string CardMusic { get; init; }
    public required string CardSystem { get; init; }
    public required string CardDownloads { get; init; }

    /// <summary>A download's source when several Chromium browsers are open and its temp file can't tell which (F2b).</summary>
    public required string CardBrowser { get; init; }
    public required string CardMicrophone { get; init; }
    public required string CardCamera { get; init; }

    // ---- the settings window (10-12) ----
    /// <summary>A button: pick a folder to add</summary>
    public required string AddFolder { get; init; }
    /// <summary>Settings → About: who made it</summary>
    public required string Author { get; init; }
    /// <summary>Settings → About: what it grew from</summary>
    public required string BasedOn { get; init; }
    /// <summary>The original project and its author</summary>
    public required string BasedOnValue { get; init; }

    // ---- the settings window (10-12) ----
    /// <summary>Settings window: the General page</summary>
    public required string NavGeneral { get; init; }
    /// <summary>Settings window: the About page</summary>
    public required string NavAbout { get; init; }
    /// <summary>Under <see cref="ConnectAgents"/></summary>
    public required string ConnectAgentsDesc { get; init; }
    /// <summary>Under <see cref="NotifyToasts"/></summary>
    public required string NotifyToastsDesc { get; init; }
    /// <summary>Under <see cref="HideToastBanners"/></summary>
    public required string HideToastBannersDesc { get; init; }
    /// <summary>Settings: the calendar page's add card</summary>
    public required string AddCalendar { get; init; }
    /// <summary>Under <see cref="AddCalendar"/></summary>
    public required string CalendarAddHint { get; init; }
    /// <summary>A button: add</summary>
    public required string Add { get; init; }
    /// <summary>Settings: heading over the calendars added</summary>
    public required string CalendarsAdded { get; init; }
    /// <summary>A button: remove</summary>
    public required string Remove { get; init; }
    /// <summary>A calendar not read yet</summary>
    public required string CalendarLoading { get; init; }
    /// <summary>A calendar read: {0} = when, {1} = how many events</summary>
    public required string CalendarSynced { get; init; }
    /// <summary>A calendar that couldn't be reached</summary>
    public required string CalendarOffline { get; init; }
    /// <summary>An address that gives no calendar</summary>
    public required string CalendarNotCalendar { get; init; }
    /// <summary>Under the Pomodoro card: {0} = focus rounds</summary>
    public required string PomodoroDesc { get; init; }
    /// <summary>Pomodoro's long break</summary>
    public required string LongBreak { get; init; }
    /// <summary>Settings: focus rounds before the long break</summary>
    public required string PomodoroRoundsLabel { get; init; }
    /// <summary>After a number box of minutes</summary>
    public required string MinutesUnit { get; init; }
    /// <summary>After the rounds number box</summary>
    public required string RoundsUnit { get; init; }
    /// <summary>Settings: quick timers</summary>
    public required string QuickTimers { get; init; }
    /// <summary>Under <see cref="QuickTimers"/></summary>
    public required string QuickTimersDesc { get; init; }
    /// <summary>A button: delete</summary>
    public required string Delete { get; init; }
    /// <summary>Settings: the version</summary>
    public required string Version { get; init; }
    /// <summary>Settings: third-party licences</summary>
    public required string Licenses { get; init; }
    /// <summary>Under <see cref="Licenses"/></summary>
    public required string LicensesDesc { get; init; }

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
}
