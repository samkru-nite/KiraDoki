using System.Text.RegularExpressions;
using IsleBar.App.Interop;
using IsleBar.Core.Island;

namespace IsleBar.App.Media;

/// <summary>
/// Puts downloads on the island (decision card #3: "download progress — extend to browser downloads").
/// While downloading, browsers append <c>.crdownload</c> (Chrome, Edge, Whale, etc.) or <c>.part</c> (Firefox) to the file, and
/// Baidu Netdisk <c>.baiduyun.p.downloading</c>, so we check the download folders every second (the system Downloads folder, or
/// the ones set in settings): downloading → speed + flowing bar, several at once → one item (<see cref="DownloadTracker"/>),
/// stopped growing → paused; when the partial file disappears and the finished file appears → done (clicking shows it in Explorer).
/// Explorer's copy/unzip/compress (<see cref="FileOperationWatcher"/>) runs on the same thread.
/// Nothing is written to disk; memory only.
/// </summary>
internal sealed partial class DownloadWatcher : IDisposable
{
    private const string BaiduExtension = ".baiduyun.p.downloading";
    private static readonly string[] PartialExtensions = [BaiduExtension, ".crdownload", ".part", ".opdownload", ".download"];
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private const long BaiduQuiet = 64 * 1024;   // Baidu's helper writes logs and settings too; less than this a second isn't the download

    private readonly Func<IReadOnlyList<string>> _settingFolders;
    private readonly CancellationTokenSource _stop = new();
    private readonly DownloadTracker _tracker = new();
    private readonly FileOperationWatcher _fileOps = new();
    private readonly List<ActivityState> _finished = [];
    /// <summary>Files finished since the merged card started; told together when the last one is through.</summary>
    private readonly List<string> _batch = [];
    private HashSet<string> _lastPartials = [];
    private readonly Dictionary<string, HashSet<string>> _lastListing = new(StringComparer.OrdinalIgnoreCase);

    // Baidu reserves each download at full size up front (measured 10-04), so the file can't show how far it is: what its helper
    // process writes stands in, shared out among the Baidu downloads under way
    private readonly ProcessWriteMeter _baidu = new("baidunetdiskhost");
    private readonly Dictionary<string, double> _baiduReceived = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="folders">Folders set in settings; empty = the system Downloads folder.</param>
    public DownloadWatcher(Func<IReadOnlyList<string>> folders)
    {
        _settingFolders = folders;
    }

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

    private List<string> Folders()
    {
        var set = _settingFolders();
        IEnumerable<string> folders = set.Count > 0 ? set.Select(Environment.ExpandEnvironmentVariables) : [DefaultFolder()];
        return folders.Select(f => f.TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            List<ActivityState> items;
            try
            {
                items = Scan(now);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                items = [];
            }

            try
            {
                items.AddRange(_fileOps.Scan(now));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                           or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
            {
            }

            Current = items;
            try
            {
                await Task.Delay(PollInterval, _stop.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private List<ActivityState> Scan(DateTimeOffset now)
    {
        var result = new List<ActivityState>();
        var files = new List<(string Path, FileInfo Info)>();
        var listings = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Folders())
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
                if (PartialExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    files.Add((file, new FileInfo(file)));
                }
            }
        }

        var baidu = files.Where(f => IsBaidu(f.Path)).Select(f => f.Path).ToList();
        ShareBaiduWrites(now, baidu);
        var chromium = files.Any(f => f.Path.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)) ? RunningChromium() : [];
        var partials = files.Select(f =>
        {
            var written = new DateTimeOffset(f.Info.LastWriteTimeUtc, TimeSpan.Zero);
            var source = DownloadSources.Of(f.Path, chromium);
            return _baiduReceived.TryGetValue(f.Path, out var received)
                ? new PartialDownload(f.Path, FinalName(f.Path), (long)received, written, Total: f.Info.Length, Source: source)
                : new PartialDownload(f.Path, FinalName(f.Path), f.Info.Length, written, Source: source);
        }).ToList();

        // all downloads under way become one item: summed speed · earliest name · "+N"
        var summary = _tracker.Update(now, partials);
        if (summary is not null)
        {
            result.Add(summary);
        }

        var tracked = new HashSet<string>(_tracker.Tracked, StringComparer.OrdinalIgnoreCase);

        // The partial file from last time is gone and the finished file is there → finished. One finishing while others go on says
        // nothing; when the last one is through, the batch is told once, on the same card (it rises from its cell) — user 10-07.
        foreach (var gone in _lastPartials.Where(p => !tracked.Contains(p)))
        {
            if (!File.Exists(gone) && Finished(gone, listings) is { } final)
            {
                _batch.Add(final);
            }
        }

        if (summary is null && _batch.Count > 0)
        {
            _finished.Clear();
            _finished.Add(new ActivityState
            {
                RawKind = ActivityState.KindTransfer,
                Title = "Download → PC",
                Name = Path.GetFileName(_batch[0]),
                MoreCount = _batch.Count - 1,
                State = "done",
                Open = _batch.Count == 1 ? _batch[0] : Path.GetDirectoryName(_batch[0]),
                SourcePath = "download:" + _batch[0],   // the merged card's key ("download")
                UpdatedAt = now,
            });
            _batch.Clear();
        }
        else if (summary is not null)
        {
            _finished.Clear();   // a new download took the card
        }

        _lastPartials = tracked;
        _lastListing.Clear();
        foreach (var (folder, names) in listings)
        {
            _lastListing[folder] = names;
        }

        _finished.RemoveAll(f => now - f.UpdatedAt > ActivityStore.DoneLinger);
        result.AddRange(_finished);
        return result;
    }

    /// <summary>
    /// The finished file of a partial that went away: its final name — really finished only if it has content (Firefox keeps an
    /// empty placeholder under the final name while it downloads, and a paused download read as "done" — review 10-03). For
    /// Chrome/Edge's "Unconfirmed 123" the name isn't known, so it's the file that newly appeared in that folder this second.
    /// </summary>
    private string? Finished(string partial, Dictionary<string, HashSet<string>> listings)
    {
        var folder = Path.GetDirectoryName(partial) ?? string.Empty;
        if (FinalName(partial) is { } name)
        {
            var final = Path.Combine(folder, name);
            return File.Exists(final) && new FileInfo(final).Length > 0 ? final : null;
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

    /// <summary>Shares what Baidu's helper wrote this second among the Baidu downloads under way.</summary>
    private void ShareBaiduWrites(DateTimeOffset now, List<string> baidu)
    {
        foreach (var gone in _baiduReceived.Keys.Where(k => !baidu.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            _baiduReceived.Remove(gone);
        }

        if (baidu.Count == 0)
        {
            _baidu.Reset();   // nothing to follow: don't even look at the process
            return;
        }

        var written = _baidu.Delta(now);
        var share = written >= BaiduQuiet ? (double)written / baidu.Count : 0;
        foreach (var path in baidu)
        {
            _baiduReceived[path] = _baiduReceived.GetValueOrDefault(path) + share;
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

    private static bool IsBaidu(string path) => path.EndsWith(BaiduExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "report.pdf.crdownload" → "report.pdf". Null for Chrome/Edge's "Unconfirmed 123.crdownload" ("未确认 307769" in Chinese):
    /// the browser keeps that name until the download ends, then renames it (measured 10-04).
    /// </summary>
    private static string? FinalName(string partial)
    {
        var name = Path.GetFileName(partial);
        if (UnconfirmedPattern().IsMatch(name))
        {
            return null;
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

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _fileOps.Dispose();
    }
}
