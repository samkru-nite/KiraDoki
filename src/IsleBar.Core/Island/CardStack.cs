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
/// those stay and come back a few seconds later (user 10-07). Not while the mouse is on the pills, not for what is there when the
/// bar starts. While others wait, each shows for <see cref="DebutAtLeast"/> only.
/// How a card <i>ends</i> is its next type: inform (stays), temporary (a few seconds), or gone.</item>
/// <item><b>The line</b> (user 10-10, replacing "a temporary card goes first / the newer replaces the older"): debuts and temporary
/// cards take the big pill one at a time in the order they came — nobody pushes in. A temporary card waits in a cell of its own
/// (a glance sees another one is coming), its time counts from when it goes up, and it is kept here even after its source dropped
/// it. One app's notifications in a row are one card ("+N", <see cref="ActivityState.Earlier"/>), up until its length after the
/// last one, at most <see cref="MessageCap"/> — then back in its cell.</item>
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

    /// <summary>A debut's length when an AI card that was done / waiting starts working again in the same chat (user 10-10: 2 s was too short to notice).</summary>
    public static readonly TimeSpan ResumeDebutLength = TimeSpan.FromSeconds(6);

    /// <summary>No debuts this soon after the first update: what is there when the bar starts doesn't parade.</summary>
    public static readonly TimeSpan StartQuiet = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The longest a temporary card stays up while messages keep coming into it (user 10-10: a busy group chat kept it there): then
    /// it goes down into its cell and lets the line move on; new messages only raise its "+N" there.
    /// </summary>
    public static readonly TimeSpan MessageCap = TimeSpan.FromSeconds(20);

    // the line (user 10-10): debuts and temporary cards take the big pill in the order they came — nobody pushes in; the one up is
    // _debutKey (a debut or a temporary card), the rest wait in _debutQueue — a temporary one in a cell of its own meanwhile
    private string? _debutKey;
    private DateTimeOffset _debutSince;
    private readonly List<string> _debutQueue = [];
    private readonly Dictionary<string, Waiting> _temps = new(StringComparer.Ordinal);
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);   // key + time of each temporary item already taken in

    /// <summary>
    /// A temporary card, kept here from when it comes until it has had its time up (its source may drop it meanwhile): one per
    /// key — one app's notifications in a row merge into one (<see cref="ActivityState.Earlier"/>).
    /// </summary>
    private sealed class Waiting(string key, ActivityState state, string taken, DateTimeOffset now)
    {
        public string Key { get; } = key;

        public ActivityState State { get; set; } = state;

        /// <summary>The item it now shows (key + time), so the same item seen again only refreshes its words (a CPU card's live percent).</summary>
        public string Taken { get; set; } = taken;

        public List<ActivityState> Messages { get; } = [state];

        public DateTimeOffset Arrived { get; } = now;

        /// <summary>The last item that came into it: its time up is counted from here (user 10-10).</summary>
        public DateTimeOffset Last { get; set; } = now;

        public TimeSpan Length { get; set; } = LengthOf(state);

        /// <summary>When it went up on the big pill (null = still waiting).</summary>
        public DateTimeOffset? Up { get; set; }

        /// <summary>Its <see cref="MessageCap"/> ran out: back in its cell until <see cref="Last"/> + <see cref="Length"/>.</summary>
        public bool Down { get; set; }

        public static TimeSpan LengthOf(ActivityState s) => s.ShowFor ?? SystemWatch.NoticeLengths.Read;
    }

    /// <summary>A notification that merges with the next one from the same app (the others replace their content).</summary>
    private static bool IsMessage(ActivityState s) => s is { Kind: ActivityKind.Notice, Stage: "expand" };
    private readonly HashSet<string> _resumed = new(StringComparer.Ordinal);   // debuts of AI cards working again: ResumeDebutLength
    private readonly Dictionary<string, string> _looks = new(StringComparer.Ordinal);   // each card's ActivityState.Look last seen
    private readonly Dictionary<string, (string Look, TimeSpan Length)> _lookDebuts = new(StringComparer.Ordinal);   // debuts a look asked for
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
        // lasting notices (focus mode on) are status cards too; battery at 5% or less is pending: pinned, orange, until plugged in (user 10-10)
        ActivityKind.Notice when state.Stage == SystemWatch.SystemNotice.HoldStage => CardType.Status,
        ActivityKind.Notice when state.Stage == SystemWatch.SystemNotice.CriticalStage => CardType.Pending,
        ActivityKind.Notice when state.Stage == SystemWatch.SystemNotice.KeepStage => CardType.Inform,   // a calendar reminder (10-10)
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
            _notes.Add($"debut of {_debutKey ?? "-"} and {_debutQueue.Count(k => !_temps.ContainsKey(k))} waiting dropped ({key} clicked)");
        }

        // a clicked card replaces any debut, and a temporary card that was up (it has been seen); waiting temporary ones still come
        if (_debutKey is { } up && up != key && _temps.ContainsKey(up))
        {
            _temps.Remove(up);
        }

        _debutKey = null;
        _debutQueue.RemoveAll(k => !_temps.ContainsKey(k));
        if (_temps.TryGetValue(key, out var picked) && !picked.Down)
        {
            _debutQueue.Remove(key);   // a waiting one clicked in its cell: up now, its time running from here
            _debutKey = key;
            _debutSince = now;
            picked.Up ??= now;
        }
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
            RestartDebut(now);
        }

        _hovering = hovering;
    }

    /// <summary>
    /// The mouse left while a card was having its turn on the big pill: its whole time again from now (user 10-10: "the mouse was on
    /// it, so I didn't read it yet" — any pause counts from when the mouse goes, not from when the card came up).
    /// </summary>
    private void RestartDebut(DateTimeOffset now)
    {
        if (_debutKey is not { } key)
        {
            return;
        }

        if (_temps.TryGetValue(key, out var t))
        {
            t.Last = now;
        }
        else
        {
            _debutSince = now;
        }

        _notes.Add($"debut of {key}: the mouse left, its time starts again");
    }

    /// <summary>"Close" on a status card that can't be ended from here (music, a download, an agent working): hidden until it ends.</summary>
    public void Hide(string key, DateTimeOffset now)
    {
        if (_temps.Remove(key))
        {
            _debutQueue.Remove(key);   // a temporary card closed: gone (the items it took in stay taken)
            if (_debutKey == key)
            {
                _debutKey = null;
            }
        }
        else
        {
            _hidden.Add(key);
        }

        Rebuild(now);
    }

    private void Rebuild(DateTimeOffset now)
    {
        _firstUpdate ??= now;
        var fresh = new List<(string Key, DateTimeOffset Updated)>();   // became a status card this time
        var cards = new List<(Card Card, DateTimeOffset Since, DateTimeOffset Updated)>();
        var present = new HashSet<string>(StringComparer.Ordinal);
        var temporary = new List<(string Key, ActivityState State)>();
        foreach (var state in _items)
        {
            var key = KeyOf(state);
            var type = TypeOf(state, now);
            if (type == CardType.Temporary)
            {
                temporary.Add((key, state));   // kept and lined up here (TakeTemporaries)
                continue;
            }

            if (!present.Add(key))
            {
                continue;   // two items with one key: the first wins (shouldn't happen)
            }

            var pinned = type != CardType.Status && !_neverPin.Contains(state.Kind);

            // a card that changes type counts as newly arrived (working → done comes to the front; done → working again goes to the back)
            var resumed = false;
            var changed = !_arrived.TryGetValue(key, out var seen) || seen.Type != type;
            if (changed)
            {
                resumed = seen.Since != default && state.Kind == ActivityKind.AgentWorking;   // done / waiting → working again
                seen = (type, now);
                _arrived[key] = seen;
            }

            // a card with looks (the calendar, 10-10) debuts when its look changes to one that asks for it, not when its type does
            if (state.Look is { } look)
            {
                var newLook = !_looks.TryGetValue(key, out var was) || was != look;
                _looks[key] = look;
                changed = newLook && state.DebutFor is not null;
                if (changed && type == CardType.Status)
                {
                    _lookDebuts[key] = (look, state.DebutFor!.Value);
                }
            }

            if (changed && type == CardType.Status)
            {
                if (TaskRules.Debuts(state) && !_hidden.Contains(key) && !_tucked.Contains(key))
                {
                    fresh.Add((key, state.UpdatedAt));
                    if (resumed)
                    {
                        _resumed.Add(key);
                    }
                }
                else
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
            _looks.Remove(gone);
        }

        _hidden.RemoveWhere(k => !present.Contains(k));
        _tucked.RemoveWhere(k => !present.Contains(k) || _hidden.Contains(k));   // ended or closed: a new one is a new card

        TakeTemporaries(temporary, present, now);
        foreach (var t in _temps.Values)
        {
            cards.Add((new Card(t.Key, CardType.Temporary, !_neverPin.Contains(t.State.Kind), t.State), t.Arrived, t.Arrived));
        }

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

                // newest first — temporary cards in their line's order, oldest first (user 10-10: nobody pushes in)
                var byNew = a.Card.Type == CardType.Temporary ? a.Updated.CompareTo(b.Updated) : b.Updated.CompareTo(a.Updated);
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
        if (ordered.RemoveAll(c => c.Type == CardType.Temporary && !_temps.ContainsKey(c.Key)) > 0)
        {
            Cards = ordered;   // a temporary card's time ended just now: gone this tick, not one more in a cell
        }

        ShownIndex = PickShown(ordered, now);
        _shownKey = ordered.Count == 0 || ShownIndex < 0 ? null : ordered[ShownIndex].Key;
    }

    /// <summary>
    /// The temporary items of this tick taken in (user 10-10): a new one gets a card and joins the line — even with the mouse on the
    /// pills, it waits its turn there; one for a card already kept (same key: the same app's next notification, the charger plugged
    /// back) goes into that card, which then stays up its length from this one on. A card the items' own status card took back (a
    /// new download under the "download" key) ends; one whose time is over leaves.
    /// </summary>
    private void TakeTemporaries(List<(string Key, ActivityState State)> items, HashSet<string> statusKeys, DateTimeOffset now)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, state) in items.OrderBy(t => t.State.UpdatedAt))
        {
            var taken = key + "\u0001" + state.UpdatedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            seen.Add(taken);
            if (!_taken.Add(taken))
            {
                if (_temps.TryGetValue(key, out var same) && same.Taken == taken)
                {
                    state.Earlier = same.State.Earlier;   // the same item again: its words as they are now, nothing new
                    same.State = state;
                    same.Messages[^1] = state;
                }

                continue;
            }

            if (statusKeys.Contains(key))
            {
                continue;
            }

            if (_temps.TryGetValue(key, out var card))
            {
                var merges = IsMessage(state) && IsMessage(card.State);
                if (!merges)
                {
                    card.Messages.Clear();   // not messages: the newer one just replaces it
                }

                card.Messages.Add(state);
                state.Earlier = card.Messages.Take(card.Messages.Count - 1).ToList();
                card.State = state;
                card.Taken = taken;
                card.Last = now;
                card.Length = Waiting.LengthOf(state);
                _notes.Add($"{key}: one more came in ({card.Messages.Count} in all, {(card.Down ? "in its cell" : card.Up is null ? "waiting" : "up")})");
                continue;
            }

            _temps[key] = new Waiting(key, state, taken, now);
            _debutQueue.Add(key);
            _notes.Add($"{key} came (temporary), waits its turn ({_debutQueue.Count} in the line{(_debutKey is null ? string.Empty : $", {_debutKey} up")})");
        }

        _taken.IntersectWith(seen);   // forget the items their sources dropped
        foreach (var t in _temps.Values.ToList())
        {
            if (statusKeys.Contains(t.Key))
            {
                Drop(t, "its status card is back");
            }
            else if (t.Down && now >= t.Last + t.Length)
            {
                Drop(t, "its time in the cell is over");
            }
        }
    }

    private void Drop(Waiting t, string why)
    {
        _temps.Remove(t.Key);
        _debutQueue.Remove(t.Key);
        if (_debutKey == t.Key)
        {
            _debutKey = null;
        }

        _notes.Add($"{t.Key} gone ({why})");
    }

    /// <summary>
    /// The temporary card up has had its time: its length from the last item that came into it (user 10-10), but not past
    /// <see cref="MessageCap"/> from going up — then it goes down into its cell while items still come.
    /// </summary>
    private void EndIfOver(Waiting t, DateTimeOffset now)
    {
        var up = t.Up ?? now;
        var end = (t.Last > up ? t.Last : up) + t.Length;
        var cap = up + MessageCap;
        if (now < (end < cap ? end : cap))
        {
            return;
        }

        _debutKey = null;
        if (end > cap)
        {
            t.Down = true;
            _notes.Add($"{t.Key} up {MessageCap.TotalSeconds:0} s: down into its cell, {t.Messages.Count} in all");
        }
        else
        {
            Drop(t, "its time is over");
        }
    }

    /// <summary>Starts, queues, hands over and ends debuts and temporary cards — one line, first come first (see the class notes).</summary>
    private void UpdateDebut(List<Card> cards, List<(string Key, DateTimeOffset Updated)> fresh, DateTimeOffset now)
    {
        // a debut a look asked for lasts while the card still shows that look
        bool Present(string? key) => key is not null
            && (_temps.TryGetValue(key, out var t) ? !t.Down : cards.Any(c => c.Key == key && c.Type == CardType.Status
                && (!_lookDebuts.TryGetValue(key, out var d) || c.State.Look == d.Look)));
        if (_hovering || now - _leftAt < HoldAfterLeave)
        {
            foreach (var f in fresh)
            {
                if (cards.FirstOrDefault(c => c.Key == f.Key) is { State.ByHand: true } hand)
                {
                    // the person's own act (files dropped on the pill, 10-10): up at once, the mouse there or not
                    if (_debutKey is { } before && before != hand.Key && _temps.ContainsKey(before))
                    {
                        _debutQueue.Insert(0, before);   // a temporary card it pushed aside comes back after it
                    }

                    _debutKey = hand.Key;
                    _debutSince = now;
                    _shownKey = hand.Key;
                    _notes.Add($"{f.Key} became status by hand: debut now, mouse or not");
                    continue;
                }

                _notes.Add($"{f.Key} became status, no debut (mouse {(_hovering ? "on the pills" : "just left")})");
            }

            var debuts = _debutQueue.Count(k => !_temps.ContainsKey(k));
            if (debuts > 0)
            {
                _notes.Add($"{debuts} waiting debut(s) dropped (mouse)");
            }

            // the mouse is there: new status cards go straight to their cells; temporary ones wait. The one up stays up — its time
            // starts again when the mouse leaves (RestartDebut; user 10-10: it used to be dropped, and went 2 s after the mouse left)
            _debutQueue.RemoveAll(k => !_temps.ContainsKey(k));
            return;
        }

        if (!Present(_debutKey))
        {
            if (_debutKey is not null)
            {
                _notes.Add($"debut of {_debutKey} ended: no longer a status card");
            }

            _debutKey = null;
        }

        _debutQueue.RemoveAll(k => !Present(k));
        _resumed.RemoveWhere(k => k != _debutKey && !_debutQueue.Contains(k) && !fresh.Any(f => f.Key == k));
        foreach (var k in _lookDebuts.Keys.Where(k => k != _debutKey && !_debutQueue.Contains(k) && !fresh.Any(f => f.Key == k)).ToList())
        {
            _lookDebuts.Remove(k);
        }

        if (_debutKey is { } showing && _lookDebuts.ContainsKey(showing) && fresh.Any(f => f.Key == showing))
        {
            _debutSince = now;   // up, and a new look that debuts too came: its full time from now
            _notes.Add($"debut of {showing} starts again (a new look)");
        }
        if (now - _firstUpdate!.Value >= StartQuiet)
        {
            var add = fresh.OrderBy(f => f.Updated).ThenBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key)
                .Where(k => k != _debutKey && !_debutQueue.Contains(k)).ToList();
            _debutQueue.AddRange(add);
            foreach (var k in add)
            {
                _notes.Add($"{k} became status, queued for a debut ({_debutQueue.Count} in the line)");
            }
        }
        else
        {
            foreach (var f in fresh)
            {
                _notes.Add($"{f.Key} became status, no debut (just started)");
            }
        }

        if (_debutKey is { } up && _temps.TryGetValue(up, out var on))
        {
            EndIfOver(on, now);
        }
        else if (_debutKey is not null && now - _debutSince >= LengthOf(_debutKey))
        {
            _notes.Add($"debut of {_debutKey} over");
            _resumed.Remove(_debutKey);
            _lookDebuts.Remove(_debutKey);
            _debutKey = null;   // its turn is over: the next in the line
        }

        if (_debutKey is null && _debutQueue.Count > 0)
        {
            _debutKey = _debutQueue[0];
            _debutQueue.RemoveAt(0);
            _debutSince = now;
            if (_temps.TryGetValue(_debutKey, out var next))
            {
                next.Up ??= now;   // its time counts from here, not from when it came (user 10-10)
                _notes.Add($"{_debutKey} up ({next.Length.TotalSeconds:0.#} s, {_debutQueue.Count} still in the line)");
            }
            else
            {
                _notes.Add($"debut of {_debutKey} starts");
            }
        }
    }

    /// <summary>How long the card <paramref name="key"/>'s debut lasts: its look's own length, else the usual ones.</summary>
    private TimeSpan LengthOf(string key)
        => _lookDebuts.TryGetValue(key, out var d) ? d.Length
            : _debutQueue.Count > 0 ? DebutAtLeast
            : _resumed.Contains(key) ? ResumeDebutLength
            : DebutLength;

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
            return debut;   // the line's turn: a few seconds, even over a pinned card
        }

        // tucked cards wait on the small pill (Tuck): the rest as if they weren't there; all tucked → none on the big pill.
        // A temporary card goes up only by its turn in the line (UpdateDebut): waiting, or back down after its cap, it is in its cell
        var top = cards.FindIndex(c => !_tucked.Contains(c.Key) && c.Type != CardType.Temporary);
        if (top >= 0 && cards[top].Pinned)
        {
            return top;   // someone is waiting for you
        }

        var chosen = _chosenKey is null ? -1 : cards.FindIndex(c => c.Key == _chosenKey);
        return chosen >= 0 ? chosen : top;
    }
}
