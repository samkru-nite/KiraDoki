"""Builds the island's icon font from Microsoft's Fluent UI System Icons (MIT).

Only the icons listed below go in, each at its own code point (U+E000 on), so the app ships a few KB instead of the
whole 2.8 MB font. Writes:
  src/IsleBar.App/Assets/Fonts/IsleIcons.ttf   (family "IsleBar Icons")
  src/IsleBar.Core/Ui/Icons.cs                 (one constant per icon)

Usage (needs `pip install fonttools`):
  python scripts/make-icons.py <folder with FluentSystemIcons-Regular.ttf and FluentSystemIcons-Regular.json>
The font and its code point map are in github.com/microsoft/fluentui-system-icons, folder `fonts`.
To add an icon: add a line below (name as in the .json, without ic_fluent_ / _regular), run again, rebuild.
"""
import json, os, sys
from fontTools.ttLib import TTFont
from fontTools import subset

# (constant, Fluent icon, what it is on the island). Order = code point; append new ones at the end.
ICONS = [
    ("Alert", "alert_16", "bell: an agent waits for you; a system notification"),
    ("Done", "checkmark_circle_16", "finished"),
    ("Error", "dismiss_circle_16", "failed / an error"),
    ("Download", "arrow_download_16", "download, receiving, an update"),
    ("Upload", "arrow_upload_16", "sending"),
    ("Copy", "copy_16", "copy (Explorer, clipboard)"),
    ("Move", "cut_16", "move (Explorer) - the cut icon, user 10-10"),
    ("Unzip", "folder_open_16", "extracting"),
    ("Zip", "folder_zip_16", "compressing"),
    ("Pause", "pause_16", "paused (timer, music, a download)"),
    ("Timer", "timer_16", "timer (the small icon is redrawn so its hand turns)"),
    ("Music", "music_note_2_16", "music"),
    ("Chat", "chat_16", "an agent we can't name"),
    ("Info", "info_16", "a notice with no icon of its own"),
    ("Charging", "battery_charge_20", "charging"),
    ("Bluetooth", "bluetooth_16", "Bluetooth"),
    ("Headphones", "headphones_16", "headphones"),
    ("Wifi", "wifi_1_20", "Wi-Fi"),
    ("Offline", "globe_off_16", "no internet"),
    ("Focus", "weather_moon_16", "focus session"),
    ("Image", "image_16", "a copied picture"),
    ("Document", "document_16", "a file"),
    ("Load", "desktop_pulse_16", "CPU / memory busy"),
    ("Calendar", "calendar_16", "calendar"),
    ("Drive", "usb_stick_20", "USB drive"),
    ("Print", "print_16", "printer"),
    ("Mic", "mic_16", "microphone in use"),
    ("Camera", "camera_16", "camera in use"),
    ("Clock", "clock_16", "an agent waits until a time"),
    ("Warning", "warning_16", "an agent's warning"),
    ("Box", "box_16", "the transit station (user 10-11)"),
    ("Stack", "stack_16", "several files in hand (user 10-11)"),
    ("Open", "arrow_up_right_12", "button: open"),
    ("Add", "add_12", "button: into the transit station"),
    ("Dismiss", "dismiss_12", "button: remove"),
    ("Previous", "previous_16", "music: previous"),
    ("Play", "play_16", "music: play"),
    ("Next", "next_16", "music: next"),
    ("Folder", "folder_16", "a folder"),
    ("MusicLarge", "music_note_2_24", "music without a cover, drawn at 24"),
    ("Video", "video_clip_16", "a video export whose app icon can't be found"),
] + [("Battery%d" % n, "battery_%d_20" % n, "battery %d0%%" % n) for n in range(11)] + [
    # the settings window (10-12): 16 in the left column, 20 on the cards (as Windows' own Settings)
    ("Settings", "settings_16", "settings: General"),
    ("CardLanguage", "local_language_20", "settings card: language"),
    ("CardPosition", "panel_bottom_20", "settings card: place on the taskbar"),
    ("CardConnect", "plug_connected_20", "settings card: connect Claude Code"),
    ("CardDownloads", "folder_20", "settings card: download folders"),
    ("CardCharging", "battery_charge_20", "settings card: power"),
    ("CardBluetooth", "bluetooth_20", "settings card: Bluetooth"),
    ("CardWifi", "wifi_1_20", "settings card: network"),
    ("CardFocus", "weather_moon_20", "settings card: focus"),
    ("CardClipboard", "clipboard_paste_20", "settings card: clipboard"),
    ("CardLoad", "desktop_pulse_20", "settings card: CPU / memory"),
    ("CardMic", "mic_20", "settings card: microphone and camera"),
    ("CardAlert", "alert_20", "settings card: Windows notifications"),
    ("CardCalendarAdd", "calendar_add_20", "settings card: add a calendar"),
    ("CardCalendar", "calendar_20", "settings card: a calendar"),
    ("CardGlobe", "globe_20", "settings card: media in the browser"),
    ("CardSound", "sound_wave_circle_20", "settings card: bars follow the sound"),
    ("CardTimer", "timer_20", "settings card: Pomodoro"),
    ("CardAlarm", "clock_alarm_20", "settings card: quick timers"),
    ("CardMute", "speaker_mute_20", "settings card: quiet in full screen"),
    ("CardInfo", "info_20", "settings card: version"),
    ("CardDiagnose", "stethoscope_20", "settings card: diagnostics"),
    ("CardLicense", "certificate_20", "settings card: third-party licences"),
    ("CardPeople", "people_20", "settings card: authors"),
    ("CardFork", "branch_fork_20", "settings card: based on IsleBar"),
    ("CardWeChat", "chat_20", "settings card: WeChat"),
    ("CardBrowserDownload", "arrow_download_20", "settings card: browser downloads"),
    ("CardDownloader", "cloud_arrow_down_20", "settings card: download programs"),
    ("CardSteam", "games_20", "settings card: Steam"),
    ("CardFileOps", "copy_20", "settings card: copy and move"),
    ("CardArchive", "folder_zip_20", "settings card: archivers"),
    ("CardExport", "video_clip_20", "settings card: video export"),
    ("CardPrinter", "print_20", "settings card: printer"),
    ("CardUsb", "usb_stick_20", "settings card: USB drives"),
    ("CardTransit", "box_20", "settings card: transit station"),
    ("CardClaude", "sparkle_20", "settings card: Claude"),
    ("CardChatGpt", "bot_sparkle_20", "settings card: ChatGPT"),
    ("CardLid", "laptop_20", "settings card: keep the AI working with the lid closed"),
]

FAMILY = "IsleBar Icons"
FIRST = 0xE000

def main(src):
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    codes = json.load(open(os.path.join(src, "FluentSystemIcons-Regular.json")))
    font = TTFont(os.path.join(src, "FluentSystemIcons-Regular.ttf"))
    cmap = font.getBestCmap()
    names = [cmap[codes["ic_fluent_%s_regular" % fluent]] for _, fluent, _ in ICONS]

    opts = subset.Options()
    opts.glyph_names = True
    opts.notdef_outline = True
    opts.name_IDs = ["*"]
    opts.layout_features = []
    sub = subset.Subsetter(opts)
    sub.populate(glyphs=names)
    sub.subset(font)

    mapping = {FIRST + i: g for i, g in enumerate(names)}
    for table in font["cmap"].tables:
        table.cmap = dict(mapping)
    font["cmap"].tables = [t for t in font["cmap"].tables if t.format in (4, 12)]

    # Fluent's file says every glyph starts at x 0 (left side bearing 0, a stale box); once the box is recalculated a renderer puts
    # the ink's left edge at the bearing, which moved every icon left by its margin (1.5 px of 16 for the timer, measured 10-11).
    # Bearing = the real left edge keeps each icon where its designer put it.
    glyf, hmtx = font["glyf"], font["hmtx"]
    for g in font.getGlyphOrder():
        glyph = glyf[g]
        glyph.recalcBounds(glyf)
        width, _ = hmtx[g]
        hmtx[g] = (width, getattr(glyph, "xMin", 0) if glyph.numberOfContours else 0)

    # The glyphs sit on a 500-unit square from the baseline up; say so, like Segoe Fluent Icons (ascent = em, no descent),
    # so a FontIcon puts them where the old icons were.
    em = font["head"].unitsPerEm
    os2, hhea = font["OS/2"], font["hhea"]
    os2.usWinAscent, os2.usWinDescent = em, 0
    os2.sTypoAscender, os2.sTypoDescender, os2.sTypoLineGap = em, 0, 0
    hhea.ascent, hhea.descent, hhea.lineGap = em, 0, 0

    name = font["name"]
    name.names = []
    for nid, text in [(0, "Icons: Copyright (c) 2020 Microsoft Corporation, MIT License (Fluent UI System Icons)"),
                      (1, FAMILY), (2, "Regular"), (3, FAMILY + " Regular"), (4, FAMILY), (5, "Version 1.0"),
                      (6, "IsleBarIcons-Regular")]:
        name.setName(text, nid, 3, 1, 0x409)
        name.setName(text, nid, 1, 0, 0)

    out = os.path.join(root, "src", "IsleBar.App", "Assets", "Fonts", "IsleIcons.ttf")
    font.save(out)

    lines = [
        "namespace IsleBar.Core.Ui;",
        "",
        "/// <summary>",
        "/// The island's icons: characters of the bundled font \"IsleBar Icons\" (Assets/Fonts/IsleIcons.ttf), built from Microsoft's",
        "/// Fluent UI System Icons, Regular (MIT) by scripts/make-icons.py \u2014 generated, edit the list there and run it again.",
        "/// 16-size designs where Fluent has them, else the 20 one drawn smaller (user 10-11).",
        "/// </summary>",
        "public static class Icons",
        "{",
        "    /// <summary>Font family name inside IsleIcons.ttf (the app loads the file, see AppFonts.Icons).</summary>",
        "    public const string Family = \"%s\";" % FAMILY,
        "",
    ]
    for i, (const, fluent, what) in enumerate(ICONS):
        lines.append("    /// <summary>%s (%s).</summary>" % (what.replace("&", "and"), fluent))
        lines.append("    public const string %s = \"\\u%04X\";" % (const, FIRST + i))
    lines += ["}", ""]
    cs = os.path.join(root, "src", "IsleBar.Core", "Ui", "Icons.cs")
    with open(cs, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print("%d icons, %d bytes -> %s" % (len(ICONS), os.path.getsize(out), out))

if __name__ == "__main__":
    main(sys.argv[1])
