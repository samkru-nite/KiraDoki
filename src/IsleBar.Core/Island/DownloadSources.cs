namespace IsleBar.Core.Island;

/// <summary>
/// Which program a download's temp file belongs to, from its extension (TODO 7.9 F, 10-08): <c>.baiduyun.p.downloading</c> Baidu
/// Netdisk, <c>.part</c> Firefox, <c>.opdownload</c> Opera. <c>.crdownload</c> is every Chromium browser's (Chrome, Edge, …), so it is
/// the one Chromium browser running — with several open (or none found) it can't be told: null, shown as "browser".
/// </summary>
public static class DownloadSources
{
    /// <summary>Chromium browsers by process name, and the name shown.</summary>
    public static readonly IReadOnlyList<(string Process, string Name)> Chromium =
    [
        ("msedge", "Edge"),
        ("chrome", "Chrome"),
        ("brave", "Brave"),
        ("vivaldi", "Vivaldi"),
        ("whale", "Whale"),
        ("opera", "Opera"),
        ("360se", "360"),
        ("QQBrowser", "QQ 浏览器"),
    ];

    /// <param name="runningChromium">Names (from <see cref="Chromium"/>) of the Chromium browsers running now.</param>
    public static string? Of(string path, IReadOnlyCollection<string> runningChromium)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(runningChromium);
        return path switch
        {
            _ when path.EndsWith(".baiduyun.p.downloading", StringComparison.OrdinalIgnoreCase) => "百度网盘",
            _ when path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) => "Firefox",
            _ when path.EndsWith(".opdownload", StringComparison.OrdinalIgnoreCase) => "Opera",
            _ when path.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) && runningChromium.Count == 1 => runningChromium.First(),
            _ => null,
        };
    }
}
