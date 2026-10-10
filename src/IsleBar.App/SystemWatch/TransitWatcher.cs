using IsleBar.App.Interop;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// The transit station card (中转站, TODO ④, user 10-10; the list in <see cref="TransitShelf"/>). Files dropped on the pill go in
/// (only their paths), one status card says "中转站 3 个文件", no colour; something new going in → debut 2 s. Dragging the big pill
/// drags them all out; what the receiver moved away leaves (<see cref="Dragged"/>), what it copied stays. Empty = no card. Kept over a
/// restart in <c>%LOCALAPPDATA%\IsleBar\transit.txt</c>. Log "transit: …" (counts only, never file names).
/// </summary>
internal sealed class TransitWatcher : NoticeWatcher
{
    private const string Key = "transit";
    private static readonly TimeSpan DebutLength = TimeSpan.FromSeconds(2);

    /// <summary>After a drag, when to look whether the files are still there (Explorer moves big files in its own time).</summary>
    private static readonly TimeSpan[] MovedChecks = [TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(20)];

    private static readonly string StoreFile = Path.Combine(AppPaths.DataDirectory, "transit.txt");

    private readonly object _gate = new();
    private TransitShelf _shelf = new();
    private DateTimeOffset _since = DateTimeOffset.UtcNow;
    private int _adds;   // goes up with every drop: the card's look changes, so it debuts again

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
            Show(debut: false);
        }
    });

    protected override void Stop()
    {
    }

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

    /// <summary>Files dropped on the pill (or the copy card's button): in they go; anything new → the card debuts.</summary>
    public void Add(IReadOnlyList<string> paths) => Guard(() =>
    {
        lock (_gate)
        {
            var added = _shelf.Add(paths);
            AppLog.Write($"transit: {paths.Count} dropped, {added} new, {_shelf.Count} in all");
            if (added > 0)
            {
                Save();
                Show(debut: true);
            }
        }
    });

    /// <summary>Takes these out (the ✕), or everything with null (清空, or closing the card by hand).</summary>
    public void Remove(IReadOnlyList<string>? paths) => Guard(() =>
    {
        lock (_gate)
        {
            var removed = paths is null ? _shelf.Count : _shelf.Remove(paths);
            if (paths is null)
            {
                _shelf.Clear();
            }

            if (removed > 0)
            {
                AppLog.Write($"transit: {removed} taken out, {_shelf.Count} left");
                Save();
                Show(debut: false);
            }
        }
    });

    /// <summary>
    /// Dragged out and dropped somewhere: whatever is no longer where it was got moved — out of the station; copied ones stay.
    /// Explorer may report "none" for a move it did itself (an optimised move), so this goes by whether the files are still there,
    /// not by the drop's result; and it looks a few times, as a big move across folders takes a moment.
    /// </summary>
    public void Dragged(IReadOnlyList<string> paths)
    {
        _ = Task.Run(async () =>
        {
            var waited = TimeSpan.Zero;
            foreach (var at in MovedChecks)
            {
                await Task.Delay(at - waited).ConfigureAwait(false);
                waited = at;
                var gone = paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToArray();
                if (gone.Length > 0)
                {
                    AppLog.Write($"transit: {gone.Length} moved away");
                    Remove(gone);
                    paths = [.. paths.Except(gone)];
                }

                if (paths.Count == 0)
                {
                    return;
                }
            }
        });
    }

    /// <summary>Call under the lock.</summary>
    private void Show(bool debut)
    {
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
        }

        var s = Text;
        var card = SystemNotice.Make(s.NoticeTransit, NoticeGlyphs.Transit, _since,
            msg: TransitShelf.CountText(_shelf.Count, s.NoticeTransitOne, s.NoticeTransitFiles));
        card.Look = "in" + _adds;
        card.DebutFor = debut ? DebutLength : null;
        Board.Hold(Key, card);
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
