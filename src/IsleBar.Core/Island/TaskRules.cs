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
/// </list>
/// </summary>
public static class TaskRules
{
    /// <summary>Whether a card that becomes a status card debuts. Every kind does for now; leave one out here.</summary>
    public static bool Debuts(ActivityState state) => state is not null;
}
