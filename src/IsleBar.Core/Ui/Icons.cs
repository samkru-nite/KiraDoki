namespace IsleBar.Core.Ui;

/// <summary>
/// The island's icons: characters of the bundled font "IsleBar Icons" (Assets/Fonts/IsleIcons.ttf), built from Microsoft's
/// Fluent UI System Icons, Regular (MIT) by scripts/make-icons.py — generated, edit the list there and run it again.
/// 16-size designs where Fluent has them, else the 20 one drawn smaller (user 10-11).
/// </summary>
public static class Icons
{
    /// <summary>Font family name inside IsleIcons.ttf (the app loads the file, see AppFonts.Icons).</summary>
    public const string Family = "IsleBar Icons";

    /// <summary>bell: an agent waits for you; a system notification (alert_16).</summary>
    public const string Alert = "\uE000";
    /// <summary>finished (checkmark_circle_16).</summary>
    public const string Done = "\uE001";
    /// <summary>failed / an error (dismiss_circle_16).</summary>
    public const string Error = "\uE002";
    /// <summary>download, receiving, an update (arrow_download_16).</summary>
    public const string Download = "\uE003";
    /// <summary>sending (arrow_upload_16).</summary>
    public const string Upload = "\uE004";
    /// <summary>copy (Explorer, clipboard) (copy_16).</summary>
    public const string Copy = "\uE005";
    /// <summary>move (Explorer) - the cut icon, user 10-10 (cut_16).</summary>
    public const string Move = "\uE006";
    /// <summary>extracting (folder_open_16).</summary>
    public const string Unzip = "\uE007";
    /// <summary>compressing (folder_zip_16).</summary>
    public const string Zip = "\uE008";
    /// <summary>paused (timer, music, a download) (pause_16).</summary>
    public const string Pause = "\uE009";
    /// <summary>timer (the small icon is redrawn so its hand turns) (timer_16).</summary>
    public const string Timer = "\uE00A";
    /// <summary>music (music_note_2_16).</summary>
    public const string Music = "\uE00B";
    /// <summary>an agent we can't name (chat_16).</summary>
    public const string Chat = "\uE00C";
    /// <summary>a notice with no icon of its own (info_16).</summary>
    public const string Info = "\uE00D";
    /// <summary>charging (battery_charge_20).</summary>
    public const string Charging = "\uE00E";
    /// <summary>Bluetooth (bluetooth_16).</summary>
    public const string Bluetooth = "\uE00F";
    /// <summary>headphones (headphones_16).</summary>
    public const string Headphones = "\uE010";
    /// <summary>Wi-Fi (wifi_1_20).</summary>
    public const string Wifi = "\uE011";
    /// <summary>no internet (globe_off_16).</summary>
    public const string Offline = "\uE012";
    /// <summary>focus session (weather_moon_16).</summary>
    public const string Focus = "\uE013";
    /// <summary>a copied picture (image_16).</summary>
    public const string Image = "\uE014";
    /// <summary>a file (document_16).</summary>
    public const string Document = "\uE015";
    /// <summary>CPU / memory busy (desktop_pulse_16).</summary>
    public const string Load = "\uE016";
    /// <summary>calendar (calendar_16).</summary>
    public const string Calendar = "\uE017";
    /// <summary>USB drive (usb_stick_20).</summary>
    public const string Drive = "\uE018";
    /// <summary>printer (print_16).</summary>
    public const string Print = "\uE019";
    /// <summary>microphone in use (mic_16).</summary>
    public const string Mic = "\uE01A";
    /// <summary>camera in use (camera_16).</summary>
    public const string Camera = "\uE01B";
    /// <summary>an agent waits until a time (clock_16).</summary>
    public const string Clock = "\uE01C";
    /// <summary>an agent's warning (warning_16).</summary>
    public const string Warning = "\uE01D";
    /// <summary>the transit station (user 10-11) (box_16).</summary>
    public const string Box = "\uE01E";
    /// <summary>several files in hand (user 10-11) (stack_16).</summary>
    public const string Stack = "\uE01F";
    /// <summary>button: open (arrow_up_right_12).</summary>
    public const string Open = "\uE020";
    /// <summary>button: into the transit station (add_12).</summary>
    public const string Add = "\uE021";
    /// <summary>button: remove (dismiss_12).</summary>
    public const string Dismiss = "\uE022";
    /// <summary>music: previous (previous_16).</summary>
    public const string Previous = "\uE023";
    /// <summary>music: play (play_16).</summary>
    public const string Play = "\uE024";
    /// <summary>music: next (next_16).</summary>
    public const string Next = "\uE025";
    /// <summary>a folder (folder_16).</summary>
    public const string Folder = "\uE026";
    /// <summary>music without a cover, drawn at 24 (music_note_2_24).</summary>
    public const string MusicLarge = "\uE027";
    /// <summary>a video export whose app icon can't be found (video_clip_16).</summary>
    public const string Video = "\uE028";
    /// <summary>battery 00% (battery_0_20).</summary>
    public const string Battery0 = "\uE029";
    /// <summary>battery 10% (battery_1_20).</summary>
    public const string Battery1 = "\uE02A";
    /// <summary>battery 20% (battery_2_20).</summary>
    public const string Battery2 = "\uE02B";
    /// <summary>battery 30% (battery_3_20).</summary>
    public const string Battery3 = "\uE02C";
    /// <summary>battery 40% (battery_4_20).</summary>
    public const string Battery4 = "\uE02D";
    /// <summary>battery 50% (battery_5_20).</summary>
    public const string Battery5 = "\uE02E";
    /// <summary>battery 60% (battery_6_20).</summary>
    public const string Battery6 = "\uE02F";
    /// <summary>battery 70% (battery_7_20).</summary>
    public const string Battery7 = "\uE030";
    /// <summary>battery 80% (battery_8_20).</summary>
    public const string Battery8 = "\uE031";
    /// <summary>battery 90% (battery_9_20).</summary>
    public const string Battery9 = "\uE032";
    /// <summary>battery 100% (battery_10_20).</summary>
    public const string Battery10 = "\uE033";
    /// <summary>settings: General (settings_16).</summary>
    public const string Settings = "\uE034";
    /// <summary>settings card: language (local_language_20).</summary>
    public const string CardLanguage = "\uE035";
    /// <summary>settings card: place on the taskbar (panel_bottom_20).</summary>
    public const string CardPosition = "\uE036";
    /// <summary>settings card: connect Claude Code (plug_connected_20).</summary>
    public const string CardConnect = "\uE037";
    /// <summary>settings card: download folders (folder_20).</summary>
    public const string CardDownloads = "\uE038";
    /// <summary>settings card: power (battery_charge_20).</summary>
    public const string CardCharging = "\uE039";
    /// <summary>settings card: Bluetooth (bluetooth_20).</summary>
    public const string CardBluetooth = "\uE03A";
    /// <summary>settings card: network (wifi_1_20).</summary>
    public const string CardWifi = "\uE03B";
    /// <summary>settings card: focus (weather_moon_20).</summary>
    public const string CardFocus = "\uE03C";
    /// <summary>settings card: clipboard (clipboard_paste_20).</summary>
    public const string CardClipboard = "\uE03D";
    /// <summary>settings card: CPU / memory (desktop_pulse_20).</summary>
    public const string CardLoad = "\uE03E";
    /// <summary>settings card: microphone and camera (mic_20).</summary>
    public const string CardMic = "\uE03F";
    /// <summary>settings card: Windows notifications (alert_20).</summary>
    public const string CardAlert = "\uE040";
    /// <summary>settings card: add a calendar (calendar_add_20).</summary>
    public const string CardCalendarAdd = "\uE041";
    /// <summary>settings card: a calendar (calendar_20).</summary>
    public const string CardCalendar = "\uE042";
    /// <summary>settings card: media in the browser (globe_20).</summary>
    public const string CardGlobe = "\uE043";
    /// <summary>settings card: bars follow the sound (sound_wave_circle_20).</summary>
    public const string CardSound = "\uE044";
    /// <summary>settings card: Pomodoro (timer_20).</summary>
    public const string CardTimer = "\uE045";
    /// <summary>settings card: quick timers (clock_alarm_20).</summary>
    public const string CardAlarm = "\uE046";
    /// <summary>settings card: quiet in full screen (speaker_mute_20).</summary>
    public const string CardMute = "\uE047";
    /// <summary>settings card: version (info_20).</summary>
    public const string CardInfo = "\uE048";
    /// <summary>settings card: diagnostics (stethoscope_20).</summary>
    public const string CardDiagnose = "\uE049";
    /// <summary>settings card: third-party licences (certificate_20).</summary>
    public const string CardLicense = "\uE04A";
    /// <summary>settings card: authors (people_20).</summary>
    public const string CardPeople = "\uE04B";
    /// <summary>settings card: based on IsleBar (branch_fork_20).</summary>
    public const string CardFork = "\uE04C";
}
