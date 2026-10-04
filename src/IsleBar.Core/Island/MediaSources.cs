namespace IsleBar.Core.Island;

/// <summary>
/// Classifies the playback source shown on the music island. <b>Browser playback (YouTube etc.) is excluded</b> — music apps only
/// (user feedback 09-30: internet playback was showing up too; apply it to music apps only).
/// Windows reports the source as an app ID (e.g. <c>chrome.exe</c>, <c>Chrome</c>, <c>MSEdge</c>,
/// <c>SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify</c>). Music apps are too varied to allow-list,
/// so browsers are filtered by a list instead.
/// </summary>
public static class MediaSources
{
    private static readonly string[] BrowserNames =
    [
        "chrome", "msedge", "edge", "firefox", "opera", "brave", "whale", "vivaldi", "iexplore", "arc", "chromium", "waterfox",
    ];

    /// <summary>Whether the playback comes from a browser.</summary>
    public static bool IsBrowser(string? appId)
    {
        if (AppName(appId) is not { } name)
        {
            return false;
        }

        // short names (arc, edge) only on exact match — so unrelated apps like "archive.exe" are not mistaken for browsers
        return name.Contains("microsoftedge", StringComparison.Ordinal)
               || BrowserNames.Any(b => name == b || (b.Length >= 5 && name.StartsWith(b, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Whether the player <paramref name="appId"/> is the program <paramref name="processName"/> (e.g. the app in front):
    /// <c>MSEdge</c> ↔ <c>msedge</c>, <c>cloudmusic.exe</c> ↔ <c>cloudmusic</c>, <c>SpotifyAB.SpotifyMusic_…!Spotify</c> ↔ <c>Spotify</c>.
    /// </summary>
    public static bool IsApp(string? appId, string? processName)
        => AppName(appId) is { } app && AppName(processName) is { } process && app == process;

    /// <summary>
    /// The program's short name, lower case — accepts all of "C:\...\chrome.exe" · "chrome.exe" · "Chrome" ·
    /// "Microsoft.MicrosoftEdge_8wekyb3d8bbwe!MicrosoftEdge". Null if empty.
    /// </summary>
    private static string? AppName(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }

        var name = appId.Trim();
        var cut = name.LastIndexOfAny(['\\', '/', '!']);
        if (cut >= 0)
        {
            name = name[(cut + 1)..];
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Length == 0 ? null : name.ToLowerInvariant();
    }
}
