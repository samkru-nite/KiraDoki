namespace IsleBar.Core.Ui;

/// <summary>
/// Where on the taskbar files can be dropped for the transit station (TODO ④ 乙, user 10-10): the whole taskbar height, so a file
/// dragged in from above never crosses a "no drop" taskbar on its way to the pill (the 🚫 and the flicker). Only while something is
/// being dragged — the rest of the time that stretch of taskbar is Windows'. This part: how wide it is, in screen pixels.
/// <list type="bullet">
/// <item>The big pill out: the circle's left to the big pill's right.</item>
/// <item>Only the circle (and maybe some cells): on the left, from the circle rightwards as wide as circle + a one-icon small pill +
/// the big pill (where the "drop here" pill will come out); in the middle, the same width centred on the circle; set by hand,
/// three circles wide centred on it (a fallback, kept simple — user). Never narrower than the pills themselves.</item>
/// </list>
/// </summary>
public static class DropZone
{
    /// <summary>Circle + a one-icon small pill + the gap + the big pill (dip).</summary>
    public static double Wide => PillGeometry.SmallWidth(1) + PillGeometry.Gap + PillGeometry.BigWidth;

    /// <param name="pillsLeft">The small pill's left end on screen (the circle is its left end).</param>
    /// <param name="pillsRight">The right end of what is out: the big pill's, or the small pill's when there's no big pill.</param>
    public static (int Left, int Right) Span(BarPosition position, int pillsLeft, int pillsRight, bool big, double scale)
    {
        if (big)
        {
            return (pillsLeft, pillsRight);
        }

        var ball = PillGeometry.Px(PillGeometry.BallBox, scale);
        var centre = pillsLeft + (ball / 2);
        var wide = PillGeometry.Px(Wide, scale);
        var (left, right) = position switch
        {
            BarPosition.Center => (centre - (wide / 2), centre - (wide / 2) + wide),
            BarPosition.Manual => (centre - (3 * ball / 2), centre - (3 * ball / 2) + (3 * ball)),
            _ => (pillsLeft, pillsLeft + wide),
        };
        return (Math.Min(left, pillsLeft), Math.Max(right, pillsRight));
    }
}
