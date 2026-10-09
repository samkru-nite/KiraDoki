using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Interop.UIAutomationClient;
using IsleBar.App.Interop;
using IsleBar.Core.Island;

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

    public List<ActivityState> Scan(DateTimeOffset now)
    {
        var result = new List<ActivityState>();
        var written = _explorer.Delta(now);
        _rate.Add(now, written);

        if (ProgressWindow() is { } window)
        {
            _opSince ??= now;
            _archive = null;
            var percent = Percent(window.Title);
            var (name, unzip) = ReadWindow(window.Handle);
            (_lastName, _lastGlyph) = (name, unzip ? UnzipGlyph : CopyGlyph);
            result.Add(new ActivityState
            {
                RawKind = ActivityState.KindTransfer,
                Title = "Files",
                Name = name,
                Glyph = unzip ? UnzipGlyph : CopyGlyph,
                Stage = "copy",
                Done = percent,
                Total = percent is null ? null : 100,
                Speed = _rate.Speed(now),
                State = "run",
                SourcePath = "fileop:window",
                UpdatedAt = _opSince.Value,
            });
        }
        else
        {
            if (_opSince is not null)
            {
                _finished = Done(_lastName, null, now, _lastGlyph);   // the window closed: done (or cancelled — the window doesn't say)
                _opSince = null;
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
                _finished = Done(Path.GetFileName(_archive), _archive, now);
            }

            _archive = null;
            return null;
        }

        return new ActivityState
        {
            RawKind = ActivityState.KindTransfer,
            Title = "Files",
            Name = Path.GetFileName(_archive),
            Glyph = CompressGlyph,
            Stage = "copy",
            Done = _archiveSize,   // size so far; the total isn't known, so the bar flows
            Speed = _rate.Speed(now),
            State = "run",
            SourcePath = "fileop:" + _archive,
            UpdatedAt = _archiveSince,
        };
    }

    /// <summary>
    /// Finished: the same card as the one in progress (same key), so it rises from its cell and shows a few seconds (user 10-07 —
    /// a separate "done" card dropped in while the icon vanished).
    /// </summary>
    private static ActivityState Done(string? name, string? open, DateTimeOffset now, string? glyph = null) => new()
    {
        RawKind = ActivityState.KindTransfer,
        Title = "Files",
        Name = name,
        Glyph = glyph,
        Stage = "copy",
        State = "done",
        Open = open,
        SourcePath = "fileop:" + (open ?? "window"),
        UpdatedAt = now,
    };

    private string? _lastName;
    private string? _lastGlyph;

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
    /// From the progress window's text: the file being handled (the element after the first "label:" — "Name:" / "名称:"), and
    /// whether it unpacks an archive (an element other than that file ends in an archive's extension — the source, "X.zip";
    /// the sentence around it ends in the percentage, so it doesn't count).
    /// Every element read is released at once: COM references left behind make Explorer sluggish (see SearchBoxTracker).
    /// </summary>
    private (string? Name, bool Unzip) ReadWindow(IntPtr handle)
    {
        var names = new List<string>();
        try
        {
            _automation ??= (IUIAutomation)new CUIAutomation();
            _all ??= _automation.CreateTrueCondition();
            var window = _automation.ElementFromHandle(handle);
            if (window is null)
            {
                return (null, false);
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
            return (null, false);
        }

        var label = names.FindIndex(n => n.EndsWith(':') || n.EndsWith('：'));
        var name = label >= 0 && label + 1 < names.Count ? names[label + 1] : null;
        var unzip = names.Any(n => n != name && IsArchive(n));
        return (name, unzip);
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
