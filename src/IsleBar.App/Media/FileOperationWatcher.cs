using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Interop.UIAutomationClient;
using IsleBar.App.Interop;
using IsleBar.Core.Island;
using IsleBar.Core.SystemWatch;
using IsleBar.Core.Ui;

namespace IsleBar.App.Media;

/// <summary>
/// Puts Explorer's file operations on the island, wherever the files go (measured 10-04 on Windows 11):
/// <list type="bullet">
/// <item>Copy, move and Windows' own unzip show a progress window (class <c>OperationStatusWindow</c>) whose title is the
/// percentage ("37% complete") — the bar follows it. The copied files are created at full size up front, so they say nothing.</item>
/// <item>Windows' own compress shows no such window; the archive just grows. When Explorer writes a lot with no progress window,
/// Everything is asked for an archive changed in the last seconds, and its size is followed (Everything's own size lags).</item>
/// </list>
/// The window's text (read through UI Automation while it shows) names the operation and the file being handled — "Copying 15
/// items from X.zip to Y … Name: DSC01495.ARW". Windows' unzip says "copying" too, from an archive: that's how it's told apart.
/// The pill shows icon · speed · file name, the icon saying copy / unzip / compress.
/// Speed is what that Explorer process wrote. Idle cost: a FindWindow and one process's write count a second.
/// Runs on the download watcher's thread.
/// </summary>
internal sealed partial class FileOperationWatcher : IDisposable
{
    private const string WindowClass = "OperationStatusWindow";
    private const long BusyBytes = 1024 * 1024;   // Explorer writes thumbnails and settings all the time; less than this a second is not a transfer
    private const string ArchiveQuery = "file: ext:zip;7z;tar;gz;tgz;rar";
    private static readonly TimeSpan ArchiveIdle = TimeSpan.FromSeconds(5);
    private static readonly string[] ArchiveExtensions = [".zip", ".7z", ".tar", ".gz", ".tgz", ".rar", ".cab"];

    // icons on the pill's left (Segoe Fluent Icons)
    internal const string CopyGlyph = "\uE8C8";
    internal const string MoveGlyph = "\uE8C6";       // cut (user 10-10: moving = the cut icon)
    internal const string UnzipGlyph = "\uE838";      // open folder
    internal const string CompressGlyph = "\uE8A5";   // document; MainWindow lays zipper teeth over it (ZipTeeth) \u2014 the font has no zip file, and its zip folder F012 looked crude (user 10-04)

    private IUIAutomation? _automation;
    private IUIAutomationCondition? _all;

    private readonly ProcessWriteMeter _explorer = new("explorer");
    private readonly RateMeter _rate = new();
    private EverythingSdk? _everything;
    private bool _everythingTried;

    private DateTimeOffset? _opSince;      // progress window showing since
    private string? _archive;              // archive being written
    private long _archiveSize;
    private DateTimeOffset _archiveSince, _archiveGrew;
    private ActivityState? _finished;

    private readonly Func<Core.Localization.LanguageStrings> _text;
    private FileOpTally? _tally;   // the operation in the window now
    private bool _unzip;
    private long? _percent;

    public FileOperationWatcher(Func<Core.Localization.LanguageStrings> text) => _text = text;

    /// <summary>Explorer's progress window is up (it's then looked at 4 times a second, <see cref="Glance"/>).</summary>
    public bool Busy => _opSince is not null;

    // For the transit station (other threads; TODO ④ 10-11, user: one thing, one card): a progress window that comes up just after
    // something was dragged out of the station is the station's — this card stays away, the station's own card shows what it reads.
    private static long _windowOpened;   // UTC ticks the last window was first seen, 0 = never
    private static int _windowUp;
    private static long _transitDropped;   // UTC ticks of the last drop out of the station
    private static int _owned;             // the window up now (or last) is the station's
    private static ActivityState? _running, _ended;

    /// <summary>A window first seen this soon after a drop out of the station is taken to be that drop's.</summary>
    private static readonly TimeSpan TransitClaim = TimeSpan.FromSeconds(3);

    /// <summary>When the last progress window was first seen (null = none yet), whether it is still up, and whether it is the
    /// transit station's (<see cref="TransitDropped"/>).</summary>
    public static (DateTimeOffset? Opened, bool Up, bool Owned) Window
        => (Volatile.Read(ref _windowOpened) is var t and > 0 ? new DateTimeOffset(t, TimeSpan.Zero) : null,
            Volatile.Read(ref _windowUp) == 1, Volatile.Read(ref _owned) == 1);

    /// <summary>The station's window as this card would show it now ("复制中 …" with the percentage); null while it asks a question.</summary>
    public static ActivityState? Running => Volatile.Read(ref _running);

    /// <summary>How the station's last window ended: "已复制 …" when surely through, else null.</summary>
    public static ActivityState? Ended => Volatile.Read(ref _ended);

    /// <summary>Something was just let go out of the transit station.</summary>
    public static void TransitDropped(DateTimeOffset at) => Volatile.Write(ref _transitDropped, at.UtcTicks);

    public List<ActivityState> Scan(DateTimeOffset now)
    {
        var result = new List<ActivityState>();
        var written = _explorer.Delta(now);
        _rate.Add(now, written);

        if (ProgressWindow() is { } window)
        {
            if (_opSince is null)
            {
                _opSince = now;
                var dropped = Volatile.Read(ref _transitDropped);
                var ago = now.UtcTicks - dropped;
                var owned = dropped > 0 && ago >= -TimeSpan.TicksPerSecond && ago <= TransitClaim.Ticks;
                Volatile.Write(ref _owned, owned ? 1 : 0);
                Volatile.Write(ref _ended, null);
                Volatile.Write(ref _windowOpened, now.UtcTicks);
                Volatile.Write(ref _windowUp, 1);
                AppLog.Write($"fileop: window up{(owned ? " — the transit station's (dragged out of it just now)" : string.Empty)}");
            }

            _archive = null;
            Look(window, now);
            var tally = _tally!;
            var job = FileOpText.JobOf(tally.Verb, _unzip);
            var words = FileOpText.Running(job, tally.Count, tally.Name, _text());
            var running = new ActivityState
            {
                RawKind = ActivityState.KindTransfer,
                Title = "Files",
                Name = words.Detail,
                Words = words.Words,
                MoreCount = words.More,
                Glyph = job switch { FileJob.Unzip => UnzipGlyph, FileJob.Move => MoveGlyph, _ => CopyGlyph },
                Stage = "copy",
                Done = _percent,
                Total = _percent is null ? null : 100,
                Speed = _rate.Speed(now),
                State = "run",
                SourcePath = "fileop:window",
                UpdatedAt = _opSince.Value,
            };

            // the window asking a question (a name clash): nothing moves till it's answered, and the window is right there — no
            // card meanwhile (user 10-11: "移动中" showed under a question)
            var shown = tally.Asking ? null : running;
            Volatile.Write(ref _running, shown);
            if (shown is not null && Volatile.Read(ref _owned) == 0)
            {
                result.Add(shown);
            }
        }
        else
        {
            if (_opSince is not null)
            {
                var before = _finished;
                Closed(now);
                Volatile.Write(ref _running, null);
                if (Volatile.Read(ref _owned) == 1)
                {
                    // the station's card says it ("已复制 …" made just now; otherwise nothing)
                    Volatile.Write(ref _ended, ReferenceEquals(before, _finished) ? null : _finished);
                    _finished = before;
                }

                Volatile.Write(ref _windowUp, 0);
            }

            if (FollowArchive(now, written) is { } archive)
            {
                result.Add(archive);
            }
        }

        if (_finished is not null && now - _finished.UpdatedAt <= ActivityStore.DoneLinger)
        {
            result.Add(_finished);
        }

        return result;
    }

    /// <summary>A look between the once-a-second scans while the window is up (nothing is shown from it; the ending needs it).</summary>
    public void Glance(DateTimeOffset now)
    {
        if (_tally is not null && ProgressWindow() is { } window)
        {
            Look(window, now);
        }
    }

    private void Look((IntPtr Handle, string Title) window, DateTimeOffset now)
    {
        _tally ??= new FileOpTally();
        var texts = ReadTexts(window.Handle);
        if (texts is null)
        {
            return;
        }

        var look = FileOpReader.Read(window.Title, texts);
        _tally.See(look, now);
        // unpacking an archive: an element other than the file being handled ends in an archive's extension — the source, "X.zip"
        // (the sentence around it ends in the percentage, so it doesn't count)
        if (!look.Asking)
        {
            _unzip |= texts.Any(n => n != look.Name && IsArchive(n));   // (a question may name an archive of the person's own)
        }

        _percent = Percent(window.Title) ?? _percent;
        Diagnose(window.Title, _percent, texts);
    }

    /// <summary>
    /// The window closed (TODO ④, user 10-10): "已复制 …" only when it surely went through (<see cref="FileOpTally.Finished"/>);
    /// otherwise — a question was answered, it was cancelled, it closed too early — the card just goes: the window never says how a
    /// question was answered, and guessing it was wrong twice.
    /// </summary>
    private void Closed(DateTimeOffset now)
    {
        var tally = _tally ?? new FileOpTally();
        var job = FileOpText.JobOf(tally.Verb, _unzip);
        if (tally.Finished(now))
        {
            var words = FileOpText.Done(job, tally.Count, tally.Name, _text());
            _finished = new ActivityState
            {
                RawKind = ActivityState.KindTransfer,
                Title = "Files",
                Name = words.Detail,
                Words = words.Words,
                MoreCount = words.More,
                Stage = "copy",
                State = "done",
                SourcePath = "fileop:window",   // the same card as the one in progress: it rises from its cell (user 10-07)
                UpdatedAt = now,
            };
            AppLog.Write($"fileop: done — {words.Words} {words.Detail}{(words.More > 0 ? " +" + words.More : string.Empty)}");
        }
        else
        {
            AppLog.Write($"fileop: ended, no card ({(tally.Cancelled ? "cancelled" : tally.Asked ? "a question was answered" : "closed before it could finish")})");
        }

        DiagnoseClosed(now);
        _opSince = null;
        _tally = null;
        _unzip = false;
        _percent = null;
    }

    /// <summary>An archive Windows is writing (compress has no progress window). Null when none.</summary>
    private ActivityState? FollowArchive(DateTimeOffset now, long written)
    {
        if (_archive is null && written >= BusyBytes && Everything() is { } everything)
        {
            var candidate = everything.RecentlyChanged(ArchiveQuery, ArchiveIdle, 5).FirstOrDefault();
            if (candidate is not null && Size(candidate) is { } size)
            {
                _archive = candidate;
                _archiveSize = size;
                _archiveSince = _archiveGrew = now;
            }
        }

        if (_archive is null)
        {
            return null;
        }

        var current = Size(_archive);
        if (current is { } grown && grown != _archiveSize)
        {
            _archiveSize = grown;
            _archiveGrew = now;
        }

        if (current is null || now - _archiveGrew > ArchiveIdle)
        {
            if (current is > 0)
            {
                _finished = new ActivityState
                {
                    RawKind = ActivityState.KindTransfer,
                    Title = "Files",
                    Name = Path.GetFileName(_archive),
                    Words = _text().FileZipped,
                    Stage = "copy",
                    State = "done",
                    Open = _archive,
                    SourcePath = "fileop:" + _archive,   // the same card as the one in progress: it rises from its cell (user 10-07)
                    UpdatedAt = now,
                };
            }

            _archive = null;
            return null;
        }

        return new ActivityState
        {
            RawKind = ActivityState.KindTransfer,
            Title = "Files",
            Name = Path.GetFileName(_archive),
            Words = _text().FileZip,
            Glyph = CompressGlyph,
            Stage = "copy",
            Done = _archiveSize,   // size so far; the total isn't known, so the bar flows
            Speed = _rate.Speed(now),
            State = "run",
            SourcePath = "fileop:" + _archive,
            UpdatedAt = _archiveSince,
        };
    }

    // ---- diagnostics (TODO ④ file transfer card, 10-10): what the progress window says, logged as it changes (the rules in
    // FileOpTally were made from these). Log "fileop: …". Kept for the trial: a case the rules get wrong shows here.
    private string? _logged;
    private int _lines;
    private long? _lastPercent;
    private readonly List<string> _titles = [];
    private DateTimeOffset _diagSince;

    /// <summary>The window's whole text, logged whenever it changes (the same text isn't logged twice).</summary>
    private void Diagnose(string title, long? percent, IReadOnlyList<string> texts)
    {
        if (_titles.Count == 0)
        {
            _diagSince = DateTimeOffset.UtcNow;
        }

        if (percent is not null)
        {
            _lastPercent = percent;
        }

        // the percentage and the time left change all the time: compared without digits, logged with them
        var all = $"[{title}] {string.Join(" ¦ ", texts.Take(40))}";   // the "keep which files" list names every clash
        var shape = Regex.Replace(all, @"\d", string.Empty);
        if (!_titles.Contains(Regex.Replace(title, @"\d", string.Empty)))
        {
            _titles.Add(Regex.Replace(title, @"\d", string.Empty));
        }

        if (shape != _logged && _lines++ < 60)   // a thousand files would log a thousand names
        {
            _logged = shape;
            AppLog.Write($"fileop: {ProgressWindowCount()} window(s) — {all}");
        }
    }

    private void DiagnoseClosed(DateTimeOffset now)
    {
        AppLog.Write($"fileop: closed after {(now - _diagSince).TotalSeconds:0.0} s — last {(_lastPercent is { } p ? p + "%" : "no %")}, titles seen: {string.Join(" / ", _titles)}");
        _logged = null;
        _lines = 0;
        _lastPercent = null;
        _titles.Clear();
    }

    private static int ProgressWindowCount()
    {
        var count = 0;
        var handle = IntPtr.Zero;
        while ((handle = FindWindowExW(IntPtr.Zero, handle, WindowClass, null)) != IntPtr.Zero)
        {
            if (IsWindowVisible(handle))
            {
                count++;
            }
        }

        return count;
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

    private static long? Size(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every text in the progress window, in order (what they say: <see cref="FileOpReader"/>). Null when it can't be read.
    /// Every element read is released at once: COM references left behind make Explorer sluggish (see SearchBoxTracker).
    /// </summary>
    private List<string>? ReadTexts(IntPtr handle)
    {
        var names = new List<string>();
        try
        {
            _automation ??= (IUIAutomation)new CUIAutomation();
            _all ??= _automation.CreateTrueCondition();
            var window = _automation.ElementFromHandle(handle);
            if (window is null)
            {
                return null;
            }

            var found = window.FindAll(TreeScope.TreeScope_Descendants, _all);
            for (var i = 0; found is not null && i < found.Length; i++)
            {
                var element = found.GetElement(i);
                if (element.CurrentName is { } text && text.Trim().Length > 0)
                {
                    names.Add(text.Trim());
                }

                Marshal.ReleaseComObject(element);
            }

            if (found is not null)
            {
                Marshal.ReleaseComObject(found);
            }

            Marshal.ReleaseComObject(window);
        }
        catch (COMException)
        {
            return null;
        }

        return names;
    }

    private static bool IsArchive(string name) => ArchiveExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>"37% complete" in any language → 37.</summary>
    private static long? Percent(string title)
        => PercentPattern().Match(title) is { Success: true } match && long.TryParse(match.Groups[1].Value, out var value)
            ? Math.Clamp(value, 0, 100)
            : null;

    [GeneratedRegex(@"(\d{1,3})\s*%")]
    private static partial Regex PercentPattern();

    /// <summary>Explorer's progress window, if one is showing (several operations share one window).</summary>
    private static (IntPtr Handle, string Title)? ProgressWindow()
    {
        var handle = IntPtr.Zero;
        while ((handle = FindWindowExW(IntPtr.Zero, handle, WindowClass, null)) != IntPtr.Zero)
        {
            if (IsWindowVisible(handle))
            {
                var buffer = new char[256];
                var length = GetWindowTextW(handle, buffer, buffer.Length);
                return (handle, new string(buffer, 0, Math.Max(0, length)));
            }
        }

        return null;
    }

    public void Dispose()
    {
        _everything?.Dispose();
        if (_all is not null)
        {
            Marshal.ReleaseComObject(_all);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string className, string? title);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr handle, char[] text, int max);
}

/// <summary>Bytes a second over the last few seconds, from per-second amounts.</summary>
internal sealed class RateMeter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
    private readonly Queue<(DateTimeOffset At, long Bytes)> _samples = new();

    public void Add(DateTimeOffset now, long bytes)
    {
        _samples.Enqueue((now, bytes));
        while (_samples.Count > 0 && now - _samples.Peek().At > Window)
        {
            _samples.Dequeue();
        }
    }

    /// <summary>Null until two seconds have been seen.</summary>
    public double? Speed(DateTimeOffset now)
    {
        if (_samples.Count < 2)
        {
            return null;
        }

        var span = now - _samples.Peek().At;
        return span.TotalSeconds < 1 ? null : _samples.Skip(1).Sum(s => s.Bytes) / span.TotalSeconds;
    }
}
