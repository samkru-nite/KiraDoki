namespace IsleBar.Core.Island;

/// <summary>What one media player (a Windows media session) reports now.</summary>
/// <param name="Source">The player's app ID. A browser is one player however many tabs play (Windows gives it one session).</param>
/// <param name="State">The item it reports (a fresh object each read), or null while it reports no title.</param>
public sealed record MediaReading(string Source, bool Playing, ActivityState? State);

/// <summary>
/// The media cards (TODO 7.9 D, user 10-07 — replaces the old "last started wins / peek / switch grace" rules):
/// <list type="bullet">
/// <item><b>One card per player</b>, made when it starts playing. A new song or video keeps the card.</item>
/// <item>A different title shows once it has been reported for <see cref="Confirm"/> (a browser switching videos first reports a
/// stale title from another tab). Pause / resume / position of the same title apply at once.</item>
/// <item>Paused for <see cref="PausedFor"/> → the card goes. Playing again later = a new card (its key has a new run number, so it
/// lines up at the back of the stack like any new card).</item>
/// <item>The player closed → the card goes at once.</item>
/// </list>
/// Pure and deterministic: the watcher feeds readings, this says which cards there are.
/// </summary>
public sealed class MediaCards
{
    public static readonly TimeSpan Confirm = TimeSpan.FromSeconds(0.5);
    public static readonly TimeSpan PausedFor = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, Entry> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _runs = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public required string Key;
        public required ActivityState Shown;
        public string? Pending;
        public DateTimeOffset PendingSince;
        public DateTimeOffset? PausedSince;
    }

    /// <summary>The cards now, in the order the players first played. Each state's <see cref="ActivityState.SourcePath"/> is its card key.</summary>
    public IReadOnlyList<ActivityState> Cards { get; private set; } = [];

    /// <summary>The player behind a card key (for play/pause and the cover). Null when the card is gone.</summary>
    public string? SourceOf(string key) => _cards.FirstOrDefault(c => c.Value.Key == key).Key;

    /// <summary>The player named in a card key (<c>media:{player}#{run}</c>).</summary>
    public static string? SourceIn(string? key)
        => key is not null && key.StartsWith("media:", StringComparison.Ordinal) && key.LastIndexOf('#') is var hash and > 6
            ? key[6..hash]
            : null;

    /// <summary>One read of every player. Returns whether a title is waiting out <see cref="Confirm"/> (read again soon).</summary>
    public bool Update(IReadOnlyList<MediaReading> readings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var waiting = false;
        foreach (var gone in _cards.Keys.Where(s => !readings.Any(r => r.Source == s)).ToList())
        {
            _cards.Remove(gone);   // the player closed
        }

        foreach (var r in readings)
        {
            if (!_cards.TryGetValue(r.Source, out var card))
            {
                if (r is { Playing: true, State: { } first })
                {
                    var run = _runs[r.Source] = _runs.GetValueOrDefault(r.Source) + 1;
                    var key = $"media:{r.Source}#{run}";
                    _cards[r.Source] = new Entry { Key = key, Shown = Keyed(first, key) };
                }

                continue;
            }

            if (r.State is not { } state)
            {
                if (card.Shown.Stage != "paused")
                {
                    card.Shown = Paused(card.Shown);   // no title right now: what it last showed, as paused
                }
            }
            else if (state.Name == card.Shown.Name)
            {
                card.Shown = Keyed(state, card.Key);
                card.Pending = null;
            }
            else if (card.Pending != state.Name)
            {
                card.Pending = state.Name;
                card.PendingSince = now;
                waiting = true;
            }
            else if (now - card.PendingSince >= Confirm)
            {
                card.Shown = Keyed(state, card.Key);
                card.Pending = null;
            }
            else
            {
                waiting = true;
            }

            var paused = card.Shown.Stage == "paused";
            card.PausedSince = paused ? card.PausedSince ?? now : null;
            if (card.PausedSince is { } since && now - since >= PausedFor)
            {
                _cards.Remove(r.Source);
            }
        }

        Cards = _cards.Values.Select(c => c.Shown).ToList();
        return waiting;
    }

    private static ActivityState Keyed(ActivityState state, string key)
    {
        state.SourcePath = key;
        return state;
    }

    /// <summary>The item as paused (its player no longer reports it).</summary>
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
}
