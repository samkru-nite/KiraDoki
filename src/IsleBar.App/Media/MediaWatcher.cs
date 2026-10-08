using IsleBar.Core.Island;
using Windows.Media.Control;

namespace IsleBar.App.Media;

/// <summary>
/// Turns what's playing (Windows media controls — what Spotify, browser YouTube, etc. report) into music cards: one per player,
/// by the rules in <see cref="MediaCards"/> (user 10-07). Nothing is written to disk; memory only. A browser is one player:
/// Windows gives it one session, naming the tab it picked (usually the last one started) — the other tabs can't be seen.
/// </summary>
internal sealed class MediaWatcher : IDisposable
{
    // Event-driven: track/state/timeline changes wake the loop instantly (SafetyPoll is only a slow backstop for missed events
    // and reconnection). A 1 s poll used to make a track change or a play/pause button press show up to ~1 s late (09-30 → 10-01).
    private static readonly TimeSpan SafetyPoll = TimeSpan.FromSeconds(1);   // also the playback-position refresh cadence for the progress bar (track/state changes come from events)

    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _wake = new(0, 1);

    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlayback;
    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onProperties;
    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> _onSession;
    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs> _onSessions;

    private readonly Func<bool> _browsers;   // the "browser media" setting, read on every pass so switching it applies at once

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private List<GlobalSystemMediaTransportControlsSession> _subscribed = [];

    /// <summary>The card rules. Loop thread only.</summary>
    private readonly MediaCards _cards = new();

    /// <summary>Each card's cover (card key → art), replaced whole so the UI thread may read it.</summary>
    private IReadOnlyDictionary<string, (Windows.Storage.Streams.IRandomAccessStreamReference Art, string Key)> _art =
        new Dictionary<string, (Windows.Storage.Streams.IRandomAccessStreamReference, string)>();

    public MediaWatcher(Func<bool> browsers)
    {
        _browsers = browsers;
        _onPlayback = (_, _) => SignalRefresh();
        _onProperties = (_, _) => SignalRefresh();
        _onSession = (_, _) => SignalRefresh();
        _onSessions = (_, _) => SignalRefresh();
    }

    /// <summary>The music cards now (one per player). Safe to read from any thread.</summary>
    public IReadOnlyList<ActivityState> Cards { get; private set; } = [];

    /// <summary>A card's album art and a key that changes with it, or null when its player gives none.</summary>
    public (Windows.Storage.Streams.IRandomAccessStreamReference Art, string Key)? ArtOf(string? cardKey)
        => cardKey is not null && _art.TryGetValue(cardKey, out var art) ? art : null;

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
                _cards.Update([], DateTimeOffset.UtcNow);
                Cards = [];
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

    private async Task RefreshAsync(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var browsers = _browsers();
        var sessions = manager.GetSessions().Where(s => browsers || !MediaSources.IsBrowser(s.SourceAppUserModelId)).ToList();
        SyncSubscriptions(sessions);

        // one reading per player (two sessions of one app: the playing one)
        var seen = new List<(MediaReading Reading, Reading? Read)>();
        foreach (var session in sessions)
        {
            var playing = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var read = await ReadAsync(session, playing);
            seen.Add((new MediaReading(session.SourceAppUserModelId, playing, read?.State), read));
        }

        seen = [.. seen.GroupBy(s => s.Reading.Source).Select(g => g.OrderByDescending(s => s.Reading.Playing && s.Read is not null).First())];
        if (_cards.Update(seen.Select(s => s.Reading).ToList(), DateTimeOffset.UtcNow))
        {
            SignalLater(MediaCards.Confirm);   // a new title is waiting: look again once it has had its half second
        }

        // a card's cover is its player's current art while the card shows that player's current title
        var art = new Dictionary<string, (Windows.Storage.Streams.IRandomAccessStreamReference, string)>(_art.Count);
        foreach (var card in _cards.Cards)
        {
            if (card.SourcePath is { } key && seen.FirstOrDefault(s => s.Reading.Source == _cards.SourceOf(key)).Read is { Art: { } cover, ArtKey: { } artKey } read
                && read.State.Name == card.Name)
            {
                art[key] = (cover, artKey);
            }
            else if (card.SourcePath is { } kept && _art.TryGetValue(kept, out var old))
            {
                art[kept] = old;   // paused / no title right now: keep the cover it had
            }
        }

        _art = art;
        Cards = _cards.Cards;
    }

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
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),      // sort behind other items (least urgent)
            AppId = session.SourceAppUserModelId,                   // the lifted card names the player and shows its icon (F2b)
            AppName = string.IsNullOrWhiteSpace(session.SourceAppUserModelId) ? null : Interop.AppNames.For(session.SourceAppUserModelId),
        }, props.Thumbnail, $"{session.SourceAppUserModelId}|{props.Title}|{props.AlbumTitle}");
    }

    /// <summary>Play/pause / previous / next on the player of the card <paramref name="cardKey"/>. Failures are silently ignored.</summary>
    public async Task SendAsync(MediaAction action, string cardKey)
    {
        try
        {
            // a manager of its own when the loop is between reconnects — setting _manager here left it without its events (review 10-03)
            var manager = _manager ?? await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var id = MediaCards.SourceIn(cardKey);   // not _cards: that belongs to the loop thread
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
