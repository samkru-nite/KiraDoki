using IsleBar.Core.Configuration;

namespace IsleBar.Core.SystemWatch;

/// <summary>
/// Which system notices to turn on. Extracted from settings (<see cref="IsleBarSettings"/>) and passed to the app's watcher bundle —
/// so the watchers do not need to know the settings file's shape.
/// </summary>
public sealed record SystemWatchOptions(
    bool Power = true,
    bool Bluetooth = true,
    bool Network = true,
    bool Focus = true,
    bool Clipboard = true,
    bool Load = true,
    bool Privacy = true,
    string Calendars = "",
    bool Toasts = false,
    bool HideBanners = false,
    bool WeChat = true,
    bool Drives = true,
    bool Transit = true,
    bool Claude = true,
    bool ChatGpt = true)
{
    /// <summary>Everything off.</summary>
    public static SystemWatchOptions None { get; } = new(false, false, false, false, false, false, false, "", Toasts: false, HideBanners: false,
        WeChat: false, Drives: false, Transit: false, Claude: false, ChatGpt: false);

    /// <summary>Calendar notices are on only when an address is set. <see cref="Calendars"/> = the addresses, one per line (a string keeps the record's equality).</summary>
    public bool Calendar => !string.IsNullOrWhiteSpace(Calendars);

    public static SystemWatchOptions From(IsleBarSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SystemWatchOptions(
            settings.NotifyPower,
            settings.NotifyBluetooth,
            settings.NotifyNetwork,
            settings.NotifyFocus,
            settings.NotifyClipboard,
            settings.NotifyLoad,
            settings.NotifyPrivacy,
            string.Join('\n', settings.Calendars.Select(c => c.Trim()).Where(c => c.Length > 0)),
            settings.NotifyToasts,
            settings.NotifyToasts && settings.HideToastBanners,   // hiding Windows' banners only makes sense while the bar shows them
            settings.NotifyWeChat,
            settings.ShowDrives,
            settings.ShowTransit,
            settings.ShowClaude,
            settings.ShowChatGpt);
    }
}
