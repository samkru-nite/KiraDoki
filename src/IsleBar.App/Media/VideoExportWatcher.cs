using System.Diagnostics;
using System.Runtime.InteropServices;
using IsleBar.Core.Island;

namespace IsleBar.App.Media;

/// <summary>
/// A video editor's export on the island (TODO 四, user 10-10: 剪映 must be done; PR / DaVinci the same way, untried). Read from files,
/// the steadiest source (TODO 一: the editor's own screen isn't read — 剪映's is Qt and gives UI Automation no words, 10-11).
/// <para>
/// 剪映 (measured 10-11, 11.5): exporting shows a window "导出-{draft}"; it writes a temporary
/// <c>C:\.__jianying_export_temp_folder_{n}__\{guid}.mp4</c>, renames it to the export's name, then moves it to the chosen folder and
/// the window goes; cancelled, the temporary file goes to the recycle bin. So: window up → "导出中 {draft}" with the speed and the
/// size so far (no percentage: the flowing bar); window gone and a video of the temporary file's very size turned up outside the
/// temporary folder and the bin → "已导出 {draft}", ↗ shows it in its folder; else no card. Without Everything (the files can't be
/// found) the window and the writes still show "导出中", and nothing is said at the end.
/// </para>
/// <para>
/// Premiere Pro / Media Encoder / DaVinci Resolve (experimental, not tried — TODO 四): no window is known; the editor writing a lot
/// while a video file grows outside caches → "导出中 {file}", "已导出 {file}" once it stopped growing for a while.
/// </para>
/// Runs on the download watcher's thread. Log "export: …".
/// </summary>
internal sealed class VideoExportWatcher : IDisposable
{
    internal const string VideoGlyph = IsleBar.Core.Ui.Icons.Video;   // only when the editor's own icon can't be found (user 10-11: its logo)
    private const string Videos = "ext:mp4;mov;mkv;avi;m4v;mxf";
    private const string JianyingTemp = "__jianying_export_temp_folder_";
    private const string JianyingWindow = "导出-";
    private const long BusyBytes = 2 * 1024 * 1024;   // a second
    private static readonly TimeSpan LookForResult = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Settled = TimeSpan.FromSeconds(6);

    private readonly Func<Core.Localization.LanguageStrings> _text;
    private readonly Interop.ProcessWriteMeter _jianying = new("JianyingPro");
    private readonly RateMeter _rate = new();
    private readonly (string Name, Interop.ProcessWriteMeter[] Meters)[] _others =
    [
        ("Premiere Pro", [new("Adobe Premiere Pro"), new("Adobe Media Encoder")]),
        ("DaVinci Resolve", [new("Resolve")]),
    ];

    private EverythingSdk? _everything;
    private bool _everythingTried;
    private readonly List<ActivityState> _finished = [];

    // 剪映's export now: since, the draft, the temporary file and its size last seen
    private DateTimeOffset? _since;
    private string _draft = string.Empty;
    private long _size;
    private DateTimeOffset _grew;
    private static readonly TimeSpan StoodStill = TimeSpan.FromSeconds(10);   // a temporary file left behind by an export that died

    // its window just went: looking for where the file went
    private (string Draft, long Size, DateTimeOffset Until, DateTimeOffset Since)? _ended;

    // an experimental editor's growing file
    private (string Editor, string Path, long Size, DateTimeOffset Grew, DateTimeOffset Since)? _other;

    public VideoExportWatcher(Func<Core.Localization.LanguageStrings> text) => _text = text;

    public List<ActivityState> Scan(DateTimeOffset now)
    {
        var result = new List<ActivityState>();
        var written = _jianying.Delta(now);
        _rate.Add(now, written);
        // Exporting = a file in 剪映's temporary export folder (10-11: its window "导出-…" is up before — the settings — and after — "done"
        // — so it says nothing). The draft's name from the window, when it is up.
        var window = JianyingExport();
        if (window is not null)
        {
            _draft = window;
        }

        var everything = Everything();
        var temp = everything?.RecentlyChanged($"path:\"{JianyingTemp}\" {Videos}", TimeSpan.FromMinutes(10), 3)
            .Select(p => (Path: p, Size: Size(p))).FirstOrDefault(f => f.Size is not null);
        if (temp?.Size is { } now0 && now0 != _size)
        {
            _grew = now;
        }

        var exporting = everything is not null
            ? temp is { Size: { } } && (_since is not null || written >= BusyBytes / 2)   // there and being written (not one left behind)
              && (_since is null || now - _grew < StoodStill)
            : window is not null && (written >= BusyBytes || (_since is not null && _rate.Speed(now) is > 0));   // no Everything: window + writes
        if (exporting)
        {
            if (_since is null)
            {
                _since = now;
                _size = 0;
                Interop.TaskbarHost.Log($"export: 剪映 exporting \"{_draft}\"{(everything is null ? " (no Everything: no ending)" : string.Empty)}");
            }

            _size = temp?.Size ?? _size;
            result.Add(Running(_draft, _size > 0 ? _size : null, _rate.Speed(now), "export:jianying", _since.Value));
        }
        else if (_since is { } since)
        {
            // the temporary file went: moved to where it was exported (done) or into the bin (cancelled)
            _ended = everything is null ? null : (_draft, _size, now + LookForResult, since);
            _since = null;
            if (everything is null)
            {
                Interop.TaskbarHost.Log("export: 剪映 stopped writing (no Everything: whether it was done isn't known) — no card");
            }
        }

        if (_ended is { } ended)
        {
            FindResult(ended, now);
        }

        if (Other(now) is { } other)
        {
            result.Add(other);
        }

        _finished.RemoveAll(f => now - f.UpdatedAt > ActivityStore.DoneLinger);
        result.AddRange(_finished);
        return result;
    }

    /// <summary>The export window gone: the file of the temporary one's size where it was moved (done), in the bin (cancelled), or not yet.</summary>
    private void FindResult((string Draft, long Size, DateTimeOffset Until, DateTimeOffset Since) ended, DateTimeOffset now)
    {
        // the file as last seen grows a little more at the very end (10-11: 66,846,768 seen, 67,536,578 exported) — so not the same
        // size: within a band around it (user 10-11: either way, should an export rewrite its file at the end), and changed since this
        // export began (an earlier export of the draft lies there too)
        var slack = Math.Max(16L * 1024 * 1024, ended.Size / 10);
        var found = ended.Size <= 0 || Everything() is not { } everything
            ? []
            : everything.RecentlyChanged(Videos, now - ended.Since + TimeSpan.FromSeconds(10), 10)
                .Where(p => !p.Contains(JianyingTemp, StringComparison.OrdinalIgnoreCase)
                            && Size(p) is { } s && s >= ended.Size - slack && s <= ended.Size + slack)
                .ToList();
        if (found.FirstOrDefault(p => !p.Contains("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) is { } path)
        {
            _ended = null;
            _finished.Add(Done(ended.Draft, path, "export:jianying", now, now - ended.Since));
            Interop.TaskbarHost.Log($"export: 剪映 done — \"{ended.Draft}\" in {Path.GetDirectoryName(path)}");
        }
        else if (found.Count > 0)
        {
            _ended = null;
            Interop.TaskbarHost.Log("export: 剪映 cancelled (the file went to the recycle bin) — no card");
        }
        else if (now > ended.Until)
        {
            _ended = null;
            Interop.TaskbarHost.Log($"export: 剪映 ended, no card (no video of about {ended.Size} bytes changed since it began{(_everything is null ? ", no Everything" : string.Empty)})");
        }
    }

    /// <summary>Experimental (Premiere / DaVinci, not tried): the editor writing a lot while a video grows; done once it stood still.</summary>
    private ActivityState? Other(DateTimeOffset now)
    {
        var busy = _others.Select(o => (o.Name, Bytes: o.Meters.Sum(m => m.Delta(now)))).MaxBy(o => o.Bytes);
        if (_other is null && busy.Bytes >= BusyBytes && Everything() is { } everything
            && everything.RecentlyChanged(Videos, TimeSpan.FromSeconds(5), 5).FirstOrDefault(Outside) is { } path && Size(path) is { } size)
        {
            _other = (busy.Name, path, size, now, now);
            Interop.TaskbarHost.Log($"export: {busy.Name} (experimental) writing {Path.GetFileName(path)}");
        }

        if (_other is not { } o)
        {
            return null;
        }

        if (Size(o.Path) is { } grown && grown != o.Size)
        {
            _other = o = o with { Size = grown, Grew = now };
        }

        if (now - o.Grew < Settled)
        {
            return Running(Path.GetFileNameWithoutExtension(o.Path), o.Size, null, "export:" + o.Editor, o.Since);
        }

        _other = null;
        if (File.Exists(o.Path))
        {
            _finished.Add(Done(Path.GetFileNameWithoutExtension(o.Path), o.Path, "export:" + o.Editor, now, o.Grew - o.Since));
            Interop.TaskbarHost.Log($"export: {o.Editor} (experimental) done — {Path.GetFileName(o.Path)}");
        }

        return null;
    }

    /// <summary>Not a cache, preview or temporary file (an editor writes those all the time).</summary>
    private static bool Outside(string path)
        => !path.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase) && !path.Contains("Cache", StringComparison.OrdinalIgnoreCase)
           && !path.Contains("Preview", StringComparison.OrdinalIgnoreCase) && !path.Contains("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)
           && !path.Contains(JianyingTemp, StringComparison.OrdinalIgnoreCase);

    /// <summary>The editor's program, whose icon the card shows in all its stages (user 10-11: 剪映's own, as an agent's logo).</summary>
    private static string AppOf(string key) => key switch
    {
        "export:jianying" => "JianyingPro.exe",
        "export:Premiere Pro" => "Adobe Premiere Pro.exe",
        _ => "Resolve.exe",
    };

    private ActivityState Running(string name, long? bytes, double? speed, string key, DateTimeOffset since) => new()
    {
        AppId = AppOf(key),
        RawKind = ActivityState.KindTransfer,
        Title = "Files",
        Name = name,
        Words = _text().FileExport,
        Glyph = VideoGlyph,
        Stage = "copy",
        Done = bytes,   // the size so far; no total: the bar flows
        Speed = speed,
        State = "run",
        SourcePath = key,
        UpdatedAt = since,
    };

    private ActivityState Done(string name, string path, string key, DateTimeOffset now, TimeSpan took) => new()
    {
        AppId = AppOf(key),
        RawKind = ActivityState.KindTransfer,
        Title = "Files",
        Name = name,
        Note = Size(path) is { } size ? Core.Ui.CardSheets.Bytes(size) : null,   // the video's size (user 10-11)
        Took = took,
        Words = _text().FileExported,
        Stage = "copy",
        State = "done",
        Open = path,   // ↗ shows it in its folder (MainWindow.StepFor)
        SourcePath = key,   // the same card as the one in progress
        UpdatedAt = now,
    };

    /// <summary>剪映's export window's draft ("导出-10月11日" → "10月11日"), or null when none is up.</summary>
    private static string? JianyingExport()
    {
        var ids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("JianyingPro"))
        {
            ids.Add((uint)p.Id);
            p.Dispose();
        }

        if (ids.Count == 0)
        {
            return null;
        }

        string? draft = null;
        var text = new char[256];
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var pid);
            if (ids.Contains(pid) && IsWindowVisible(window) && GetWindowText(window, text, text.Length) is var n and > 0
                && new string(text, 0, n) is var title && title.StartsWith(JianyingWindow, StringComparison.Ordinal) && title.Length > JianyingWindow.Length)
            {
                draft = title[JianyingWindow.Length..].Trim();
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return draft;
    }

    private static long? Size(string path)
    {
        try
        {
            return new FileInfo(path) is { Exists: true } f ? f.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private EverythingSdk? Everything()
    {
        if (!_everythingTried)
        {
            _everythingTried = true;
            _everything = EverythingSdk.TryLoad();
        }

        return _everything;
    }

    public void Dispose() => _everything?.Dispose();

    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, char[] text, int max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
}
