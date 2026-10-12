using System.Text.RegularExpressions;
using IsleBar.App.Interop;
using IsleBar.Core.Island;

namespace IsleBar.App.Media;

/// <summary>
/// Puts downloads on the island (decision card #3: "download progress — extend to browser downloads").
/// While downloading, browsers append <c>.crdownload</c> (Chrome, Edge, Whale, etc.) or <c>.part</c> (Firefox) to the file, Baidu
/// Netdisk <c>.baiduyun.p.downloading</c>, 迅雷 <c>.xltd</c> and 夸克网盘 <c>.qkdownloading</c> (<see cref="Downloaders"/>, 10-11), so
/// every second we look for those files: asked of Everything wherever they are (a folder picked for one download, 10-11), and in the
/// download folders — the ones set in settings (else the system Downloads folder), the desktop, and the ones the download programs'
/// own settings name. Downloading → speed + flowing bar, several at once → one item (<see cref="DownloadTracker"/>), stopped
/// growing → paused; when the partial file disappears and the finished file appears → done (clicking shows it in Explorer).
/// Explorer's copy/unzip/compress (<see cref="FileOperationWatcher"/>), WinRAR's (<see cref="ArchiverWatcher"/>), exports
/// (<see cref="VideoExportWatcher"/>), Steam's downloads (<see cref="SteamWatcher"/>) and the printer (<see cref="PrintWatcher"/>) run on the same thread.
/// Nothing is written to disk; memory only. Log "download: …" (迅雷 and 夸克: experimental, TODO 四 10-11).
/// </summary>
internal sealed partial class DownloadWatcher : IDisposable
{
    private const string BaiduExtension = ".baiduyun.p.downloading";
    private static readonly string[] PartialExtensions = [BaiduExtension, Downloaders.Thunder, Downloaders.Quark, ".crdownload", ".part", ".opdownload", ".download"];
    private const string Anywhere = "<ext:xltd;qkdownloading;crdownload;opdownload | endwith:.baiduyun.p.downloading> !path:$Recycle.Bin";   // the bin by its path (a name-only "!$Recycle.Bin" let "$I….xltd" through, 10-11); IsPartial checks again
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Glance = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FoldersEvery = TimeSpan.FromSeconds(30);
    private const long BaiduQuiet = 64 * 1024;   // Baidu's helper writes logs and settings too; less than this a second isn't the download
    private const long ThunderQuiet = 512 * 1024;   // 迅雷 writes about 0.3 MB/s of its own all the time (measured 10-11)
    private const long ThunderBusy = 1024 * 1024;

    private readonly Func<Core.Configuration.IsleBarSettings> _settings;
    private readonly CancellationTokenSource _stop = new();
    private readonly DownloadTracker _tracker = new();
    private readonly FileOperationWatcher _fileOps;
    private readonly ArchiverWatcher _archivers;
    private readonly VideoExportWatcher _exports;
    private readonly SteamWatcher _steam;
    private readonly PrintWatcher _print;
    private readonly List<ActivityState> _finished = [];
    /// <summary>Per program: files finished since its card started; told together when its last one is through.</summary>
    private readonly Dictionary<string, List<string>> _batch = [];
    /// <summary>Per program: when its card started (a finished card says how long it took).</summary>
    private readonly Dictionary<string, DateTimeOffset> _since = [];
    private HashSet<string> _lastPartials = [];
    private readonly Dictionary<string, HashSet<string>> _lastListing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PartialDownload> _lastSeen = new(StringComparer.OrdinalIgnoreCase);

    private EverythingSdk? _everything;
    private bool _everythingTried;
    private List<string> _ownFolders = [];
    private DateTimeOffset _ownFoldersAt;

    // Baidu reserves each download at full size up front (measured 10-04), and so does 迅雷 (10-11): the file can't show how far it
    // is, so what the program writes stands in, shared out among its downloads under way
    private readonly Reserved _baidu = new("baidunetdiskhost", BaiduQuiet);
    private readonly Reserved _thunder = new("thunder", ThunderQuiet, always: true);

    // 迅雷's ".xltd" sizes (its whole file): done only if a file of the very size took its place
    private readonly Dictionary<string, long> _thunderSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _thunderOpen = new(StringComparer.OrdinalIgnoreCase);

    // the fallback (TODO 一): 迅雷 writing a lot with no download of its we can see (saved where we don't look, Everything missing)
    private int _thunderStreak;
    private DateTimeOffset? _thunderBusySince, _thunderBusyLast;

    /// <param name="settings">The folders added in settings, and each feature's switch (10-12: a switched-off one isn't looked at).</param>
    public DownloadWatcher(Func<Core.Configuration.IsleBarSettings> settings, Func<Core.Localization.LanguageStrings> text)
    {
        _settings = settings;
        _text = text;
        _fileOps = new FileOperationWatcher(text);
        _archivers = new ArchiverWatcher(text);
        _exports = new VideoExportWatcher(text);
        _steam = new SteamWatcher(text);
        _print = new PrintWatcher(text);
    }

    private readonly Func<Core.Localization.LanguageStrings> _text;

    /// <summary>Downloads and file operations to show now (in progress + just finished). Safe to read from any thread.</summary>
    public IReadOnlyList<ActivityState> Current { get; private set; } = [];

    public void Start() => _ = Task.Run(LoopAsync);

    /// <summary>This user's Downloads folder (if relocated, that location — the path the shell knows). Also the settings box's default.</summary>
    internal static string DefaultFolder()
    {
        try
        {
            var path = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders",
                "{374DE290-123F-4565-9164-39C4925E467B}", null) as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                return Environment.ExpandEnvironmentVariables(path);
            }
        }
        catch (System.Security.SecurityException)
        {
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>
    /// Downloads always, the folders added in settings (10-12: added one by one, Downloads no longer dropped once one is),
    /// the desktop, and the folders the download programs save to by their own settings. Each looked at once.
    /// </summary>
    private List<string> Folders(DateTimeOffset now)
    {
        if (now - _ownFoldersAt >= FoldersEvery)
        {
            _ownFoldersAt = now;
            _ownFolders = OwnFolders();
        }

        var folders = new[] { DefaultFolder() }.Concat(_settings().DownloadFolders.Select(Environment.ExpandEnvironmentVariables));
        return folders.Append(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)).Concat(_ownFolders)
            .Where(f => f.Length > 0).Select(f => f.TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Where 迅雷, 夸克 and the Chromium browsers save by their own settings (10-11, user: read them, don't ask).</summary>
    private static List<string> OwnFolders()
    {
        var found = new List<string?>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (AppNames.ExePath("thunder.exe") is { } thunder && Path.GetDirectoryName(Path.GetDirectoryName(thunder)) is { } root)
        {
            found.Add(Downloaders.ThunderFolder(ReadText(Path.Combine(root, "Profiles", "config.json"))));
        }

        found.Add(Downloaders.QuarkFolder(ReadText(Path.Combine(local, "QuarkCloudDrive", "User Data", "preference.json"))));
        foreach (var browser in new[] { @"Microsoft\Edge\User Data", @"Google\Chrome\User Data" })
        {
            var data = Path.Combine(local, browser);
            if (!Directory.Exists(data))
            {
                continue;
            }

            foreach (var profile in Directory.EnumerateDirectories(data).Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal)))
            {
                found.Add(Downloaders.ChromiumFolder(ReadText(Path.Combine(profile, "Preferences"))));
            }
        }

        return [.. found.OfType<string>()];
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var on = _settings();   // each feature's switch (10-12): one that's off isn't looked at at all
            List<ActivityState> items;
            try
            {
                items = on.ShowBrowserDownloads || on.ShowDownloaders ? Scan(now, on.ShowBrowserDownloads, on.ShowDownloaders) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                items = [];
            }

            try
            {
                if (on.ShowFileOps)
                {
                    items.AddRange(_fileOps.Scan(now));
                }

                if (on.ShowArchivers)
                {
                    items.AddRange(_archivers.Scan(now));   // WinRAR, 7-Zip, Bandizip (TODO 四, 10-11)
                }

                if (on.ShowExports)
                {
                    items.AddRange(_exports.Scan(now));     // 剪映's export (TODO 四, 10-11)
                }

                if (on.ShowSteam)
                {
                    items.AddRange(_steam.Scan(now));       // Steam's game downloads (TODO 四, 10-11, experimental)
                }

                if (on.ShowPrinter)
                {
                    items.AddRange(_print.Scan(now));       // the printer's queue (TODO 四, 10-11)
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                           or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
            {
            }

            Current = items;
            try
            {
                // while Explorer's progress window is up it's looked at 4 times a second: its last words before closing
                // ("cancelling", how many are left) come and go within a second (user's tests 10-10)
                var waited = TimeSpan.Zero;
                while (((on.ShowFileOps && _fileOps.Busy) || (on.ShowArchivers && _archivers.Busy)) && waited + Glance < PollInterval)
                {
                    await Task.Delay(Glance, _stop.Token);
                    waited += Glance;
                    try
                    {
                        var glance = DateTimeOffset.UtcNow;
                        if (on.ShowFileOps)
                        {
                            _fileOps.Glance(glance);
                        }

                        if (on.ShowArchivers)
                        {
                            _archivers.Glance(glance);
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
                    {
                    }
                }

                await Task.Delay(PollInterval - waited, _stop.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    /// <param name="browsers">Browser downloads switched on.</param>
    /// <param name="programs">Download programs (迅雷, 夸克, 百度网盘) switched on.</param>
    private List<ActivityState> Scan(DateTimeOffset now, bool browsers, bool programs)
    {
        var result = new List<ActivityState>();
        var files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        var listings = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        bool Wanted(string path) => IsProgram(path) ? programs : browsers;
        foreach (var file in Everything()?.Find(Anywhere, 50) ?? [])
        {
            if (IsPartial(file) && Wanted(file) && new FileInfo(file) is { Exists: true } info)
            {
                files[file] = info;
            }
        }

        // the folders listed too: what a browser's "Unconfirmed 123" became is the file that newly appeared beside it
        foreach (var folder in Folders(now).Concat(files.Keys.Select(Path.GetDirectoryName).OfType<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            listings[folder] = names;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                names.Add(Path.GetFileName(file));
                if (IsPartial(file) && Wanted(file) && !files.ContainsKey(file))
                {
                    files[file] = new FileInfo(file);
                }
            }
        }

        // the programs' write meters only while they're switched on (迅雷's is read even with nothing to follow: its fallback)
        if (programs)
        {
            _baidu.Share(now, [.. files.Keys.Where(IsBaidu)]);
        }

        var thunder = files.Keys.Where(IsThunder).ToList();
        var open = ThunderOpen(thunder);
        var thunderWritten = programs ? _thunder.Share(now, open) : 0;
        foreach (var path in thunder)
        {
            _thunderSizes[path] = files[path].Length;
        }

        var chromium = files.Keys.Any(f => f.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)) ? RunningChromium() : [];
        var partials = files.Select(f =>
        {
            var (path, info) = (f.Key, f.Value);
            var written = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            var source = DownloadSources.Of(path, chromium);
            var app = AppOf(path);
            var tool = app ?? ToolOf(source);
            if (IsThunder(path))
            {
                // its write time moves only when it's closed: held open = being downloaded now; no total shown (only what came
                // since it was first seen is known, not what came before — a resumed download)
                return new PartialDownload(path, FinalName(path), (long)_thunder.Received(path), open.Contains(path) ? now : written,
                    Source: source, App: app, Moving: open.Contains(path), Tool: tool);
            }

            return IsBaidu(path)
                ? new PartialDownload(path, FinalName(path), (long)_baidu.Received(path), written, Total: info.Length, Source: source, App: app, Tool: tool)
                : new PartialDownload(path, FinalName(path), info.Length, written, Source: source, App: app, Tool: tool);
        }).ToList();

        // each program's downloads under way become one item: summed speed · earliest name · "+N" (user 10-11: a card each)
        var summaries = _tracker.Update(now, partials);
        result.AddRange(summaries);
        var under = summaries.Select(ProgramOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var program in under)
        {
            _since.TryAdd(program, now);
        }

        var tracked = new HashSet<string>(_tracker.Tracked, StringComparer.OrdinalIgnoreCase);

        // The partial file from last time is gone and the finished file is there → finished. One finishing while others of its
        // program go on says nothing; when its last one is through, the batch is told once, on the same card (it rises from its
        // cell) — user 10-07.
        foreach (var gone in _lastPartials.Where(p => !tracked.Contains(p)))
        {
            if (!File.Exists(gone) && Finished(gone, listings) is { } final && _lastSeen.GetValueOrDefault(gone) is { } was)
            {
                if (!_batch.TryGetValue(was.Program, out var list))
                {
                    list = _batch[was.Program] = [];
                }

                list.Add(final);
            }
        }

        _finished.RemoveAll(f => under.Contains(ProgramOf(f)));   // a new download took its program's card
        foreach (var (program, done) in _batch.Where(b => !under.Contains(b.Key)).ToList())
        {
            var was = _lastSeen.Values.FirstOrDefault(p => p.Program == program);
            _finished.Add(new ActivityState
            {
                AppId = was?.App,
                Tool = was?.Tool,
                RawKind = ActivityState.KindTransfer,
                Title = "Download → PC",
                Name = Path.GetFileName(done[0]),
                MoreCount = done.Count - 1,
                Note = done.Count == 1 && new FileInfo(done[0]) is { Exists: true } info ? Core.Ui.CardSheets.Bytes(info.Length) : null,
                Took = _since.TryGetValue(program, out var since) ? now - since : null,
                State = "done",
                Open = done.Count == 1 ? done[0] : Path.GetDirectoryName(done[0]),
                SourcePath = "download:" + program + "|" + done[0],   // the same card as its downloads under way
                UpdatedAt = now,
            });
            _batch.Remove(program);
        }

        foreach (var program in _since.Keys.Where(p => !under.Contains(p)).ToList())
        {
            _since.Remove(program);
        }

        _lastPartials = tracked;
        foreach (var p in partials.Where(p => tracked.Contains(p.Path)))
        {
            _lastSeen[p.Path] = p;
        }

        foreach (var gone in _thunderSizes.Keys.Where(k => !files.ContainsKey(k) && !_lastPartials.Contains(k)).ToList())
        {
            _thunderSizes.Remove(gone);
        }

        foreach (var gone in _lastSeen.Keys.Where(k => !tracked.Contains(k)).ToList())
        {
            _lastSeen.Remove(gone);
        }

        _lastListing.Clear();
        foreach (var (folder, names) in listings)
        {
            _lastListing[folder] = names;
        }

        if (programs && ThunderFallback(now, thunderWritten, open.Count > 0) is { } busy)
        {
            result.Add(busy);
        }

        _finished.RemoveAll(f => now - f.UpdatedAt > ActivityStore.DoneLinger);
        result.AddRange(_finished);
        return result;
    }

    /// <summary>迅雷's files it holds open now (being downloaded), logged when that changes.</summary>
    private HashSet<string> ThunderOpen(List<string> thunder)
    {
        var open = new HashSet<string>(thunder.Where(FileUsers.IsOpen), StringComparer.OrdinalIgnoreCase);
        foreach (var path in open.Where(p => !_thunderOpen.Contains(p)))
        {
            AppLog.Write($"download: 迅雷 downloading {Path.GetFileName(path)} ({new FileInfo(path).Length >> 20} MB reserved)");
        }

        foreach (var path in _thunderOpen.Where(p => !open.Contains(p)))
        {
            AppLog.Write($"download: 迅雷 let go of {Path.GetFileName(path)} ({(File.Exists(path) ? "paused or cancelled: still there" : "gone")})");
        }

        _thunderOpen.Clear();
        _thunderOpen.UnionWith(open);
        return open;
    }

    /// <summary>迅雷 writing a lot (two busy seconds in a row) with none of its downloads in sight, till it's quiet a moment.</summary>
    private ActivityState? ThunderFallback(DateTimeOffset now, long written, bool seen)
    {
        _thunderStreak = written >= ThunderBusy ? _thunderStreak + 1 : 0;
        if (seen || (_thunderBusySince is null && _thunderStreak < 2)
            || (written < ThunderBusy && (_thunderBusyLast is null || now - _thunderBusyLast > TimeSpan.FromSeconds(3))))
        {
            if (_thunderBusySince is not null)
            {
                AppLog.Write("download: 迅雷 busy without a download we see — over");
            }

            _thunderBusySince = _thunderBusyLast = null;
            return null;
        }

        if (written >= ThunderBusy)
        {
            _thunderBusyLast = now;
        }

        if (_thunderBusySince is null)
        {
            _thunderBusySince = now;
            AppLog.Write($"download: 迅雷 writing {written >> 10} KB/s without a download we see — fallback card ({(_everything is null ? "no Everything" : "Everything found no .xltd it holds")})");
        }

        return new ActivityState
        {
            AppId = "thunder.exe",
            Tool = "thunder.exe",
            RawKind = ActivityState.KindTransfer,
            Title = "Download → PC",
            Name = "迅雷",
            Stage = "download",
            State = "run",   // no size, no speed: the bar flows
            SourcePath = "download:thunder.exe|busy",   // 迅雷's own card
            UpdatedAt = _thunderBusySince.Value,
        };
    }

    /// <summary>
    /// The finished file of a partial that went away: its final name — really finished only if it has content (Firefox keeps an
    /// empty placeholder under the final name while it downloads, and a paused download read as "done" — review 10-03); 迅雷's only
    /// if it is exactly the size of the reserved ".xltd" (not seen finishing yet, 10-11: no guess). For Chrome/Edge's
    /// "Unconfirmed 123" the name isn't known, so it's the file that newly appeared in that folder this second.
    /// </summary>
    private string? Finished(string partial, Dictionary<string, HashSet<string>> listings)
    {
        var folder = Path.GetDirectoryName(partial) ?? string.Empty;
        if (FinalName(partial) is { } name)
        {
            var final = new FileInfo(Path.Combine(folder, name));
            if (IsThunder(partial))
            {
                var size = _thunderSizes.GetValueOrDefault(partial);
                var through = final.Exists && size > 0 && final.Length == size;
                AppLog.Write($"download: 迅雷 {Path.GetFileName(partial)} went — {(through ? "done" : final.Exists ? $"{name} is {final.Length} bytes, not {size}: no card" : $"no {name}: cancelled, no card")}");
                return through ? final.FullName : null;
            }

            if (final.Exists && final.Length > 0 && (!IsQuark(partial) || DateTime.UtcNow - final.LastWriteTimeUtc < TimeSpan.FromMinutes(1)))
            {
                return final.FullName;
            }

            if (!IsQuark(partial))
            {
                return null;   // 夸克 may finish under another name when that one's taken: the file that newly appeared, below
            }
        }

        if (!listings.TryGetValue(folder, out var now) || !_lastListing.TryGetValue(folder, out var before))
        {
            return null;
        }

        return now.Where(n => !before.Contains(n) && !PartialExtensions.Any(ext => n.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Select(n => new FileInfo(Path.Combine(folder, n)))
            .Where(f => f.Exists && f.Length > 0)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// What a program that reserves its files at full size wrote, shared out among its downloads under way (Baidu: all its
    /// partials; 迅雷: the ones it holds open).
    /// </summary>
    /// <param name="always">Looked at even with nothing to follow (迅雷's fallback); otherwise the process isn't even looked at then.</param>
    private sealed class Reserved(string process, long quiet, bool always = false)
    {
        private readonly ProcessWriteMeter _meter = new(process);
        private readonly Dictionary<string, double> _received = new(StringComparer.OrdinalIgnoreCase);

        public double Received(string path) => _received.GetValueOrDefault(path);

        /// <summary>Takes this second's writes and returns them (the first look sets the baseline: 0).</summary>
        public long Share(DateTimeOffset now, IReadOnlyCollection<string> under)
        {
            foreach (var gone in _received.Keys.Where(k => !under.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                _received.Remove(gone);
            }

            if (under.Count == 0 && !always)
            {
                _meter.Reset();
                return 0;
            }

            var written = _meter.Delta(now);
            var share = written >= quiet && under.Count > 0 ? (double)written / under.Count : 0;
            foreach (var path in under)
            {
                _received[path] = _received.GetValueOrDefault(path) + share;
            }

            return written;
        }
    }

    /// <summary>The Chromium browsers running now (a ".crdownload" is one of theirs — which, only they know).</summary>
    private static List<string> RunningChromium()
    {
        var names = new List<string>();
        foreach (var (process, name) in DownloadSources.Chromium)
        {
            var found = System.Diagnostics.Process.GetProcessesByName(process);
            if (found.Length > 0)
            {
                names.Add(name);
            }

            foreach (var p in found)
            {
                p.Dispose();
            }
        }

        return names;
    }

    private static bool IsPartial(string path)
        => PartialExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) && !Downloaders.InRecycleBin(path);

    private static bool IsBaidu(string path) => path.EndsWith(BaiduExtension, StringComparison.OrdinalIgnoreCase);

    private static bool IsThunder(string path) => path.EndsWith(Downloaders.Thunder, StringComparison.OrdinalIgnoreCase);

    private static bool IsQuark(string path) => path.EndsWith(Downloaders.Quark, StringComparison.OrdinalIgnoreCase);

    /// <summary>A download program's file (迅雷, 夸克, 百度网盘) — the rest are the browsers' (each its own switch, 10-12).</summary>
    private static bool IsProgram(string path) => IsThunder(path) || IsQuark(path) || IsBaidu(path);

    /// <summary>The program of a download item ("download:{program}|{file}").</summary>
    private static string ProgramOf(ActivityState s)
        => s.SourcePath is { } p && p.StartsWith("download:", StringComparison.Ordinal) && p.IndexOf('|', StringComparison.Ordinal) is var bar and > 0
            ? p[9..bar]
            : string.Empty;

    /// <summary>A browser's exe, for its icon and name on the lifted card ("Edge" → "msedge.exe"); null when not known which.</summary>
    private static string? ToolOf(string? source) => source switch
    {
        null => null,
        "Firefox" => "firefox.exe",
        "Opera" => "opera.exe",
        _ => DownloadSources.Chromium.FirstOrDefault(c => c.Name == source).Process is { } process ? process + ".exe" : null,
    };

    /// <summary>The download program whose icon its card shows; null = a browser's (the arrow, as before).</summary>
    private static string? AppOf(string path) => path switch
    {
        _ when IsThunder(path) => "thunder.exe",
        _ when IsQuark(path) => "quark_cloud_drive.exe",
        _ when IsBaidu(path) => "BaiduNetdisk.exe",
        _ => null,
    };

    /// <summary>
    /// "report.pdf.crdownload" → "report.pdf"; 夸克's "X-c4d5541d929b.zip.qkdownloading" → "X.zip". Null for Chrome/Edge's
    /// "Unconfirmed 123.crdownload" ("未确认 307769" in Chinese): the browser keeps that name until the download ends, then renames
    /// it (measured 10-04).
    /// </summary>
    private static string? FinalName(string partial)
    {
        var name = Path.GetFileName(partial);
        if (UnconfirmedPattern().IsMatch(name))
        {
            return null;
        }

        if (IsQuark(name))
        {
            return Downloaders.QuarkFinalName(name);
        }

        foreach (var ext in PartialExtensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^ext.Length];
            }
        }

        return name;
    }

    /// <summary>"Unconfirmed 123.crdownload" in any language: words, a space, digits, no extension of its own.</summary>
    [GeneratedRegex(@"^[^.]+ \d+\.crdownload$", RegexOptions.IgnoreCase)]
    private static partial Regex UnconfirmedPattern();

    private EverythingSdk? Everything()
    {
        if (!_everythingTried)
        {
            _everythingTried = true;
            _everything = EverythingSdk.TryLoad();
        }

        return _everything;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _fileOps.Dispose();
        _exports.Dispose();
        _everything?.Dispose();
    }
}
