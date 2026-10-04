using IsleBar.Core.Island;
using Windows.Media.Control;

namespace IsleBar.App.Media;

/// <summary>
/// Turns what's playing now (Windows media controls — what Spotify, browser YouTube, etc. report)
/// into an island item. Nothing is written to disk; memory only (re-read every second).
/// <para>
/// Which one (user 10-04): <b>the player you started last, while it plays; when nothing plays, the one shown last, as paused</b>
/// (gone after <see cref="PausedLinger"/>). Two guards against what browsers report while switching videos (measured on
/// Edge/Bilibili 10-04 — Windows sees one session for the whole browser):
/// <list type="bullet">
/// <item>for 1–4 s the session says "paused" with the title of some other video (often one paused in another tab), until the new
/// video plays — so a player that stops with a <i>different</i> title than the one shown is switching, and still counts as
/// playing for up to <see cref="SwitchGrace"/> (otherwise background music took over for those seconds);</item>
/// <item>such a stale title can also blink "playing" for an instant — so the pill moves to a different item only once it has played
/// for <see cref="Confirm"/>, from any player (one rule for all). Pause / resume of the item shown applies at once.</item>
/// </list>
/// </para>
/// <para>
/// "Started" also counts a player that keeps playing but changes title <b>while its window is in front</b> — you picked another
/// song or video in it. The same change with the window in the back is the player moving on by itself (next track, next episode):
/// it doesn't take the pill, but if it isn't the player shown, its new item peeks in for <see cref="PeekFor"/> (user 10-04).
/// Players don't say whether a change was automatic, and NetEase Cloud Music reports no position, so the window in front is the
/// only sign of your choice.
/// </para>
/// </summary>
internal sealed class MediaWatcher : IDisposable
{
    // Event-driven: track/state/timeline changes wake the loop instantly (SafetyPoll is only a slow backstop for missed events
    // and reconnection). A 1 s poll used to make a track change or a play/pause button press show up to ~1 s late (09-30 → 10-01).
    private static readonly TimeSpan SafetyPoll = TimeSpan.FromSeconds(1);   // also the playback-position refresh cadence for the progress bar (track/state changes come from events)

    /// <summary>Keep showing this long after everything stopped — so it can be resumed with the island's ⏯ button.</summary>
    private static readonly TimeSpan PausedLinger = TimeSpan.FromSeconds(30);

    /// <summary>A different item must play this long before the pill moves to it.</summary>
    private static readonly TimeSpan Confirm = TimeSpan.FromSeconds(0.5);

    /// <summary>How long a player switching to another video/track still counts as playing (the slowest load seen was ~4 s).</summary>
    private static readonly TimeSpan SwitchGrace = TimeSpan.FromSeconds(8);

    /// <summary>How long a background player's next track/video peeks in before the pill goes back.</summary>
    private static readonly TimeSpan PeekFor = TimeSpan.FromSeconds(4);

    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _wake = new(0, 1);

    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlayback;
    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onProperties;
    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> _onSession;
    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs> _onSessions;

    private readonly Func<bool> _browsers;   // the "browser media" setting, read on every pass so switching it applies at once

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private List<GlobalSystemMediaTransportControlsSession> _subscribed = [];

    /// <summary>Per player (app ID): its current playing run. Loop thread only.</summary>
    private readonly Dictionary<string, Run> _runs = [];

    private Reading? _shown;                  // what the pill shows when nothing peeks in
    private string? _shownId;                 // its player
    private volatile string? _controlId;      // the player on the pill right now (shown or peeking), for ⏯ / ⏮ / ⏭ (read from another thread)
    private (string Id, string Title, DateTimeOffset Since)? _candidate;   // a different item waiting out Confirm
    private (string Id, string Title, DateTimeOffset Since)? _peekCandidate;   // a background player's next item waiting out Confirm
    private (Reading Read, string Id, DateTimeOffset Until)? _peek;            // a background player's next item, on the pill for a moment
    private DateTimeOffset _lastActive = DateTimeOffset.MinValue;          // last time any player played (or was switching)

    public MediaWatcher(Func<bool> browsers)
    {
        _browsers = browsers;
        _onPlayback = (_, _) => SignalRefresh();
        _onProperties = (_, _) => SignalRefresh();
        _onSession = (_, _) => SignalRefresh();
        _onSessions = (_, _) => SignalRefresh();
    }

    /// <summary>The music item to show now. Null if nothing is playing. Safe to read from any thread.</summary>
    public ActivityState? Current { get; private set; }

    /// <summary>Album art for the current track (only if the player provides it). When the track changes, <see cref="CurrentArtKey"/> changes too.</summary>
    public Windows.Storage.Streams.IRandomAccessStreamReference? CurrentArt { get; private set; }

    public string? CurrentArtKey { get; private set; }

    public void Start() => _ = Task.Run(LoopAsync);

    /// <summary>Wake the loop to re-read now (from a media event or a button press). Coalesced — extra signals collapse into one read.</summary>
    private void SignalRefresh()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // already pending — one re-read covers it
        }
    }

    /// <summary>Wake the loop again after <paramref name="delay"/> (so a waiting item is checked on time, not at the next poll).</summary>
    private void SignalLater(TimeSpan delay)
        => _ = Task.Delay(delay + TimeSpan.FromMilliseconds(50), _stop.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                SignalRefresh();
            }
        }, TaskScheduler.Default);

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (_manager is null)
                {
                    _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    _manager.CurrentSessionChanged += _onSession;
                    _manager.SessionsChanged += _onSessions;
                }

                await RefreshAsync(_manager);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // any failure (a session closing mid-read throws all sorts): start over — an uncaught one ended this loop and left
                // the last track on the pill for good (review 10-03)
                _peek = null;
                Show(null);
                Publish();
                _runs.Clear();
                _candidate = null;
                _peekCandidate = null;
                try
                {
                    SyncSubscriptions([]);   // on a session that just closed this can throw too
                }
                catch (Exception unsubscribeEx) when (unsubscribeEx is not OutOfMemoryException)
                {
                    _subscribed = [];
                }

                if (_manager is { } stale)
                {
                    try
                    {
                        // the old manager kept calling these after it was dropped, and every reconnect added another pair (review 10-03)
                        stale.CurrentSessionChanged -= _onSession;
                        stale.SessionsChanged -= _onSessions;
                    }
                    catch (Exception unhookEx) when (unhookEx is not OutOfMemoryException)
                    {
                    }
                }

                _manager = null;   // reconnect on the next wake
            }

            try
            {
                await _wake.WaitAsync(SafetyPoll, _stop.Token);   // wake on a media event, or re-read after the safety interval
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One player as read now. <see cref="Read"/> is null while it reports no title.</summary>
    private sealed record Seen(GlobalSystemMediaTransportControlsSession Session, string Id, bool Playing, Reading? Read);

    /// <summary>
    /// A player's playing run. <see cref="Started"/> orders players (the last started wins); <see cref="SwitchUntil"/> is set while it
    /// is switching items; <see cref="ShownTitle"/> is the title of its item the pill last showed; <see cref="Title"/> the one it
    /// last played.
    /// </summary>
    private sealed class Run
    {
        public DateTimeOffset Started;
        public bool Playing;
        public DateTimeOffset? SwitchUntil;
        public string? ShownTitle;
        public string? Title;

        public bool Active(DateTimeOffset now) => Playing || SwitchUntil > now;
    }

    private async Task RefreshAsync(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var now = DateTimeOffset.UtcNow;
        var browsers = _browsers();
        var sessions = manager.GetSessions().Where(s => browsers || !MediaSources.IsBrowser(s.SourceAppUserModelId)).ToList();
        SyncSubscriptions(sessions);

        // one entry per player (two sessions of one app: the playing one)
        var seen = new List<Seen>();
        foreach (var session in sessions)
        {
            var playing = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            seen.Add(new Seen(session, session.SourceAppUserModelId, playing, await ReadAsync(session, playing)));
        }

        seen = [.. seen.GroupBy(s => s.Id).Select(g => g.OrderByDescending(s => s.Playing && s.Read is not null).First())];

        var movedOn = UpdateRuns(seen, now, ForegroundProcess());

        var winner = seen.Where(s => _runs[s.Id].Active(now)).OrderByDescending(s => _runs[s.Id].Started).FirstOrDefault();
        if (winner is not null)
        {
            _lastActive = now;
            if (winner.Playing && winner.Read is { } playing)
            {
                Offer(playing, winner.Id, now);
            }
            else
            {
                _candidate = null;   // switching: keep what's shown until it plays something
            }
        }
        else if (_shown is { } shown && now - _lastActive <= PausedLinger)
        {
            // nothing plays: keep the item shown last, as paused (re-read if its player still reports it, for the position)
            _candidate = null;
            var same = seen.FirstOrDefault(s => s.Id == _shownId && s.Read?.State.Name == shown.State.Name)?.Read;
            Show(same ?? shown with { State = Paused(shown.State) });
        }
        else
        {
            _candidate = null;
            Show(null);
        }

        UpdatePeek(seen, movedOn, now);
        Publish();
    }

    /// <summary>
    /// A background player that moved on by itself (and isn't the one shown) peeks in with its new item for <see cref="PeekFor"/>,
    /// once that item has played for <see cref="Confirm"/> (same guard against stale titles as everywhere else).
    /// </summary>
    private void UpdatePeek(List<Seen> seen, List<string> movedOn, DateTimeOffset now)
    {
        foreach (var id in movedOn.Where(id => id != _shownId))
        {
            _peekCandidate = (id, _runs[id].Title ?? string.Empty, now);
            SignalLater(Confirm);
        }

        if (_peekCandidate is { } waiting)
        {
            var player = seen.FirstOrDefault(s => s.Id == waiting.Id);
            if (waiting.Id == _shownId || player is not { Playing: true, Read: { } read } || read.State.Name != waiting.Title)
            {
                _peekCandidate = null;   // it became the one shown, stopped, or moved on again (a new candidate then)
            }
            else if (now - waiting.Since >= Confirm)
            {
                _peekCandidate = null;
                _peek = (read, waiting.Id, now + PeekFor);
                SignalLater(PeekFor);
            }
        }

        if (_peek is { } peek)
        {
            var fresh = seen.FirstOrDefault(s => s.Id == peek.Id)?.Read;
            if (now >= peek.Until || peek.Id == _shownId || fresh is null || fresh.State.Name != peek.Read.State.Name)
            {
                _peek = null;   // over, or the pill shows that player now anyway, or it changed again
            }
            else
            {
                _peek = peek with { Read = fresh };   // keep its position / play state current
            }
        }
    }

    /// <summary>Put the peeking item, else the shown one, on the pill.</summary>
    private void Publish()
    {
        var (read, id) = _peek is { } peek ? (peek.Read, peek.Id) : (_shown, _shownId);
        _controlId = id;
        Current = read?.State;
        CurrentArt = read?.Art;
        CurrentArtKey = read?.ArtKey;
    }

    /// <summary>
    /// Start, continue or end each player's playing run from what it reports now. Returns the players that moved on to another
    /// item by themselves (title changed while playing, window not in front).
    /// </summary>
    private List<string> UpdateRuns(List<Seen> seen, DateTimeOffset now, string? foreground)
    {
        var movedOn = new List<string>();
        foreach (var gone in _runs.Keys.Except(seen.Select(s => s.Id)).ToList())
        {
            _runs.Remove(gone);   // the player closed: no grace
        }

        foreach (var s in seen)
        {
            if (!_runs.TryGetValue(s.Id, out var run))
            {
                _runs[s.Id] = run = new Run { Started = now };
            }

            if (s.Playing && s.Read is { } read)
            {
                var title = read.State.Name;
                if (!run.Active(now))
                {
                    run.Started = now;   // a new run; a run that was only switching keeps its place
                }
                else if (run.Playing && run.Title is not null && run.Title != title)
                {
                    // kept playing, another item: picked in its window (in front) → your latest choice; else it moved on by itself
                    if (MediaSources.IsApp(s.Id, foreground))
                    {
                        run.Started = now;
                    }
                    else
                    {
                        movedOn.Add(s.Id);
                    }
                }

                run.Playing = true;
                run.SwitchUntil = null;
                run.Title = title;
            }
            else if (run.Playing)
            {
                run.Playing = false;
                // Stopped showing another title than the one on the pill → switching to another video/track, not paused by you
                if (run.ShownTitle is { } shownTitle && s.Read?.State.Name != shownTitle)
                {
                    run.SwitchUntil = now + SwitchGrace;
                    SignalLater(SwitchGrace);
                }
            }
        }

        return movedOn;
    }

    /// <summary>The process name of the window in front (null if unknown) — the sign of which player you are using.</summary>
    private static string? ForegroundProcess()
    {
        try
        {
            var window = Interop.NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var pid) == 0 || pid == 0)
            {
                return null;
            }

            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;   // it just closed
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>A playing item that should be on the pill: the same item updates at once, a different one after <see cref="Confirm"/>.</summary>
    private void Offer(Reading read, string id, DateTimeOffset now)
    {
        var title = read.State.Name ?? string.Empty;
        if (_shown is null || (_shownId == id && _shown.State.Name == title))
        {
            _candidate = null;
            Show(read, id);
            return;
        }

        if (_candidate is not { } waiting || waiting.Id != id || waiting.Title != title)
        {
            _candidate = (id, title, now);
            SignalLater(Confirm);
            return;
        }

        if (now - waiting.Since >= Confirm)
        {
            _candidate = null;
            Show(read, id);
        }
    }

    private void Show(Reading? read, string? id = null)
    {
        if (read is not null && id is not null)
        {
            _shownId = id;
            if (_runs.TryGetValue(id, out var run))
            {
                run.ShownTitle = read.State.Name;
            }
        }
        else if (read is null)
        {
            _shownId = null;
        }

        _shown = read;   // on the pill at the next Publish
    }

    /// <summary>The item as paused (its player no longer reports it). Copies the fields <see cref="ReadAsync"/> fills.</summary>
    private static ActivityState Paused(ActivityState state) => new()
    {
        RawKind = state.RawKind,
        Total = state.Total,
        Done = state.Done,
        Name = state.Name,
        Msg = state.Msg,
        State = state.State,
        Stage = "paused",
        SourcePath = state.SourcePath,
        UpdatedAt = state.UpdatedAt,
    };

    /// <summary>Follow every player's events (not just the shown one's), so a player starting or stopping is seen at once.</summary>
    private void SyncSubscriptions(List<GlobalSystemMediaTransportControlsSession> sessions)
    {
        foreach (var old in _subscribed.Where(o => !sessions.Any(s => ReferenceEquals(s, o))))
        {
            old.PlaybackInfoChanged -= _onPlayback;
            old.MediaPropertiesChanged -= _onProperties;
        }

        foreach (var added in sessions.Where(s => !_subscribed.Any(o => ReferenceEquals(s, o))))
        {
            added.PlaybackInfoChanged += _onPlayback;
            added.MediaPropertiesChanged += _onProperties;
        }

        _subscribed = sessions;
    }

    /// <summary>One reading: the item plus its album art — applied together, so the cover always matches the title.</summary>
    private sealed record Reading(ActivityState State, Windows.Storage.Streams.IRandomAccessStreamReference? Art, string? ArtKey);

    private static async Task<Reading?> ReadAsync(GlobalSystemMediaTransportControlsSession session, bool playing)
    {
        var props = await session.TryGetMediaPropertiesAsync();
        var title = props?.Title?.Trim() ?? string.Empty;
        var artist = props?.Artist?.Trim() ?? string.Empty;
        if (props is null || title.Length == 0)
        {
            return null;
        }

        // Playback position → bar (same progress bar as the timer). Apps report position only occasionally, so add the time elapsed since the last report.
        long? total = null, done = null;
        var timeline = session.GetTimelineProperties();
        var length = timeline.EndTime - timeline.StartTime;
        if (length > TimeSpan.Zero)
        {
            var position = timeline.Position - timeline.StartTime;
            if (playing)
            {
                position += DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            }

            total = (long)length.TotalMilliseconds;
            done = (long)Math.Clamp(position.TotalMilliseconds, 0, length.TotalMilliseconds);
        }

        return new Reading(new ActivityState
        {
            RawKind = ActivityState.KindMusic,
            Total = total,
            Done = done,
            // Track title (bold) · artist (dim) — same as the preview. Long text scrolls once to the end and stops, so the artist is included too (user feedback 09-30)
            Name = title,
            Msg = artist.Length > 0 ? artist : null,
            State = "run",
            Stage = playing ? "playing" : "paused",
            SourcePath = "media:" + session.SourceAppUserModelId,   // used by the island to decide "is it the same item"
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),      // sort behind other items (least urgent)
        }, props.Thumbnail, $"{session.SourceAppUserModelId}|{props.Title}|{props.AlbumTitle}");
    }

    /// <summary>Play/pause / previous / next on the player on the pill. Failures are silently ignored.</summary>
    public async Task SendAsync(MediaAction action)
    {
        try
        {
            // a manager of its own when the loop is between reconnects — setting _manager here left it without its events (review 10-03)
            var manager = _manager ?? await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var id = _controlId;   // the player on the pill — the one peeking in, if any
            var candidates = manager.GetSessions().Where(s => s.SourceAppUserModelId == id).ToList();
            var session = candidates.FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                          ?? candidates.FirstOrDefault();
            if (session is null)
            {
                return;
            }

            _ = action switch
            {
                MediaAction.PlayPause => await session.TryTogglePlayPauseAsync(),
                MediaAction.Previous => await session.TrySkipPreviousAsync(),
                _ => await session.TrySkipNextAsync(),
            };

            SignalRefresh();   // reflect the new state/track right away instead of waiting for the event or the safety poll
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            if (_manager is not null)
            {
                _manager.CurrentSessionChanged -= _onSession;
                _manager.SessionsChanged -= _onSessions;
            }

            SyncSubscriptions([]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // a session that just closed; quitting must go on (it stopped the rest of the clean-up — review 10-03)
        }

        _stop.Dispose();
        _wake.Dispose();
    }
}

internal enum MediaAction
{
    PlayPause,
    Previous,
    Next,
}
