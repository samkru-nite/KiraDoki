using System.Diagnostics;
using System.Runtime.InteropServices;
using IsleBar.Core.Island;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.Media;

/// <summary>
/// WinRAR's and 7-Zip's packing and unpacking on the island (TODO 四, measured 10-11): its progress window is an ordinary dialog whose words can be
/// read (<see cref="ArchiverReader"/>) — "压缩中 X.rar" with the real overall percentage, then "已压缩 X.rar" when it surely went
/// through (<see cref="ArchiveTally"/>), nothing when cancelled or after a question. Each window is its own card ("archiver:{window}"),
/// hidden while another window of WinRAR asks something. A window sent to the background (hidden) is still read; a job ends when
/// its window is gone, not hidden. Idle cost: one process lookup a second. Runs on the download watcher's thread. Log "archiver: …".
/// </summary>
internal sealed class ArchiverWatcher
{
    // WinRAR; 7-Zip: 7zG.exe is its right-click menu's, 7zFM.exe its own window (the same progress window)
    private static readonly string[] Processes = ["WinRAR", "7zG", "7zFM", "Bandizip", "bz"];

    private readonly Func<Core.Localization.LanguageStrings> _text;
    private readonly Dictionary<IntPtr, Job> _jobs = [];
    private readonly List<ActivityState> _finished = [];

    private sealed class Job(DateTimeOffset since)
    {
        public DateTimeOffset Since { get; } = since;
        public ArchiveTally Tally { get; } = new();
        public bool Asking { get; set; }
    }

    // The fallback (user 10-11: a new WinRAR whose window we no longer read must not vanish): WinRAR writing a lot with no progress
    // window we know → "进行中 WinRAR", a flowing bar, no percentage, no "done" (it isn't known how it ended)
    private const long BusyBytes = 1024 * 1024;   // a second
    private static readonly TimeSpan BusyLinger = TimeSpan.FromSeconds(3);
    private readonly (string Name, Interop.ProcessWriteMeter Meter)[] _written =
        [("WinRAR", new("WinRAR")), ("7-Zip", new("7zG")), ("7-Zip", new("7zFM")), ("Bandizip", new("Bandizip")), ("Bandizip", new("bz"))];
    private string _busyName = string.Empty;
    private DateTimeOffset? _busySince, _busyLast;

    // a job's last writes come in the second its window closes (10-11: the fallback showed for 3 s after every 7-Zip job), so none
    // for a while after a job, and only after two busy seconds in a row
    private static readonly TimeSpan AfterJob = TimeSpan.FromSeconds(5);
    private DateTimeOffset _quietUntil;
    private int _busyStreak;
    private IReadOnlyList<string> _unread = [];

    public ArchiverWatcher(Func<Core.Localization.LanguageStrings> text) => _text = text;

    /// <summary>A progress window is up (it's then looked at 4 times a second: "Cancel" pauses it for about a second, 10-11).</summary>
    public bool Busy => _jobs.Count > 0;

    public List<ActivityState> Scan(DateTimeOffset now)
    {
        Look(now);
        var result = new List<ActivityState>();
        var text = _text();
        if (Fallback(now) is { } busy)
        {
            result.Add(busy);
        }

        foreach (var (window, job) in _jobs)
        {
            if (job.Asking || job.Tally.Last is not { } last)
            {
                continue;   // a question is up: nothing moves till it's answered, and it is right there
            }

            result.Add(new ActivityState
            {
                RawKind = ActivityState.KindTransfer,
                Title = "Files",
                Name = Path.GetFileName(last.Archive),
                Words = last.Job == ArchiveJob.Zip ? text.FileZip : text.FileUnzip,
                Glyph = last.Job == ArchiveJob.Zip ? FileOperationWatcher.CompressGlyph : FileOperationWatcher.UnzipGlyph,
                Stage = "copy",
                Done = last.Percent,
                Total = last.Percent is null ? null : 100,
                State = "run",   // paused: the bar just stands (it is paused right there, in the window)
                SourcePath = Key(window),
                UpdatedAt = job.Since,
            });
        }

        _finished.RemoveAll(f => now - f.UpdatedAt > ActivityStore.DoneLinger);
        result.AddRange(_finished);
        return result;
    }

    /// <summary>WinRAR busy with no window read (<see cref="BusyBytes"/> a second), till it has been quiet for a moment.</summary>
    private ActivityState? Fallback(DateTimeOffset now)
    {
        var (busiest, written) = _written.Select(w => (w.Name, Bytes: w.Meter.Delta(now))).MaxBy(w => w.Bytes);
        if (_jobs.Count > 0)
        {
            _quietUntil = now + AfterJob;
        }

        _busyStreak = written >= BusyBytes ? _busyStreak + 1 : 0;
        if (now < _quietUntil || (_busySince is null && _busyStreak < 2)
            || (written < BusyBytes && (_busyLast is null || now - _busyLast > BusyLinger)))
        {
            if (_busySince is not null)
            {
                Interop.TaskbarHost.Log("archiver: busy without a window we read — over");
            }

            _busySince = _busyLast = null;
            return null;
        }

        if (written >= BusyBytes)
        {
            _busyLast = now;
            _busyName = busiest;
        }

        if (_busySince is null)
        {
            _busySince = now;
            // what its windows said, to read them again next time (a new WinRAR, another language)
            Interop.TaskbarHost.Log($"archiver: busy without a window we read — fallback card; its windows: {string.Join(" | ", _unread)}");
        }

        return new ActivityState
        {
            RawKind = ActivityState.KindTransfer,
            Title = "Files",
            Name = _busyName,
            Words = _text().CardRunning,
            Glyph = FileOperationWatcher.CompressGlyph,
            Stage = "copy",
            State = "run",   // no percentage: the bar flows
            SourcePath = "archiver:busy",
            UpdatedAt = _busySince.Value,
        };
    }

    /// <summary>A look between the once-a-second scans while a window is up (the ending needs it).</summary>
    public void Glance(DateTimeOffset now) => Look(now);

    private static string Key(IntPtr window) => "archiver:" + window.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void Look(DateTimeOffset now)
    {
        var pids = new HashSet<uint>();
        foreach (var name in Processes)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                pids.Add((uint)p.Id);
                p.Dispose();
            }
        }

        var dialogs = new List<(IntPtr Window, uint Pid)>();
        if (pids.Count > 0)
        {
            var cls = new char[32];
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var pid);
                if (pids.Contains(pid) && GetClassName(window, cls, cls.Length) is var n && new string(cls, 0, n) == "#32770")
                {
                    dialogs.Add((window, pid));
                }

                return true;
            }, IntPtr.Zero);
        }

        var titles = dialogs.ToDictionary(d => d.Window, d => Text(d.Window));
        var progress = new Dictionary<IntPtr, ArchiveLook>();
        foreach (var (window, _) in dialogs)
        {
            _jobs.TryGetValue(window, out var job);
            if (ArchiverReader.Read(titles[window], Children(window), job?.Tally.Last) is { } look)
            {
                progress[window] = look;
            }
        }

        _unread = [.. dialogs.Where(d => !progress.ContainsKey(d.Window) && IsWindowVisible(d.Window)).Select(d => titles[d.Window])];
        foreach (var (window, look) in progress)
        {
            if (!_jobs.TryGetValue(window, out var job))
            {
                job = _jobs[window] = new Job(now);
                Interop.TaskbarHost.Log($"archiver: {look.Job} {Path.GetFileName(look.Archive)} began");
            }

            var pid = dialogs.First(d => d.Window == window).Pid;
            var asking = dialogs.Where(d => d.Pid == pid && !progress.ContainsKey(d.Window) && IsWindowVisible(d.Window)
                                            && ArchiverReader.Asks(titles[d.Window])).Select(d => titles[d.Window]).FirstOrDefault();
            if (asking is not null && !job.Asking)
            {
                Interop.TaskbarHost.Log($"archiver: asks \"{asking}\" — no \"done\" card for this one");
            }

            job.Asking = asking is not null;
            job.Tally.See(look, job.Asking);
        }

        foreach (var window in _jobs.Keys.Where(w => !progress.ContainsKey(w) && !IsWindow(w)).ToList())
        {
            Ended(window, _jobs[window], now);
            _jobs.Remove(window);
        }
    }

    /// <summary>The window went away: "已压缩 X.rar" when it surely went through; otherwise the card just goes (it is not guessed).</summary>
    private void Ended(IntPtr window, Job job, DateTimeOffset now)
    {
        var tally = job.Tally;
        if (tally.Last is not { } last || !tally.Finished)
        {
            Interop.TaskbarHost.Log($"archiver: ended, no card ({(tally.Asked ? "a question was answered" : tally.Last?.Paused == true ? "cancelled" : "not through")}, last {tally.Last?.Percent}%)");
            return;
        }

        var text = _text();
        _finished.Add(new ActivityState
        {
            RawKind = ActivityState.KindTransfer,
            Title = "Files",
            Name = Path.GetFileName(last.Archive),
            Words = last.Job == ArchiveJob.Zip ? text.FileZipped : text.FileUnzipped,
            Stage = "copy",
            State = "done",
            Open = last.Job == ArchiveJob.Zip && File.Exists(last.Archive) ? last.Archive : null,
            SourcePath = Key(window),   // the same card as the one in progress: it rises from its cell
            UpdatedAt = now,
        });
        Interop.TaskbarHost.Log($"archiver: done — {last.Job} {Path.GetFileName(last.Archive)} (last seen {last.Percent}%)");
    }

    /// <summary>A window's text — WM_GETTEXT, which reads another program's controls (GetWindowText doesn't).</summary>
    private static string Text(IntPtr window)
    {
        var buffer = new char[1024];
        return SendMessageTimeout(window, GetText, (IntPtr)buffer.Length, buffer, AbortIfHung, 200, out _) == IntPtr.Zero
            ? string.Empty
            : new string(buffer, 0, Array.IndexOf(buffer, '\0') is var end and >= 0 ? end : buffer.Length);
    }

    /// <summary>The texts of a window's controls, top to bottom as made, the empty ones left out.</summary>
    private static List<string> Children(IntPtr window)
    {
        var texts = new List<string>();
        EnumChildWindows(window, (child, _) =>
        {
            if (Text(child) is { Length: > 0 } t && !string.IsNullOrWhiteSpace(t))
            {
                texts.Add(t);
            }

            return true;
        }, IntPtr.Zero);
        return texts;
    }

    private const uint GetText = 0x000D;   // WM_GETTEXT
    private const uint AbortIfHung = 0x0002;

    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, char[] text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, char[] lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);
}
