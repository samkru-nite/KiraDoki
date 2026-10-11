using System.Diagnostics;
using System.Globalization;
using IsleBar.Core.Island;
using IsleBar.Core.SystemWatch;
using IsleBar.Core.Ui;
using Microsoft.Win32;

namespace IsleBar.App.Media;

/// <summary>
/// Steam's game downloads on the island (TODO 四, 10-11). EXPERIMENTAL: written from Steam's public file formats, not yet seen on
/// the user's PC (Steam wouldn't log in through the VPN) — the log ("steam: …") keeps what was read, to check it later.
/// <para>
/// Read from Steam's own records, the steadiest source (TODO 一): the registry marks the game being downloaded
/// (<c>HKCU\Software\Valve\Steam\Apps\{id}\Updating</c>), its manifest (<see cref="SteamManifest"/>) has its name, state and bytes →
/// "下载中 {game}" with the real percentage, "已暂停 {game}", then "已安装 / 已更新 {game}" (<see cref="SteamTally"/>). One card per game
/// ("steam:{id}"), Steam's own icon in every stage. The fallback (TODO 一: reading another program's records may break with an
/// update): Steam writing a lot with no game marked → "下载中 Steam", a flowing bar, no ending.
/// </para>
/// Idle cost: one process lookup a second (nothing else while Steam isn't running). Runs on the download watcher's thread.
/// </summary>
internal sealed class SteamWatcher
{
    private const string App = "steam.exe";
    private const string DownloadGlyph = "";   // download
    private const string AppsKey = @"Software\Valve\Steam\Apps";
    private const long BusyBytes = 1024 * 1024;   // a second
    private static readonly TimeSpan BusyLinger = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LibrariesEvery = TimeSpan.FromSeconds(30);

    private readonly Func<Core.Localization.LanguageStrings> _text;
    private readonly Interop.ProcessWriteMeter _written = new("steam");
    private readonly RateMeter _rate = new();
    private readonly Dictionary<int, Game> _games = [];
    private readonly List<ActivityState> _finished = [];

    private List<string> _libraries = [];
    private DateTimeOffset _librariesAt;

    private sealed class Game(DateTimeOffset since)
    {
        public DateTimeOffset Since { get; set; } = since;
        public SteamTally Tally { get; } = new();
        public string? Path { get; set; }
        public SteadyEta Eta { get; } = new();
        public string Said { get; set; } = string.Empty;   // last logged, to log changes only
        public DateTimeOffset SaidAt { get; set; }
    }

    // the fallback
    private int _busyStreak;
    private DateTimeOffset? _busySince, _busyLast;

    public SteamWatcher(Func<Core.Localization.LanguageStrings> text) => _text = text;

    public List<ActivityState> Scan(DateTimeOffset now)
    {
        var result = new List<ActivityState>();
        if (!Running())
        {
            if (_games.Count > 0 || _busySince is not null)
            {
                Interop.TaskbarHost.Log("steam: Steam closed — its cards go");
            }

            _games.Clear();
            _busySince = _busyLast = null;
            _busyStreak = 0;
            _written.Reset();
        }
        else
        {
            var written = _written.Delta(now);
            _rate.Add(now, written);
            var updating = Updating();
            foreach (var id in updating.Where(id => !_games.ContainsKey(id)))
            {
                _games[id] = new Game(now);
            }

            var downloading = false;
            foreach (var (id, game) in _games.ToList())
            {
                var manifest = Read(id, game);
                var phase = game.Tally.See(manifest, updating.Contains(id));
                Log(id, game, manifest, phase, updating.Contains(id));
                switch (phase)
                {
                    case SteamPhase.Downloading or SteamPhase.Paused:
                        downloading |= phase == SteamPhase.Downloading;
                        result.Add(Card(id, manifest!, phase, now, game));
                        break;
                    case SteamPhase.Installed or SteamPhase.Updated:
                        _finished.RemoveAll(f => f.SourcePath == Key(id));
                        _finished.Add(Done(id, manifest!.Name, phase, now, game.Since));
                        _games.Remove(id);
                        break;
                    default:
                        _games.Remove(id);
                        break;
                }
            }

            if (Fallback(now, written, downloading, updating) is { } busy)
            {
                result.Add(busy);
            }
        }

        _finished.RemoveAll(f => now - f.UpdatedAt > ActivityStore.DoneLinger);
        result.AddRange(_finished);
        return result;
    }

    private static string Key(int id) => "steam:" + id.ToString(CultureInfo.InvariantCulture);

    private ActivityState Card(int id, SteamManifest manifest, SteamPhase phase, DateTimeOffset now, Game game)
    {
        var text = _text();
        var progress = manifest.Progress;
        var speed = phase == SteamPhase.Downloading ? _rate.Speed(now) : null;   // what Steam writes to disk
        return new ActivityState
        {
            AppId = App,
            Tool = App,
            AppName = "Steam",
            RawKind = ActivityState.KindTransfer,
            Title = "Files",
            Name = manifest.Name,
            // "已下载 4.1 GB，共 11.2 GB" (user 10-11)
            Note = progress is { } p ? text.CardBytesOf.Replace("{a}", CardSheets.Bytes(p.Done), StringComparison.Ordinal)
                .Replace("{b}", CardSheets.Bytes(p.Total), StringComparison.Ordinal) : null,
            Eta = game.Eta.See(progress is { } q ? SteadyEta.From(q.Total - q.Done, speed) : null, now),
            Words = phase == SteamPhase.Paused ? text.Paused : text.Downloading,
            Glyph = DownloadGlyph,
            Stage = "copy",   // paused: the bar just stands, like WinRAR's
            Done = progress?.Done,
            Total = progress?.Total,
            Speed = speed,
            State = "run",
            SourcePath = Key(id),
            UpdatedAt = game.Since,
        };
    }

    private ActivityState Done(int id, string name, SteamPhase phase, DateTimeOffset now, DateTimeOffset since)
    {
        var text = _text();
        return new ActivityState
        {
            AppId = App,
            Tool = App,
            AppName = "Steam",
            Took = now - since,
            RawKind = ActivityState.KindTransfer,
            Title = "Files",
            Name = name,
            Words = phase == SteamPhase.Installed ? text.GameInstalled : text.GameUpdated,
            Stage = "copy",
            State = "done",
            SourcePath = Key(id),   // the same card as the one in progress: it rises from its cell
            UpdatedAt = now,
        };
    }

    /// <summary>Steam writing a lot (two busy seconds in a row) while no game we read is downloading, till it has been quiet a moment.</summary>
    private ActivityState? Fallback(DateTimeOffset now, long written, bool downloading, HashSet<int> updating)
    {
        _busyStreak = written >= BusyBytes ? _busyStreak + 1 : 0;
        if (downloading || (_busySince is null && _busyStreak < 2)
            || (written < BusyBytes && (_busyLast is null || now - _busyLast > BusyLinger)))
        {
            if (_busySince is not null)
            {
                Interop.TaskbarHost.Log("steam: busy without a game we read — over");
            }

            _busySince = _busyLast = null;
            return null;
        }

        if (written >= BusyBytes)
        {
            _busyLast = now;
        }

        if (_busySince is null)
        {
            _busySince = now;
            // what was there to read, to fix the reading next time
            Interop.TaskbarHost.Log($"steam: busy ({written / 1024} KB/s) without a game we read — fallback card; updating in the registry: [{string.Join(", ", updating)}], libraries: [{string.Join(" | ", _libraries)}]");
        }

        return new ActivityState
        {
            AppId = App,
            Tool = App,
            AppName = "Steam",
            Speed = written > 0 ? written : null,
            RawKind = ActivityState.KindTransfer,
            Title = "Files",
            Name = "Steam",
            Words = _text().Downloading,
            Glyph = DownloadGlyph,
            Stage = "copy",
            State = "run",   // no percentage: the bar flows
            SourcePath = "steam:busy",
            UpdatedAt = _busySince.Value,
        };
    }

    private void Log(int id, Game game, SteamManifest? m, SteamPhase phase, bool updating)
    {
        // the bytes rounded to MB: how often Steam rewrites its manifest during a download is what's to be learnt (TODO 四)
        var said = m is null
            ? $"no manifest (looked in {_libraries.Count} libraries)"
            : $"\"{m.Name}\" flags {m.Flags} download {m.Downloaded >> 20}/{m.ToDownload >> 20} MB stage {m.Staged >> 20}/{m.ToStage >> 20} MB";
        var line = $"{said}, updating {updating} → {phase}";
        var state = $"{m?.Flags} {updating} {phase}";   // a change of state at once, the bytes every 30 s
        if (state != game.Said || DateTimeOffset.UtcNow - game.SaidAt > TimeSpan.FromSeconds(30))
        {
            game.Said = state;
            game.SaidAt = DateTimeOffset.UtcNow;
            var age = game.Path is { } p && File.Exists(p) ? $" (manifest written {(DateTime.Now - File.GetLastWriteTime(p)).TotalSeconds:0} s ago)" : string.Empty;
            Interop.TaskbarHost.Log($"steam: {id} {line}{age}");
        }
    }

    private static bool Running()
    {
        var found = Process.GetProcessesByName("steam");
        foreach (var p in found)
        {
            p.Dispose();
        }

        return found.Length > 0;
    }

    /// <summary>The games Steam marks as downloading / updating now.</summary>
    private static HashSet<int> Updating()
    {
        var ids = new HashSet<int>();
        try
        {
            using var apps = Registry.CurrentUser.OpenSubKey(AppsKey);
            if (apps is null)
            {
                return ids;
            }

            foreach (var name in apps.GetSubKeyNames())
            {
                if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                {
                    using var app = apps.OpenSubKey(name);
                    if (app?.GetValue("Updating") is int updating && updating != 0)
                    {
                        ids.Add(id);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
        }

        return ids;
    }

    /// <summary>A game's manifest, from whichever library holds it (the place is remembered).</summary>
    private SteamManifest? Read(int id, Game game)
    {
        if (game.Path is null || !File.Exists(game.Path))
        {
            game.Path = Libraries().Select(l => Path.Combine(l, "steamapps", $"appmanifest_{id}.acf")).FirstOrDefault(File.Exists);
        }

        if (game.Path is null)
        {
            return null;
        }

        try
        {
            // Steam may be writing it: shared, and a half-written one reads as nothing this second
            using var stream = new FileStream(game.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return SteamManifest.Parse(reader.ReadToEnd()) is { } m && m.AppId == id ? m : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Steam's libraries: its own folder and the ones in libraryfolders.vdf (looked at again every 30 s).</summary>
    private List<string> Libraries()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _librariesAt < LibrariesEvery)
        {
            return _libraries;
        }

        _librariesAt = now;
        var found = new List<string>();
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam && steam.Length > 0)
        {
            steam = Path.GetFullPath(steam);
            found.Add(steam);
            foreach (var vdf in new[] { Path.Combine(steam, "steamapps", "libraryfolders.vdf"), Path.Combine(steam, "config", "libraryfolders.vdf") })
            {
                try
                {
                    if (File.Exists(vdf))
                    {
                        found.AddRange(SteamVdf.Libraries(File.ReadAllText(vdf)));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        _libraries = [.. found.Select(f => f.TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase)];
        return _libraries;
    }
}
