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
        _ = Task.Run(async () =>
        {
            var waited = TimeSpan.Zero;
            foreach (var at in MovedChecks)
            {
                await Task.Delay(at - waited).ConfigureAwait(false);
                waited = at;
                var (moved, copied) = TransitOut.Check(paths, target, since, p => File.Exists(p) || Directory.Exists(p), Made);
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

    private static DateTimeOffset? Made(string path)
        => File.Exists(path) ? File.GetCreationTimeUtc(path) : Directory.Exists(path) ? Directory.GetCreationTimeUtc(path) : null;

    /// <summary>How long "已复制 / 已移动" shows (user 10-10: 2 s).</summary>
    private static readonly TimeSpan SaidLength = TimeSpan.FromSeconds(2);

    private (string Words, string Names)? _said;
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

            _said = (words, TransitOut.Names(paths));
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
            // "已复制 DSC02580.ARW +1": its own look, so it debuts (2 s); kept up for that even when everything was moved out
            var told = SystemNotice.Make(said.Words, NoticeGlyphs.Transit, _since, msg: said.Names);
            told.Files = [.. _shelf.Paths];
            told.Gone = _gone;
            told.Look = "out" + _saids;
            told.DebutFor = SaidLength;
            told.ByHand = true;
            Board.Hold(Key, told);
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
