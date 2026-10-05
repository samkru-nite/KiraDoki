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
/// <item>Scrolling chooses the card to show, wrapping round at both ends, and the choice stays — it never deletes anything.</item>
/// <item>While the mouse is on the pill (= still scrolling) and for <see cref="HoldAfterLeave"/> after it leaves, the shown card
/// doesn't change, even when a pinned card arrives. After that: a pinned card there → the top one shows; none → the chosen card
/// (the top card if nothing was chosen or it ended).</item>
/// <item>"Close" on a status card hides it until it ends (its key drops out of the list).</item>
/// </list>
/// Deterministic: the same items and the same actions always give the same result (user: "the computer must not be a black box").
/// </summary>
public sealed class CardStack
{
    /// <summary>How long the shown card is held after the mouse leaves the pill before a pinned card takes over (user 10-05: 3 s).</summary>
    public static readonly TimeSpan HoldAfterLeave = TimeSpan.FromSeconds(3);

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

    /// <summary>Position of the card on the pill in <see cref="Cards"/> (0 = top). 0 when there are no cards.</summary>
    public int ShownIndex { get; private set; }

    /// <summary>The card on the big pill, or null when there are none (only the ball shows).</summary>
    public Card? Shown => Cards.Count == 0 ? null : Cards[ShownIndex];

    /// <summary>The status card last scrolled to, if it is still there.</summary>
    public string? ChosenKey => _chosenKey;

    /// <summary>The type of an item. A timer is status until it reaches zero, then an inform "time's up".</summary>
    public static CardType TypeOf(ActivityState state, DateTimeOffset now) => state.Kind switch
    {
        ActivityKind.AgentPermission => CardType.Pending,
        ActivityKind.AgentDone or ActivityKind.Error => CardType.Inform,
        // an agent's own failure (usage limit, stopped on an error, couldn't be started) is inform; the system's notices are temporary
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
    /// Which items are "the same card" from tick to tick. All music is one card (a new song or player doesn't reorder the stack);
    /// the merged browser download is one card even when the file it names changes; everything else is its state file / source.
    /// </summary>
    public static string KeyOf(ActivityState state)
    {
        if (state.Kind == ActivityKind.Music)
        {
            return "music";
        }

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

    /// <summary>Wheel / touchpad: <paramref name="steps"/> &gt; 0 = further down the stack, &lt; 0 = back up. Wraps round at both ends.</summary>
    public void Scroll(int steps, DateTimeOffset now)
    {
        if (Cards.Count == 0 || steps == 0)
        {
            return;
        }

        var index = (((ShownIndex + steps) % Cards.Count) + Cards.Count) % Cards.Count;
        var card = Cards[index];
        if (!card.Pinned)
        {
            _chosenKey = card.Key;   // a pinned card shows by itself anyway — the choice to come back to is the status card
        }

        _shownKey = card.Key;
        ShownIndex = index;
        if (!_hovering)
        {
            _leftAt = now;   // scrolled without a hover notice (shouldn't happen) — still hold it for a moment
        }
    }

    /// <summary>The mouse entered / left the pill. Leaving starts the <see cref="HoldAfterLeave"/> countdown.</summary>
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

        ShownIndex = PickShown(ordered, now);
        _shownKey = ordered.Count == 0 ? null : ordered[ShownIndex].Key;
    }

    private int PickShown(List<Card> cards, DateTimeOffset now)
    {
        if (cards.Count == 0)
        {
            return 0;
        }

        // still scrolling (mouse on the pill) or just left: keep what is shown, so nothing changes under the hand
        var holding = _hovering || now - _leftAt < HoldAfterLeave;
        var shown = _shownKey is null ? -1 : cards.FindIndex(c => c.Key == _shownKey);
        if (holding && shown >= 0)
        {
            return shown;
        }

        if (cards[0].Pinned)
        {
            return 0;   // someone is waiting for you
        }

        var chosen = _chosenKey is null ? -1 : cards.FindIndex(c => c.Key == _chosenKey);
        return chosen >= 0 ? chosen : 0;
    }
}

/// <summary>
/// Turns wheel / touchpad deltas into whole card steps. A wheel notch is 120; a touchpad sends many small deltas per swipe, so
/// they are added up and one card is flipped per 120 (otherwise one swipe flips through every card — measured 10-05, TODO 7.9).
/// Reversing direction drops what was collected the other way.
/// </summary>
public sealed class WheelSteps
{
    public const int Notch = 120;

    private int _sum;

    /// <summary>Adds one delta (WinUI: positive = wheel away from you / swipe up) and returns the steps down the stack (may be 0).</summary>
    public int Add(int delta)
    {
        if (delta == 0)
        {
            return 0;
        }

        if (_sum != 0 && Math.Sign(delta) != Math.Sign(_sum))
        {
            _sum = 0;
        }

        _sum += delta;
        var notches = _sum / Notch;   // truncates toward zero
        _sum -= notches * Notch;
        return -notches;   // wheel toward you (negative) = further down the stack
    }
}
