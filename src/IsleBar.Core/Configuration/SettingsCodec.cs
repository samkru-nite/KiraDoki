using System.Text.Json;
using System.Text.Json.Nodes;
using IsleBar.Core.Localization;

namespace IsleBar.Core.Configuration;

/// <summary>
/// JSON ↔ <see cref="IsleBarSettings"/>. Shape checks are <b>per key</b> — if one key is bad,
/// the rest are kept and only the bad key is reset to default.
/// </summary>
public static class SettingsCodec
{
    /// <summary>Keys of removed features, dropped from the file on save: the real search box switch (10-05); file search,
    /// the Tab order and question history; launching agents — which agent, its options, working folder, model lists, the very
    /// old "flags" (10-10).</summary>
    private static readonly string[] RemovedKeys =
        ["native_searchbox", "files", "file_results", "file_sort", "file_exclude", "mode_order", "modes_off", "history",
         "agent", "values", "codex_values", "folder", "recent_folders", "models", "codex_models", "models_checked", "flags"];

    // JSON key names (same as the Python version's claude_bar.json)
    private const string KeyLang = "lang";
    private const string KeyDownloadFolders = "download_folders";
    private const string KeyPomoFocus = "pomo_focus";
    private const string KeyPomoBreak = "pomo_break";
    private const string KeyPomoLong = "pomo_long";
    private const string KeyQuickTimers = "quick_timers";
    private const string KeyPosition = "position";
    private const string KeyManualOffset = "manual_offset";
    private const string KeyPillCorners = "pill_corners";
    private const string KeyBallSide = "ball_side";
    private const string KeySwapAnimation = "swap_animation";
    private const string KeySwapBounce = "swap_bounce";
    private const string KeyTransparency = "transparency";
    private const string KeyLiftAnimation = "lift_animation";
    private const string KeyListStagger = "list_stagger";
    private const string KeyCardTextAlign = "card_text_align";
    private const string KeyAudioBars = "audio_bars";
    private const string KeyNotifyPower = "notify_power";
    private const string KeyNotifyBluetooth = "notify_bluetooth";
    private const string KeyNotifyNetwork = "notify_network";
    private const string KeyNotifyFocus = "notify_focus";
    private const string KeyNotifyClipboard = "notify_clipboard";
    private const string KeyNotifyLoad = "notify_load";
    private const string KeyNotifyPrivacy = "notify_privacy";
    private const string KeyNotifyToasts = "notify_toasts";
    private const string KeyHideToastBanners = "hide_toast_banners";
    private const string KeyBrowserMedia = "browser_media";
    private const string KeyMuteChimeFullscreen = "mute_chime_fullscreen";
    private const string KeyCalendarIcs = "calendar_ics";
    private const string KeyDropCommand = "drop_command";
    private const string KeyStyleGlass = "style_glass";

    public static IsleBarSettings FromJson(JsonObject? raw)
    {
        var s = new IsleBarSettings();
        if (raw is null)
        {
            return s;
        }

        if (AsString(raw[KeyLang]) is { } lang && (lang == "auto" || LanguageCatalog.IsKnown(lang)))
        {
            s.Lang = lang;
        }

        if (raw[KeyDownloadFolders] is JsonArray downloadArr)
        {
            s.DownloadFolders = [.. downloadArr.Select(AsString).OfType<string>().Where(line => line.Trim().Length > 0)];
        }

        s.NotifyPower = AsBool(raw[KeyNotifyPower]) ?? s.NotifyPower;
        s.NotifyBluetooth = AsBool(raw[KeyNotifyBluetooth]) ?? s.NotifyBluetooth;
        s.NotifyNetwork = AsBool(raw[KeyNotifyNetwork]) ?? s.NotifyNetwork;
        s.NotifyFocus = AsBool(raw[KeyNotifyFocus]) ?? s.NotifyFocus;
        s.NotifyClipboard = AsBool(raw[KeyNotifyClipboard]) ?? s.NotifyClipboard;
        s.NotifyLoad = AsBool(raw[KeyNotifyLoad]) ?? s.NotifyLoad;
        s.NotifyPrivacy = AsBool(raw[KeyNotifyPrivacy]) ?? s.NotifyPrivacy;
        s.NotifyToasts = AsBool(raw[KeyNotifyToasts]) ?? s.NotifyToasts;
        s.HideToastBanners = AsBool(raw[KeyHideToastBanners]) ?? s.HideToastBanners;
        s.BrowserMedia = AsBool(raw[KeyBrowserMedia]) ?? s.BrowserMedia;
        s.MuteChimeFullscreen = AsBool(raw[KeyMuteChimeFullscreen]) ?? s.MuteChimeFullscreen;
        if (AsString(raw[KeyCalendarIcs]) is { } ics)
        {
            s.CalendarIcs = ics.Trim();
        }

        if (AsString(raw[KeyDropCommand]) is { } drop)
        {
            s.DropCommand = drop.Trim();
        }

        s.StyleGlass = AsBool(raw[KeyStyleGlass]) ?? s.StyleGlass;

        s.Position = Ui.BarSlot.Key(Ui.BarSlot.Parse(AsString(raw[KeyPosition]) ?? s.Position));
        s.PillCorners = AsString(raw[KeyPillCorners]) == "small" ? "small" : "round";
        s.BallSide = AsString(raw[KeyBallSide]) == "left" ? "left" : "right";
        s.SwapAnimation = Ui.SwapMotion.Key(Ui.SwapMotion.Parse(AsString(raw[KeySwapAnimation])));
        s.Bounce = AsBool(raw[KeySwapBounce]) ?? s.Bounce;
        s.Transparency = AsBool(raw[KeyTransparency]) ?? s.Transparency;
        s.LiftAnimation = Ui.LiftMotion.Key(Ui.LiftMotion.Parse(AsString(raw[KeyLiftAnimation])));
        s.ListStagger = AsString(raw[KeyListStagger]) == "40" ? "40" : "20";
        s.CardTextAlign = AsString(raw[KeyCardTextAlign]) is "group" or "text" ? AsString(raw[KeyCardTextAlign])! : "left";
        if (AsInt(raw[KeyManualOffset]) is { } manualOffset)
        {
            s.ManualOffset = Math.Clamp(manualOffset, 0, 10000);
        }

        s.AudioBars = AsBool(raw[KeyAudioBars]) ?? s.AudioBars;
        s.PomodoroFocus = Minutes(raw[KeyPomoFocus]) ?? s.PomodoroFocus;
        s.PomodoroBreak = Minutes(raw[KeyPomoBreak]) ?? s.PomodoroBreak;
        s.PomodoroLong = Minutes(raw[KeyPomoLong]) ?? s.PomodoroLong;
        if (raw[KeyQuickTimers] is JsonArray quickTimerArr)
        {
            s.QuickTimers = [.. quickTimerArr.Select(Minutes).OfType<int>().Distinct().Take(6)];
        }

        return s;
    }

    /// <summary>Overwrites only known keys. Unknown keys are left untouched and thus preserved.</summary>
    public static void Apply(IsleBarSettings s, JsonObject raw)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(raw);

        raw[KeyLang] = s.Lang;
        foreach (var gone in RemovedKeys)
        {
            raw.Remove(gone);
        }

        if (s.DownloadFolders.Count == 0)
        {
            raw.Remove(KeyDownloadFolders);
        }
        else
        {
            raw[KeyDownloadFolders] = new JsonArray([.. s.DownloadFolders.Select(line => (JsonNode)line)]);
        }
        raw[KeyPosition] = s.Position;
        raw[KeyManualOffset] = s.ManualOffset;
        raw[KeyPillCorners] = s.PillCorners;
        raw[KeyBallSide] = s.BallSide;
        raw[KeySwapAnimation] = s.SwapAnimation;
        raw[KeySwapBounce] = s.Bounce;
        raw[KeyTransparency] = s.Transparency;
        raw[KeyLiftAnimation] = s.LiftAnimation;
        raw[KeyListStagger] = s.ListStagger;
        raw[KeyCardTextAlign] = s.CardTextAlign;
        raw[KeyAudioBars] = s.AudioBars;
        raw[KeyPomoFocus] = s.PomodoroFocus;
        raw[KeyPomoBreak] = s.PomodoroBreak;
        raw[KeyPomoLong] = s.PomodoroLong;
        raw[KeyQuickTimers] = new JsonArray([.. s.QuickTimers.Select(m => (JsonNode)m)]);
        raw[KeyNotifyPower] = s.NotifyPower;
        raw[KeyNotifyBluetooth] = s.NotifyBluetooth;
        raw[KeyNotifyNetwork] = s.NotifyNetwork;
        raw[KeyNotifyFocus] = s.NotifyFocus;
        raw[KeyNotifyClipboard] = s.NotifyClipboard;
        raw[KeyNotifyLoad] = s.NotifyLoad;
        raw[KeyNotifyPrivacy] = s.NotifyPrivacy;
        raw[KeyNotifyToasts] = s.NotifyToasts;
        raw[KeyHideToastBanners] = s.HideToastBanners;
        raw[KeyBrowserMedia] = s.BrowserMedia;
        raw[KeyMuteChimeFullscreen] = s.MuteChimeFullscreen;
        raw[KeyCalendarIcs] = s.CalendarIcs;
        raw[KeyDropCommand] = s.DropCommand;
        raw[KeyStyleGlass] = s.StyleGlass;
        raw.Remove("style_black");     // removed setting (09-30)
        raw.Remove("advanced");        // removed setting (10-01)
        raw.Remove("drop_transfer");   // removed setting (10-01, the attach/transfer toggle — behaviour now decided by whether drop_command is set)
    }

    // ---- Return a value only when the type matches exactly. "true" (a string) or 1 is not a bool. ----

    private static string? AsString(JsonNode? node)
        => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool? AsBool(JsonNode? node)
        => node is JsonValue v && v.GetValueKind() == JsonValueKind.True ? true
            : node is JsonValue v2 && v2.GetValueKind() == JsonValueKind.False ? false
            : null;

    /// <summary>A whole number, or null if missing or not one.</summary>
    private static int? AsInt(JsonNode? node)
    {
        try
        {
            return node?.GetValue<int>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Minute value (1–600). Null if not a number or out of range.</summary>
    private static int? Minutes(JsonNode? node)
    {
        try
        {
            var value = node?.GetValue<int>();
            return value is >= 1 and <= 600 ? value : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
