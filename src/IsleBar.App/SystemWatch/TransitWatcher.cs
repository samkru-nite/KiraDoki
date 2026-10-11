using IsleBar.App.Interop;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// The transit station card (中转站, TODO ④, user 10-10; the list in <see cref="TransitShelf"/>). Files dropped on the pill go in
/// (only their paths), one status card says "中转站 3 个文件", no colour; something new going in → debut 10 s, even with the mouse on the pill (it was dropped there). Dragging the big pill
/// drags them all out; what the receiver moved away leaves (<see cref="Dragged"/>), what it copied stays. Empty = no card. Kept over a
/// restart in <c>%LOCALAPPDATA%\IsleBar\transit.txt</c>. Log "transit: …" (counts only, never file names).
/// </summary>
internal sealed class TransitWatcher : NoticeWatcher
{
    private const string Key = TransitShelf.Key;
    private static readonly TimeSpan DebutLength = TimeSpan.FromSeconds(10);   // 2 was too short, then 6 (user 10-10)

    /// <summary>How often to look whether the files are still where they were (the lifted list says "not found").</summary>
    private static readonly TimeSpan GoneCheck = TimeSpan.FromSeconds(2);

    /// <summary>After a drag, when to look whether the files are still there (Explorer moves big files in its own time).</summary>
    private static readonly TimeSpan[] MovedChecks = [TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(20)];

    private static readonly string StoreFile = Path.Combine(AppPaths.DataDirectory, "transit.txt");

    /// <summary>Where a copied image (a screenshot has no file) is saved before it goes in — the copy card's "+". Ours: taken out by
    /// hand → deleted; one the list no longer has (moved away, or left from before) → deleted at start.</summary>
    public static readonly string PictureFolder = Path.Combine(AppPaths.DataDirectory, "transit-images");

    private readonly object _gate = new();
    private TransitShelf _shelf = new();
    private DateTimeOffset _since = DateTimeOffset.UtcNow;
    private int _adds;   // goes up with every drop: the card's look changes, so it debuts again
    private string[] _gone = [];
    private Timer? _check;

    public TransitWatcher(Func<Core.Localization.LanguageStrings> strings)
        : base(strings)
    {
    }

    public override void Start() => Guard(() =>
    {
        lock (_gate)
        {
            _shelf = TransitShelf.Load(File.Exists(StoreFile) ? File.ReadAllText(StoreFile) : null);
            AppLog.Write($"transit: {_shelf.Count} item(s) kept from last time");
            DeletePictures(LeftPictures());
            _gone = Missing();
            Show(debut: false);
        }

        _check ??= new Timer(_ => Guard(CheckGone), null, GoneCheck, GoneCheck);
    });

    protected override void Stop() => _check?.Dispose();

    /// <summary>A file deleted or moved by something else: its row says "not found" (it stays until taken out by hand).</summary>
    private void CheckGone()
    {
        lock (_gate)
        {
            var gone = Missing();
            if (!gone.SequenceEqual(_gone, StringComparer.OrdinalIgnoreCase))
            {
                AppLog.Write($"transit: {gone.Length} not found");
                _gone = gone;
                Show(debut: false);
            }
        }
    }

    /// <summary>Call under the lock.</summary>
    private string[] Missing() => [.. _shelf.Paths.Where(p => !File.Exists(p) && !Directory.Exists(p))];

    /// <summary>Whether a card is the transit station's.</summary>
    public static bool Owns(Core.Island.ActivityState card) => card.SourcePath == SystemNotice.SourcePrefix + Key;

    /// <summary>What is in the station now, oldest first.</summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_gate)
            {
                return [.. _shelf.Paths];
            }
        }
    }

    /// <summary>Files dropped on the pill (or the copy card's button): in they go, and the card debuts — even when they all were
    /// in it already (user 10-10: "got it, it's here").</summary>
    public void Add(IReadOnlyList<string> paths) => Guard(() =>
    {
        lock (_gate)
        {
            var added = _shelf.Add(paths);
            AppLog.Write($"transit: {paths.Count} dropped, {added} new, {_shelf.Count} in all");
            if (added > 0)
            {
                _gone = Missing();
                Save();
            }

            if (_shelf.Count > 0)
            {
                Show(debut: true);
            }
        }
    });

    /// <summary>Takes these out (the ✕), or everything with null (清空, or closing the card by hand).</summary>
    public void Remove(IReadOnlyList<string>? paths) => Guard(() =>
    {
        lock (_gate)
        {
            var before = _shelf.Paths.ToArray();
            var removed = paths is null ? _shelf.Count : _shelf.Remove(paths);
            if (paths is null)
            {
                _shelf.Clear();
            }

            if (removed > 0)
            {
                AppLog.Write($"transit: {removed} taken out, {_shelf.Count} left");
                DeletePictures(before.Where(p => !_shelf.Contains(p)));
                _gone = Missing();
                Save();
                Show(debut: false);
            }
        }
    });

    /// <summary>
    /// Dragged out and let go somewhere (<paramref name="target"/> = the folder, null = not a folder): what is no longer where it was
    /// got moved — out of the station, the card says "已移动 …" for 2 s; what was copied into the folder stays — "已复制 …" for 2 s
    /// (user 10-10). Only facts (<see cref="TransitOut"/>): Explorer reports "none" for moves it did itself and the result says nothing
    /// of a copy it then skipped, so this looks at the files, a few times (a big copy to a USB stick takes a while). Nothing sure by
    /// the last look (an app took them, it was cancelled): nothing is said.
    /// </summary>
    public void Dragged(IReadOnlyList<string> paths, string? target, DateTimeOffset since)
    {
        var dragged = paths;
        var snap = _snap;
        _ = Task.Run(async () =>
        {
            var before = snap is null ? null : await snap.ConfigureAwait(false);
            var waited = TimeSpan.Zero;
            foreach (var at in MovedChecks)
            {
                await Task.Delay(at - waited).ConfigureAwait(false);
                waited = at;

                // Explorer's progress window came up for it (a big copy, 10-11): this card follows it — "复制中 …" with its
                // percentage, then what it says at the end; Explorer's own card stays away (one thing, one card)
                if (WindowIsOurs(since))
                {
                    AppLog.Write($"transit: following Explorer's progress window ({at.TotalSeconds:0.#} s)");
                    while (Media.FileOperationWatcher.Window.Up)
                    {
                        Follow(Media.FileOperationWatcher.Running, dragged);
                        await Task.Delay(FollowEvery).ConfigureAwait(false);
                    }

                    Follow(null, dragged);
                    var gone = TransitOut.Check(paths, target, since, Exists, Made).Moved;
                    var ended = Media.FileOperationWatcher.Ended;
                    AppLog.Write($"transit: the window closed — {(ended is null ? "not surely through, nothing said" : "through")}, {gone.Count} moved away");
                    if (ended?.Words is { } words)
                    {
                        Tell(words, dragged, takeOut: false);
                    }

                    TakeOut(gone);
                    return;
                }

                var moved = TransitOut.Check(paths, target, since, Exists, Made, Written).Moved;
                var copied = InWhere(paths, target, since, before);
                if (at < FolderWait)
                {
                    copied = [.. copied.Where(p => !Directory.Exists(p))];   // a folder has no "modified" step: give the window time to be seen
                }

                if (moved.Count > 0)
                {
                    AppLog.Write($"transit: {moved.Count} moved away");
                    Tell(Text.FileMoved, moved, takeOut: true);
                }

                if (copied.Count > 0)
                {
                    AppLog.Write($"transit: {copied.Count} copied");
                    Tell(Text.FileCopied, copied, takeOut: false);
                }

                paths = [.. paths.Except(moved).Except(copied)];
                if (paths.Count == 0)
                {
                    return;
                }
            }

            AppLog.Write($"transit: {paths.Count} neither moved nor copied into a folder (nothing said)");
        });
    }

    /// <summary>
    /// What was copied — into the folder let go on, or one of its subfolders written to since the drag began. The item under the mouse
    /// isn't always readable (10-11 14:12:50: let go on a subfolder's icon on the desktop, read as the desktop's list), and a copy into
    /// a folder updates that folder's "modified".
    /// </summary>
    private static IReadOnlyList<string> InWhere(IReadOnlyList<string> paths, string? target, DateTimeOffset since, Snapshot? before)
    {
        if (target is null)
        {
            return [];
        }

        Func<string, bool?>? had = before is null ? null
            : p => before.Folders.Contains(Path.GetDirectoryName(p) ?? string.Empty) && before.Names.Contains(Path.GetFileName(p))
                ? before.There.Contains(p) : null;
        var copied = new List<string>(TransitOut.Check(paths, target, since, Exists, Made, Written, had).Copied);
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(target).Take(500))
            {
                if (copied.Count == paths.Count)
                {
                    break;
                }

                if (Directory.GetLastWriteTimeUtc(sub) >= (since - TransitOut.Slack).UtcDateTime)
                {
                    var left = paths.Except(copied).ToList();
                    copied.AddRange(TransitOut.Check(left, sub, since, Exists, Made, Written, had).Copied);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("transit: subfolders unreadable — " + ex.GetType().Name);
        }

        return copied;
    }

    /// <summary>Which of the dragged names were in which folders when the drag began: the open Explorer folders, the desktop, and
    /// the folders right inside them (a drop on a folder's icon).</summary>
    private sealed record Snapshot(HashSet<string> Folders, HashSet<string> Names, HashSet<string> There);

    private Task<Snapshot?>? _snap;

    /// <summary>A drag out of the station begins (before anything can be let go): note where these names already are.</summary>
    public void Snap(IReadOnlyList<string> paths)
    {
        _snap = Task.Run(async () =>
        {
            try
            {
                var started = DateTime.UtcNow;
                var open = await DropSpot.OpenFoldersAsync().ConfigureAwait(false);
                var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in open)
                {
                    folders.Add(folder);
                    try
                    {
                        folders.UnionWith(Directory.EnumerateDirectories(folder).Take(500));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // a folder we can't list: its subfolders fall back to the creation time
                    }
                }

                var names = paths.Select(p => Path.GetFileName(p.TrimEnd('\\'))).ToArray();
                var there = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in folders)
                {
                    foreach (var name in names)
                    {
                        var p = Path.Combine(folder, name);
                        if (Exists(p))
                        {
                            there.Add(p);
                        }
                    }
                }

                AppLog.Write($"transit: snapshot — {open.Count} open folder(s), {folders.Count} looked at, {there.Count} same name(s) already, "
                             + $"{(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
                return new Snapshot(folders, new HashSet<string>(names, StringComparer.OrdinalIgnoreCase), there);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                AppLog.Write("transit: snapshot failed — " + ex.GetType().Name);
                return null;
            }
        });
    }

    /// <summary>A copied folder is told from this check on: the progress window is looked for once a second.</summary>
    private static readonly TimeSpan FolderWait = TimeSpan.FromSeconds(2);

    /// <summary>The station's own progress window (it came up just after the drop), first seen after this drag began.</summary>
    private static bool WindowIsOurs(DateTimeOffset since)
        => Media.FileOperationWatcher.Window is { Owned: true, Opened: { } opened } && opened >= since - TransitOut.Slack;

    private static readonly TimeSpan FollowEvery = TimeSpan.FromMilliseconds(250);

    private ActivityStateLook? _following;

    /// <summary>The card while Explorer moves the files: its words and percentage, the dragged files' names (null = back to itself;
    /// the window asking a question = back to itself till it's answered).</summary>
    private void Follow(Core.Island.ActivityState? running, IReadOnlyList<string> dragged) => Guard(() =>
    {
        var now = running is { Words: { } words } ? new ActivityStateLook(words, running.Done, running.Total, dragged) : null;
        lock (_gate)
        {
            if (now == _following)
            {
                return;
            }

            if (now?.Words != _following?.Words)
            {
                _saids++;   // new words: a look of its own (it debuts)
            }

            _following = now;
            Show(debut: false);
        }
    });

    private sealed record ActivityStateLook(string Words, long? Done, long? Total, IReadOnlyList<string> Paths);

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>
    /// When the file or folder at a path was made — only for folders the drag's snapshot didn't cover (<see cref="Snap"/>): Windows
    /// hands a creation time on to a file of the same name put in the same folder within 15 s ("tunnelling"), and NTFS's "changed"
    /// doesn't help either — Explorer's copy takes the original's (10-11 logs).
    /// </summary>
    private static DateTimeOffset? Made(string path)
        => File.Exists(path) ? File.GetCreationTimeUtc(path) : Directory.Exists(path) ? Directory.GetCreationTimeUtc(path) : null;

    private static DateTimeOffset? Written(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;

    /// <summary>Moved away while the card followed Explorer's window: out of the station (what happened was said already).</summary>
    private void TakeOut(IReadOnlyList<string> paths) => Guard(() =>
    {
        lock (_gate)
        {
            if (paths.Count > 0 && _shelf.Remove(paths) > 0)
            {
                _gone = Missing();
                Save();
                Show(debut: false);
            }
        }
    });

    /// <summary>How long "已复制 / 已移动" shows (user 10-10: 2 s).</summary>
    private static readonly TimeSpan SaidLength = TimeSpan.FromSeconds(2);

    private (string Words, IReadOnlyList<string> Paths)? _said;
    private int _saids;

    /// <summary>The card says what happened for <see cref="SaidLength"/> (a debut), then is itself again — or goes, when it's empty.</summary>
    private void Tell(string words, IReadOnlyList<string> paths, bool takeOut) => Guard(() =>
    {
        int said;
        lock (_gate)
        {
            if (takeOut && _shelf.Remove(paths) > 0)
            {
                _gone = Missing();
                Save();
            }

            _said = (words, paths);
            said = ++_saids;
            Show(debut: false);
        }

        _ = Task.Delay(SaidLength).ContinueWith(_ => Guard(() =>
        {
            lock (_gate)
            {
                if (_saids == said)
                {
                    _said = null;
                    Show(debut: false);
                }
            }
        }), TaskScheduler.Default);
    });

    /// <summary>Call under the lock.</summary>
    private void Show(bool debut)
    {
        if (_said is { } said)
        {
            // "已复制 DSC02580.ARW +1": its own look, so it debuts (2 s), green like other things done (user 10-11); kept up for
            // that even when everything was moved out
            var told = Saying(said.Words, said.Paths, "out" + _saids, SaidLength);
            told.State = "done";
            Board.Hold(Key, told);
            return;
        }

        if (_following is { } follow)
        {
            // Explorer moving them (its progress window, 10-11): "复制中 DSC02580.ARW +1" and its percentage
            var moving = Saying(follow.Words, follow.Paths, "follow" + _saids, SaidLength);
            moving.Done = follow.Done;
            moving.Total = follow.Total;
            Board.Hold(Key, moving);
            return;
        }

        if (_shelf.Count == 0)
        {
            Board.Clear(Key);
            return;
        }

        if (debut || !Board.Has(Key, DateTimeOffset.UtcNow))
        {
            _since = DateTimeOffset.UtcNow;
        }

        if (debut)
        {
            _adds++;
            (_look, _lookFor) = ("in" + _adds, DebutLength);
        }

        var s = Text;
        var card = SystemNotice.Make(s.NoticeTransit, NoticeGlyphs.Transit, _since,
            msg: TransitShelf.CountText(_shelf.Count, s.NoticeTransitOne, s.NoticeTransitFiles));
        card.Files = [.. _shelf.Paths];
        card.Gone = _gone;
        card.Look = _look;   // kept until the next change: going back to an older look would end a debut early
        card.DebutFor = _lookFor;
        card.ByHand = true;
        Board.Hold(Key, card);
    }

    /// <summary>Call under the lock. The card saying something of these files: words first, the first one's name grey, "+N" apart
    /// (the same as Explorer's card — a long name can't push the +N off).</summary>
    private Core.Island.ActivityState Saying(string words, IReadOnlyList<string> paths, string look, TimeSpan debut)
    {
        var card = SystemNotice.Make(words, NoticeGlyphs.Transit, _since,
            msg: paths.Count == 0 ? null : Path.GetFileName(paths[0].TrimEnd('\\')));
        card.MoreCount = Math.Max(0, paths.Count - 1);
        card.Files = [.. _shelf.Paths];
        card.Gone = _gone;
        card.Look = look;
        card.DebutFor = debut;
        card.ByHand = true;
        return card;
    }

    private string _look = "in0";
    private TimeSpan? _lookFor;
    private int _backs;

    /// <summary>How long it stays up after being put back on its place (user 10-11: 2 s — else a pinned card took the big pill at once).</summary>
    private static readonly TimeSpan BackLength = TimeSpan.FromSeconds(2);

    /// <summary>Dragged out and put back on its place: a short debut, so it holds the big pill for <see cref="BackLength"/>.</summary>
    public void PutBack() => Guard(() =>
    {
        lock (_gate)
        {
            (_look, _lookFor) = ("back" + ++_backs, BackLength);
            Show(debut: false);
        }
    });

    /// <summary>Call under the lock. The saved images in <see cref="PictureFolder"/> the list doesn't have.</summary>
    private string[] LeftPictures()
    {
        try
        {
            return Directory.Exists(PictureFolder) ? [.. Directory.EnumerateFiles(PictureFolder).Where(p => !_shelf.Contains(p))] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Deletes those of <paramref name="paths"/> that are our saved images (anything else is the user's and stays).</summary>
    private static void DeletePictures(IEnumerable<string> paths)
    {
        var folder = PictureFolder.TrimEnd('\\') + "\\";
        var deleted = 0;
        foreach (var path in paths.Where(p => p.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("transit: couldn't delete a saved image — " + ex.Message);
            }
        }

        if (deleted > 0)
        {
            AppLog.Write($"transit: {deleted} saved image(s) deleted");
        }
    }

    /// <summary>Call under the lock. A failed write only loses the list over a restart.</summary>
    private void Save()
    {
        try
        {
            File.WriteAllText(StoreFile, _shelf.Save());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("transit: couldn't save the list — " + ex.Message);
        }
    }
}
