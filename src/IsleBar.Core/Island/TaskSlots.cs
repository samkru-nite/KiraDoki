namespace IsleBar.Core.Island;

/// <summary>
/// The task icons on the small pill (TODO 7.9 D, 10-07): one cell per card the big pill isn't showing — temporary cards too while
/// they wait their turn in the line (user 10-10: so a glance sees another one is coming). The order is kept from tick to tick so
/// icons don't jump under the hand:
/// <list type="bullet">
/// <item>a new card takes its place by stack order (before the first cell that is further back in the stack);</item>
/// <item>while the mouse is on the pills a new card goes to the end instead (nothing moves under the pointer);</item>
/// <item>clicking an icon swaps it with the big pill: the card coming down takes the clicked cell, the others stay put
/// (<see cref="Swap"/>); a card coming down because the rules put another one up (a pinned card arrived) is placed by stack order.</item>
/// </list>
/// No limit on the count: the window fits what it can before the taskbar's app icons, the rest are only in the circle's list (TODO ①½).
/// </summary>
public sealed class TaskSlots
{
    private readonly List<string> _order = [];

    /// <summary>The cells, left to right.</summary>
    public IReadOnlyList<Card> Cells { get; private set; } = [];

    /// <summary>
    /// One tick: <paramref name="cards"/> is the stack (top first), <paramref name="shownKey"/> the card on the big pill.
    /// <paramref name="hovering"/> = the mouse is on the pills (new cells go to the end).
    /// </summary>
    public void Update(IReadOnlyList<Card> cards, string? shownKey, bool hovering)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var wanted = cards.Where(c => c.Key != shownKey).ToList();
        var stackIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < cards.Count; i++)
        {
            stackIndex.TryAdd(cards[i].Key, i);
        }

        _order.RemoveAll(k => !wanted.Any(c => c.Key == k));
        foreach (var card in wanted)
        {
            if (_order.Contains(card.Key))
            {
                continue;
            }

            var at = hovering ? _order.Count : _order.FindIndex(k => stackIndex[k] > stackIndex[card.Key]);
            _order.Insert(at < 0 ? _order.Count : at, card.Key);
        }

        var byKey = wanted.ToDictionary(c => c.Key, StringComparer.Ordinal);
        Cells = _order.Select(k => byKey[k]).ToList();
    }

    /// <summary>
    /// The icon <paramref name="clickedKey"/> was clicked while <paramref name="shownKey"/> was on the big pill: the shown card takes the
    /// clicked cell (a temporary card just leaves it — it has no cell). Call before the next <see cref="Update"/>.
    /// </summary>
    public void Swap(string clickedKey, string? shownKey, bool shownIsTemporary)
    {
        var at = _order.IndexOf(clickedKey);
        if (at < 0)
        {
            return;
        }

        if (shownKey is null || shownIsTemporary || _order.Contains(shownKey))
        {
            _order.RemoveAt(at);
            return;
        }

        _order[at] = shownKey;
    }
}
