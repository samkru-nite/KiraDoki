namespace IsleBar.Core.Island;

/// <summary>
/// <b>How each kind of task starts and ends</b> (user 10-07) — the one place to look, and to change a kind.
/// <para>Start: <b>debut</b> = shows on the big pill for a moment, then goes down into its cell (<see cref="CardStack"/>).</para>
/// <para>End: <b>nothing</b> = just gone · <b>brief</b> = rises from its cell, stays a few seconds (a temporary card) ·
/// <b>stays</b> = rises, with a border, until you close it (an inform card). The end is the card's next type, so the task's source
/// writes it; this table is what they do:</para>
/// <list type="table">
/// <item><term>AI working</term><description>debut · done / failed / interrupted → stays</description></item>
/// <item><term>Timer, pomodoro</term><description>debut · time's up → stays; cancelled → nothing</description></item>
/// <item><term>Stopwatch</term><description>debut · stopped → nothing</description></item>
/// <item><term>Music (Edge too)</term><description>debut (a new song is the same card) · nothing</description></item>
/// <item><term>Browser download</term><description>debut · <i>all</i> of them finished → brief (one finishing among others: nothing)</description></item>
/// <item><term>Copy / unzip / zip</term><description>debut · finished → brief</description></item>
/// <item><term>Mic / camera</term><description>debut · nothing</description></item>
/// <item><term>Send to phone</term><description>debut · sent → brief; failed → stays</description></item>
/// <item><term>Focus mode on</term><description>debut · off → nothing (10-10)</description></item>
/// <item><term>Calendar</term><description>one card all day that changes looks, a second only while two events overlap
/// (<see cref="SystemWatch.CalendarLogic"/>): its own reminder → pinned orange; start → debut 6 s orange, then in its cell "Ongoing";
/// end → debut 6 s green, then a pinned green gap or nothing; a look debuts by <see cref="ActivityState.Look"/>, not by type (10-10)</description></item>
/// <item><term>Battery low</term><description>brief at 20% and at 10%; at 5% or less pinned orange (pending) · plugged in → nothing (10-10)</description></item>
/// </list>
/// </summary>
public static class TaskRules
{
    /// <summary>Whether a card that becomes a status card debuts. Every kind does for now; leave one out here.</summary>
    public static bool Debuts(ActivityState state) => state is not null;
}
