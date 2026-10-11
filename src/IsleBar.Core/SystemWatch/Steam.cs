using System.Globalization;
using System.Text.RegularExpressions;

namespace IsleBar.Core.SystemWatch;

/// <summary>
/// A game's <c>steamapps\appmanifest_{id}.acf</c> (Valve's text "KeyValues" format): its name, its state flags and how far its
/// download is. Written by Steam itself; the format hasn't changed in years. EXPERIMENTAL (TODO 四, 10-11): written from the public
/// format, not yet seen on the user's PC — how often Steam rewrites the byte counts during a download isn't known.
/// </summary>
public sealed record SteamManifest(int AppId, string Name, long Flags, long ToDownload, long Downloaded, long ToStage, long Staged)
{
    public static SteamManifest? Parse(string text)
    {
        var values = SteamVdf.Values(text);
        if (!values.TryGetValue("appid", out var id) || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
        {
            return null;
        }

        return new SteamManifest(appId, values.GetValueOrDefault("name") ?? appId.ToString(CultureInfo.InvariantCulture),
            Long(values, "StateFlags"), Long(values, "BytesToDownload"), Long(values, "BytesDownloaded"),
            Long(values, "BytesToStage"), Long(values, "BytesStaged"));
    }

    private static long Long(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var v) && long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>How far: the download's bytes, or — nothing left to download (an update that only moves files) — the staging's.</summary>
    public (long Done, long Total)? Progress
        => ToDownload > 0 ? (Math.Min(Downloaded, ToDownload), ToDownload)
            : ToStage > 0 ? (Math.Min(Staged, ToStage), ToStage)
            : null;

    public bool Installed => (Flags & SteamFlags.FullyInstalled) != 0;

    /// <summary>Installed and nothing pending: what a finished download leaves.</summary>
    public bool Complete => Installed && (Flags & (SteamFlags.UpdateRequired | SteamFlags.UpdateStarted | SteamFlags.UpdatePaused | SteamFlags.UpdateRunning)) == 0;

    public bool Paused => (Flags & SteamFlags.UpdatePaused) != 0;
}

/// <summary>Steam's <c>EAppState</c> bits (StateFlags in the manifest).</summary>
public static class SteamFlags
{
    public const long UpdateRequired = 2;
    public const long FullyInstalled = 4;
    public const long UpdateRunning = 256;
    public const long UpdatePaused = 512;
    public const long UpdateStarted = 1024;
}

/// <summary>The few bits of Valve's KeyValues text read here: <c>"key"  "value"</c> pairs, at any depth.</summary>
public static partial class SteamVdf
{
    /// <summary>Every <c>"key" "value"</c> pair; the first of a key wins (the manifest's top keys come before its nested ones).</summary>
    public static Dictionary<string, string> Values(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Pair().Matches(text))
        {
            values.TryAdd(m.Groups["k"].Value, Unescape(m.Groups["v"].Value));
        }

        return values;
    }

    /// <summary><c>libraryfolders.vdf</c>: each library's folder ("path"), the main one included.</summary>
    public static List<string> Libraries(string text)
        => [.. Pair().Matches(text).Where(m => m.Groups["k"].Value.Equals("path", StringComparison.OrdinalIgnoreCase))
            .Select(m => Unescape(m.Groups["v"].Value))];

    private static string Unescape(string value) => value.Replace(@"\\", @"\", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal);

    [GeneratedRegex("\"(?<k>[^\"]+)\"[ \\t]+\"(?<v>(?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex Pair();
}

/// <summary>What a game's card says now.</summary>
public enum SteamPhase
{
    None,
    Downloading,
    Paused,
    Installed,
    Updated,
}

/// <summary>
/// One game's download, followed from Steam's own records (EXPERIMENTAL, TODO 四 10-11): Steam marks the game it's downloading in
/// the registry (<c>HKCU\Software\Valve\Steam\Apps\{id}\Updating</c> = 1) and keeps the game's state and bytes in its manifest.
/// Downloading = marked updating and not paused; paused = its manifest says so (shown only for a game seen downloading since
/// IsleBar started — Steam keeps paused downloads for days); done = it was followed and is now installed with nothing pending:
/// "已安装" when it wasn't installed before, "已更新" when it was. Anything else (uninstalled, cancelled, a state not known) ends the
/// card without a word.
/// </summary>
public sealed class SteamTally
{
    private bool _followed;
    private bool _wasInstalled;

    public SteamPhase See(SteamManifest? manifest, bool updating)
    {
        if (manifest is null)
        {
            _followed = false;   // uninstalled (its manifest went)
            return SteamPhase.None;
        }

        if (updating && !manifest.Paused)
        {
            if (!_followed)
            {
                _followed = true;
                _wasInstalled = manifest.Installed;
            }

            return SteamPhase.Downloading;
        }

        if (!_followed)
        {
            return SteamPhase.None;
        }

        if (manifest.Complete)
        {
            _followed = false;
            return _wasInstalled ? SteamPhase.Updated : SteamPhase.Installed;
        }

        if (manifest.Paused || (manifest.Flags & SteamFlags.UpdateStarted) != 0)
        {
            return SteamPhase.Paused;   // paused by hand, or put back in the queue
        }

        _followed = false;
        return SteamPhase.None;
    }
}
