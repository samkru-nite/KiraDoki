namespace IsleBar.Core.Island;

/// <summary>
/// The four card types of the new design (TODO 7.2). <b>Value order is the order among pinned cards</b> — a smaller value sits
/// higher. Pinned = "someone is waiting for you"; things you started yourself never push in front. When a status card ends it
/// becomes a reminder: an inform card if it matters (must be closed by hand), else a temporary one (gone in a few seconds).
/// </summary>
public enum CardType
{
    /// <summary>Pinned, gone by itself in a few seconds — fine to miss: copied, Bluetooth, network, battery, a download / copy finished.</summary>
    Temporary = 0,

    /// <summary>Pinned until handled in the terminal / card: an agent asking for permission. Can't be closed by hand (closing it wouldn't let the agent go on).</summary>
    Pending = 1,

    /// <summary>Pinned until closed by hand (or looked at): an agent finished or failed, a timer reached zero.</summary>
    Inform = 2,

    /// <summary>Not pinned, waits behind the others: music, a running timer, a download in progress, an agent working.</summary>
    Status = 3,
}

/// <summary>One card in the stack.</summary>
/// <param name="Key">Stays the same while it is "the same thing" (a song change is still the music card).</param>
/// <param name="Pinned">Sorted in front of the status cards (its type is pinned and the settings don't say "never pin").</param>
public sealed record Card(string Key, CardType Type, bool Pinned, ActivityState State);

/// <summary>
/// The new design's card rules (TODO 7.2 / 7.3, user 10-05): every item is a card in one ordered stack; the big pill shows one.
/// <list type="bullet">
/// <item>Order: pinned cards first — temporary, then pending, then inform; newest first within a type. Then the status cards in the
/// order they arrived (oldest first: a new one waits at the back). A card that changes type counts as newly arrived.</item>
/// <item>Clicking a task icon chooses the card to show, and the choice stays — it never deletes anything. (No scrolling: 10-07.)</item>
/// <item>While the mouse is on either pill and for <see cref="HoldAfterLeave"/> after it leaves, the shown card
/// doesn't change, even when a pinned card arrives. After that: a pinned card there → the top one shows; none → the chosen card
/// (the top card if nothing was chosen or it ended).</item>
/// <item>"Close" on a status card hides it until it ends (its key drops out of the list).</item>
/// <item><b>Debut</b> (user 10-07, replacing 10-06 "status cards never go on top first; send a separate temporary card"): a card
/// that becomes a status card — new, or another type before (permission answered, a new turn after "done") — shows on the big pill
/// for <see cref="DebutLength"/>, then goes down into its cell. Not pinning: no border. It shows over a pending / inform card too —
/// those stay and come back a few seconds later (user 10-07) — but waits while a temporary card shows (that one is gone in seconds
/// and would be missed). Not while the mouse is on the pills, not for what is there when the bar starts. Every new card gets its turn, in the order they came; while others
/// wait, each shows for <see cref="DebutAtLeast"/> only.
/// How a card <i>ends</i> is its next type: inform (stays), temporary (a few seconds), or gone.</item>
/// </list>
/// Deterministic: the same items and the same actions always give the same result (user: "the computer must not be a black box").
/// </summary>
public sealed class CardStack
{
    /// <summary>How long the shown card is held after the mouse leaves the pill before a pinned card takes over (user 10-05: 3 s; 10-07: 2 s).</summary>
    public static readonly TimeSpan HoldAfterLeave = TimeSpan.FromSeconds(2);   // 3 → 2 (user 10-07)

    /// <summary>How long a new status card shows on the big pill before going down into its cell.</summary>
    public static readonly TimeSpan DebutLength = TimeSpan.FromSeconds(2);

    /// <summary>A debut's length while other new cards wait their turn (user 10-07).</summary>
    public static readonly TimeSpan DebutAtLeast = TimeSpan.FromSeconds(1.5);

    /// <summary>No debuts this soon after the first update: what is there when the bar starts doesn't parade.</summary>
    public static readonly TimeSpan StartQuiet = TimeSpan.FromSeconds(2);

    private string? _debutKey;
    private DateTimeOffset _debutSince;
    private readonly List<string> _debutQueue = [];
    private DateTimeOffset? _firstUpdate;

    /// <summary>The card on its debut now (null = none).</summary>
    public string? DebutKey => _debutKey;

    private readonly List<string> _notes = [];

    /// <summary>What happened to debuts since the last call — queued, started, skipped and why (for the log; 甲②③ 10-09).</summary>
    public IReadOnlyList<string> TakeDebutNotes()
    {
        var notes = _notes.ToList();
        _notes.Clear();
        return notes;
    }

    private readonly Dictionary<string, (CardType Type, DateTimeOffset Since)> _arrived = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private IReadOnlyList<ActivityState> _items = [];
    private IReadOnlySet<ActivityKind> _neverPin = new HashSet<ActivityKind>();
    private string? _chosenKey;
    private string? _shownKey;
    private bool _hovering;
    private DateTimeOffset _leftAt = DateTimeOffset.MinValue;

    /// <summary>All cards, top first.</summary>
    public IReadOnlyList<Card> Cards { get; private set; } = [];

    /// <summary>Position of the card on the pill in <see cref="Cards"/> (0 = top). 0 when there are no cards, −1 when all are tucked.</summary>
    public int ShownIndex { get; private set; }

    /// <summary>The card on the big pill, or null when there are none or all are tucked (only the small pill shows).</summary>
    public Card? Shown => Cards.Count == 0 || ShownIndex < 0 ? null : Cards[ShownIndex];

    private readonly HashSet<string> _tucked = new(StringComparer.Ordinal);

    /// <summary>
    /// "Keep in the small pill" (user 10-10, 甲⑤): the card stays on the small pill — no debut, no taking the big pill when it turns
    /// done / needs you; clicked, it shows while the mouse is there and goes back down after. Until it ends or is closed.
    /// </summary>
    public void Tuck(string key, DateTimeOffset now)
    {
        _tucked.Add(key);
        if (_chosenKey == key)
        {
            _chosenKey = null;
        }

        _debutQueue.Remove(key);
        if (_debutKey == key)
        {
            _debutKey = null;
        }

        _shownKey = null;   // it goes down now, even with the mouse there (the menu was opened on it)
        Rebuild(now);
    }

    /// <summary>"Unpin": the card follows the usual rules again (a done / needs-you card comes up by itself).</summary>
    public void Untuck(string key, DateTimeOffset now)
    {
        if (_tucked.Remove(key))
        {
            Rebuild(now);
        }
    }

    /// <summary>The card is kept in the small pill (<see cref="Tuck"/>).</summary>
    public bool IsTucked(string key) => _tucked.Contains(key);

    /// <summary>The status card last chosen, if it is still there.</summary>
    public string? ChosenKey => _chosenKey;

    /// <summary>The type of an item. A timer is status until it reaches zero, then an inform "time's up".</summary>
    public static CardType TypeOf(ActivityState state, DateTimeOffset now) => state.Kind switch
    {
        ActivityKind.AgentPermission => CardType.Pending,
        ActivityKind.AgentDone or ActivityKind.Error => CardType.Inform,
        // an agent's own failure (usage limit, stopped on an error, couldn't be started) is inform; the system's notices are temporary
        // the mic / camera in use is a status card while it lasts (TODO 7.9 D, 10-06: not pinned — it is your own doing)
        ActivityKind.Notice when state.Stage == "privacy" => CardType.Status,
        ActivityKind.Notice => state.RunState == ActivityRunState.Error && state.Agent is not null ? CardType.Inform : CardType.Temporary,
        ActivityKind.Transfer => state.RunState switch
        {
            ActivityRunState.Running => CardType.Status,
            ActivityRunState.Error => CardType.Inform,
            _ => CardType.Temporary,
        },
        ActivityKind.Timer => !state.IsPaused && !TimerParser.IsStopwatch(state) && TimerParser.Remaining(state, now) <= TimeSpan.Zero
            ? CardType.Inform
            : CardType.Status,
        _ => CardType.Status,   // music, agent working
    };

    /// <summary>
    /// Which items are "the same card" from tick to tick. The merged browser download is one card even when the file it names
    /// changes; everything else is its state file / source — for music one card per player and playing run (user 10-07: a new song
    /// keeps the card, playing again after it was dropped is a new one; see <see cref="MediaCards"/>).
    /// </summary>
    public static string KeyOf(ActivityState state)
    {
        if (state.SourcePath is { } source)
        {
            return source.StartsWith("download:", StringComparison.Ordinal) ? "download" : source;
        }

        return $"{state.Kind}|{state.Title}|{state.Name}";
    }

    /// <summary>
    /// One tick: the items now (any order) → the stack. <paramref name="neverPin"/> = kinds the settings say never to pin
    /// (they wait with the status cards instead).
    /// </summary>
    public void Update(IReadOnlyList<ActivityState> items, DateTimeOffset now, IReadOnlySet<ActivityKind>? neverPin = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
        _neverPin = neverPin ?? new HashSet<ActivityKind>();
        Rebuild(now);
    }

    /// <summary>
    /// A task icon on the small pill was clicked: that card goes on the big pill (TODO 7.3, 10-07). A status card stays chosen —
    /// after the hold it comes back whenever no pinned card is waiting.
    /// </summary>
    public void Choose(string key, DateTimeOffset now)
    {
        var index = Cards.ToList().FindIndex(c => c.Key == key);
        if (index < 0)
        {
            return;
        }

        var card = Cards[index];
        if (_debutKey is not null || _debutQueue.Count > 0)
        {
            _notes.Add($"debut of {_debutKey ?? "-"} and {_debutQueue.Count} waiting dropped ({key} clicked)");
        }

        _debutKey = null;   // a clicked card replaces any debut
        _debutQueue.Clear();
        if (!card.Pinned && !_tucked.Contains(card.Key))   // a tucked card shows while looked at, then goes back down
        {
            _chosenKey = card.Key;   // a pinned card shows by itself anyway — the choice to come back to is the status card
        }

        _shownKey = card.Key;
        ShownIndex = index;
        if (!_hovering)
        {
            _leftAt = now;   // chosen without a hover notice (shouldn't happen) — still hold it for a moment
        }
    }

    /// <summary>The mouse entered / left the pills (big or small). Leaving starts the <see cref="HoldAfterLeave"/> countdown.</summary>
    public void SetHover(bool hovering, DateTimeOffset now)
    {
        if (_hovering && !hovering)
        {
            _leftAt = now;
        }

        _hovering = hovering;
    }

    /// <summary>"Close" on a status card that can't be ended from here (music, a download, an agent working): hidden until it ends.</summary>
    public void Hide(string key, DateTimeOffset now)
    {
        _hidden.Add(key);
        Rebuild(now);
    }

    private void Rebuild(DateTimeOffset now)
    {
        _firstUpdate ??= now;
        var fresh = new List<(string Key, DateTimeOffset Updated)>();   // became a status card this time
        var cards = new List<(Card Card, DateTimeOffset Since, DateTimeOffset Updated)>();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in _items)
        {
            var key = KeyOf(state);
            if (!present.Add(key))
            {
                continue;   // two items with one key: the first wins (shouldn't happen)
            }

            var type = TypeOf(state, now);
            var pinned = type != CardType.Status && !_neverPin.Contains(state.Kind);

            // a card that changes type counts as newly arrived (working → done comes to the front; done → working again goes to the back)
            if (!_arrived.TryGetValue(key, out var seen) || seen.Type != type)
            {
                seen = (type, now);
                _arrived[key] = seen;
                if (type == CardType.Status && TaskRules.Debuts(state) && !_hidden.Contains(key) && !_tucked.Contains(key))
                {
                    fresh.Add((key, state.UpdatedAt));
                }
                else if (type == CardType.Status)
                {
                    _notes.Add($"{key} became status, no debut ({(_hidden.Contains(key) ? "closed" : _tucked.Contains(key) ? "tucked" : "rules")})");
                }
            }

            if (!_hidden.Contains(key))
            {
                cards.Add((new Card(key, type, pinned, state), seen.Since, state.UpdatedAt));
            }
        }

        // forget what ended: a hidden card that comes back shows again, a chosen one is no longer chosen
        foreach (var gone in _arrived.Keys.Where(k => !present.Contains(k)).ToList())
        {
            _arrived.Remove(gone);
        }

        _hidden.RemoveWhere(k => !present.Contains(k));
        _tucked.RemoveWhere(k => !present.Contains(k) || _hidden.Contains(k));   // ended or closed: a new one is a new card
        cards.Sort((a, b) =>
        {
            if (a.Card.Pinned != b.Card.Pinned)
            {
                return a.Card.Pinned ? -1 : 1;
            }

            if (a.Card.Pinned)
            {
                var byType = a.Card.Type.CompareTo(b.Card.Type);
                if (byType != 0)
                {
                    return byType;
                }

                var byNew = b.Updated.CompareTo(a.Updated);   // newest first
                return byNew != 0 ? byNew : string.CompareOrdinal(a.Card.Key, b.Card.Key);
            }

            var byArrival = a.Since.CompareTo(b.Since);   // oldest first: a new status card waits at the back
            return byArrival != 0 ? byArrival : string.CompareOrdinal(a.Card.Key, b.Card.Key);
        });

        var ordered = cards.Select(c => c.Card).ToList();
        Cards = ordered;
        if (_chosenKey is not null && ordered.FindIndex(c => c.Key == _chosenKey) < 0)
        {
            _chosenKey = null;
        }

        UpdateDebut(ordered, fresh, now);
        ShownIndex = PickShown(ordered, now);
        _shownKey = ordered.Count == 0 || ShownIndex < 0 ? null : ordered[ShownIndex].Key;
    }

    /// <summary>Starts, queues, hands over and ends debuts (see the class notes).</summary>
    private void UpdateDebut(List<Card> cards, List<(string Key, DateTimeOffset Updated)> fresh, DateTimeOffset now)
    {
        bool Present(string? key) => key is not null && cards.Any(c => c.Key == key && c.Type == CardType.Status);
        if (_hovering || now - _leftAt < HoldAfterLeave)
        {
            foreach (var f in fresh)
            {
                _notes.Add($"{f.Key} became status, no debut (mouse {(_hovering ? "on the pills" : "just left")})");
            }

            if (_debutKey is not null || _debutQueue.Count > 0)
            {
                _notes.Add($"debut of {_debutKey ?? "-"} and {_debutQueue.Count} waiting dropped (mouse)");
            }

            _debutKey = null;   // the mouse is there: new cards go straight to their cells
            _debutQueue.Clear();
            return;
        }

        var temporary = cards.Count > 0 && cards[0] is { Pinned: true, Type: CardType.Temporary };

        if (!Present(_debutKey))
        {
            if (_debutKey is not null)
            {
                _notes.Add($"debut of {_debutKey} ended: no longer a status card");
            }

            _debutKey = null;
        }

        _debutQueue.RemoveAll(k => !Present(k));
        if (now - _firstUpdate!.Value >= StartQuiet)
        {
            var add = fresh.OrderBy(f => f.Updated).ThenBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key)
                .Where(k => k != _debutKey && !_debutQueue.Contains(k)).ToList();
            _debutQueue.AddRange(add);
            foreach (var k in add)
            {
                _notes.Add($"{k} became status, queued for a debut ({_debutQueue.Count} waiting{(temporary ? ", a temporary card shows" : string.Empty)})");
            }
        }
        else
        {
            foreach (var f in fresh)
            {
                _notes.Add($"{f.Key} became status, no debut (just started)");
            }
        }

        if (_debutKey is not null && (temporary || now - _debutSince >= (_debutQueue.Count > 0 ? DebutAtLeast : DebutLength)))
        {
            _notes.Add($"debut of {_debutKey} over{(temporary ? " (a temporary card came)" : string.Empty)}");
            _debutKey = null;   // its turn is over, or a temporary card came: that one goes first
        }

        if (_debutKey is null && _debutQueue.Count > 0 && !temporary)
        {
            _debutKey = _debutQueue[0];
            _debutQueue.RemoveAt(0);
            _debutSince = now;
            _notes.Add($"debut of {_debutKey} starts");
        }
    }

    private int PickShown(List<Card> cards, DateTimeOffset now)
    {
        if (cards.Count == 0)
        {
            return 0;
        }

        // mouse on a pill or just left: keep what is shown, so nothing changes under the hand
        var holding = _hovering || now - _leftAt < HoldAfterLeave;
        var shown = _shownKey is null ? -1 : cards.FindIndex(c => c.Key == _shownKey);
        if (holding && shown >= 0)
        {
            return shown;
        }

        var debut = _debutKey is null ? -1 : cards.FindIndex(c => c.Key == _debutKey);
        if (debut >= 0)
        {
            return debut;   // a few seconds, even over a pinned card (a temporary one stops it: UpdateDebut)
        }

        // tucked cards wait on the small pill (Tuck): the rest as if they weren't there; all tucked → none on the big pill
        var top = cards.FindIndex(c => !_tucked.Contains(c.Key));
        if (top >= 0 && cards[top].Pinned)
        {
            return top;   // someone is waiting for you
        }

        var chosen = _chosenKey is null ? -1 : cards.FindIndex(c => c.Key == _chosenKey);
        return chosen >= 0 ? chosen : top;
    }
}
