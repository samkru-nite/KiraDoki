using IsleBar.Core.Island;

namespace IsleBar.App.Media;

/// <summary>
/// Puts browser downloads on the island (decision card #3: "download progress — extend to browser downloads").
/// While downloading, browsers append <c>.crdownload</c> (Chrome, Edge, Whale, etc.) or <c>.part</c> (Firefox) to the file,
/// so we check the downloads folder every second: downloading → speed + flowing bar (total size can't be known from the file),
/// several at once → one item (<see cref="DownloadTracker"/>), stopped growing → paused;
/// when the partial file disappears and the original-name file appears → done (clicking shows that file in Explorer).
/// Nothing is written to disk; memory only.
/// </summary>
internal sealed class DownloadWatcher : IDisposable
{
    private static readonly string[] PartialExtensions = [".crdownload", ".part", ".opdownload", ".download"];
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LookupInterval = TimeSpan.FromSeconds(3);   // the browser writes a new download's row within seconds

    private readonly string _folder;
    private readonly CancellationTokenSource _stop = new();
    private readonly DownloadTracker _tracker = new();
    private readonly List<ActivityState> _finished = [];
    private HashSet<string> _lastPartials = [];
    private readonly Dictionary<string, long> _totals = new(StringComparer.OrdinalIgnoreCase);   // partial file → full size (from the browser)
    private readonly HashSet<string> _loggedMiss = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _nextLookup;

    public DownloadWatcher(string? folder = null)
    {
        _folder = folder ?? DefaultFolder();
    }

    /// <summary>Downloads to show now (in progress + just finished). Safe to read from any thread.</summary>
    public IReadOnlyList<ActivityState> Current { get; private set; } = [];

    public void Start() => _ = Task.Run(LoopAsync);

    /// <summary>The user's downloads folder (if relocated, that location — the path the shell knows).</summary>
    private static string DefaultFolder()
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

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                Current = Scan(DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Current = [];
            }

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
        var files = new List<PartialDownload>();
        if (Directory.Exists(_folder))
        {
            foreach (var file in Directory.EnumerateFiles(_folder))
            {
                if (!PartialExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var info = new FileInfo(file);
                files.Add(new PartialDownload(file, FinalName(file), info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
            }
        }

        LookUpTotals(now, files);
        files = files.Select(f => _totals.TryGetValue(f.Path, out var total) ? f with { Total = total } : f).ToList();

        // all downloads under way become one item: summed speed · earliest name · "+N"
        if (_tracker.Update(now, files) is { } summary)
        {
            result.Add(summary);
        }

        var partials = new HashSet<string>(_tracker.Tracked, StringComparer.OrdinalIgnoreCase);
        foreach (var gone in _totals.Keys.Where(k => !partials.Contains(k)).ToList())
        {
            _totals.Remove(gone);
        }

        _loggedMiss.RemoveWhere(k => !partials.Contains(k));

        // The partial file from last time is gone and the original-name file exists → done (shown briefly)
        foreach (var gone in _lastPartials.Where(p => !partials.Contains(p)))
        {
            var final = Path.Combine(_folder, FinalName(gone));
            // really finished: the partial file is gone (not just stalled) and the file has content — Firefox keeps an empty
            // placeholder under the final name while it downloads, and a paused download read as "done" (review 10-03)
            if (!File.Exists(gone) && File.Exists(final) && new FileInfo(final).Length > 0)
            {
                _finished.Add(new ActivityState
                {
                    RawKind = ActivityState.KindTransfer,
                    Title = "Download → PC",
                    Name = Path.GetFileName(final),
                    State = "done",
                    Open = final,
                    SourcePath = "download-done:" + final,
                    UpdatedAt = now,
                });
            }
        }

        _lastPartials = partials;
        _finished.RemoveAll(f => now - f.UpdatedAt > ActivityStore.DoneLinger);
        result.AddRange(_finished);
        return result;
    }

    /// <summary>
    /// Full sizes of the partial files, from Edge's/Chrome's download list (<see cref="BrowserDownloadHistory"/>) — asked every few
    /// seconds while some file still has none. A row matches by its partial file, or by its final name in this folder.
    /// Firefox and other programs aren't in that list: they keep the flowing bar.
    /// </summary>
    private void LookUpTotals(DateTimeOffset now, List<PartialDownload> files)
    {
        var missing = files.Where(f => !_totals.ContainsKey(f.Path) && now - f.LastWrite <= DownloadTracker.StaleOnArrival).ToList();
        if (missing.Count == 0 || now < _nextLookup)
        {
            return;
        }

        _nextLookup = now + LookupInterval;
        var rows = BrowserDownloadHistory.InProgress();
        foreach (var file in missing)
        {
            var final = Path.Combine(_folder, file.Name);
            var row = rows.FirstOrDefault(r => string.Equals(r.CurrentPath, file.Path, StringComparison.OrdinalIgnoreCase))
                      ?? rows.FirstOrDefault(r => string.Equals(r.TargetPath, final, StringComparison.OrdinalIgnoreCase));
            if (row is { Total: > 0 })
            {
                _totals[file.Path] = row.Total;
                Interop.AppLog.Write($"download total: {file.Name} = {row.Total} bytes (browser download list)");
            }
            else if (_loggedMiss.Add(file.Path))
            {
                // once per file: no row yet (asked again later), or the server didn't say the size (total 0 → flowing bar)
                Interop.AppLog.Write($"download total: {file.Name} not known yet ({(row is null ? $"no matching row among {rows.Count}" : "browser has total 0")})");
            }
        }
    }

    /// <summary>"report.pdf.crdownload" → "report.pdf". Chrome's "Unconfirmed 123.crdownload" stays as-is (name not known yet).</summary>
    private static string FinalName(string partial)
    {
        var name = Path.GetFileName(partial);
        foreach (var ext in PartialExtensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^ext.Length];
            }
        }

        return name;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
