namespace IsleBar.Core.Island;

/// <summary>
/// The card on the big pill now (from <see cref="CardStack"/>), plus how many others there are.
/// </summary>
/// <param name="Primary">State to show. Null when nothing is going on.</param>
/// <param name="ExtraCount">How many are hidden behind it. 0 means no dot is drawn.</param>
public sealed record IslandSnapshot(ActivityState? Primary, int ExtraCount)
{
    /// <summary>Nothing going on (the usual search bar).</summary>
    public static readonly IslandSnapshot Empty = new(null, 0);

    /// <summary>Whether there is anything to show.</summary>
    public bool IsActive => Primary is not null;

    /// <summary>Whether the small dot should be drawn.</summary>
    public bool HasMore => ExtraCount > 0;
}
