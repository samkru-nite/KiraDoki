namespace IsleBar.Core.Ui;

/// <summary>
/// <b>Every number of the ⋯ list in one place</b> (TODO 7.9 F3, user 10-09 after the preview): all the cards drawn as big pills,
/// stacked over the big pill; they fly out of the ⋯ circle, each growing into its pill on the way, the nearest first. Pure math, so
/// it can be checked without a window. Boxes are in dip, relative to whatever origin the caller gives the big pill.
/// </summary>
public static class ListMotion
{
    /// <summary>Space between two pills of the list, and between the lowest one and the taskbar's top (dip) — as the lifted card.</summary>
    public const double Gap = 8;

    /// <summary>The way out of ⋯ bows up by this much in the middle (dip; the swap's arc, user 10-09 picked 20).</summary>
    public const double Arc = 20;

    /// <summary>A pill shows itself over this first share of its way (a 24 circle coming out of ⋯).</summary>
    public const double Appear = 0.15;

    /// <summary>Its content (the big pill's look) fades in from this share of the way (user 10-09: 50%).</summary>
    public const double ContentIn = 0.5;

    /// <summary>The big pill's content and the task icons dim to this while the list is up.</summary>
    public const double Dim = 0.4;

    /// <summary>Tapping a pill: the others fade out over this first share of the lift.</summary>
    public const double OthersOut = 0.3;

    /// <summary>Tapping a pill: it stays this long under the lifted card's first frames, so there's never a frame with neither.</summary>
    public static readonly TimeSpan Handover = TimeSpan.FromMilliseconds(60);

    /// <summary>Going back into ⋯: the plain way out backwards, as quick as the lifted card's close.</summary>
    public static readonly TimeSpan Close = LiftMotion.Close;

    /// <summary>One pill's way out (the swap's and the lift's length, user 10-09: "the bounce as the card's").</summary>
    public static TimeSpan Length(bool bounce) => SwapMotion.Length(bounce);

    /// <summary>"20" / "40" (setting) → ms between two pills setting off.</summary>
    public static int Stagger(string? setting) => setting == "40" ? 40 : 20;

    /// <summary>The whole way out: the last pill sets off <paramref name="count"/> − 1 staggers late.</summary>
    public static TimeSpan Total(int count, bool bounce, int staggerMs)
        => Length(bounce) + TimeSpan.FromMilliseconds(staggerMs * Math.Max(0, count - 1));

    /// <summary>The <paramref name="index"/>-th pill's place (0 = lowest, the big pill's card), over the big pill at <paramref name="pill"/>.</summary>
    public static MotionBox Slot(int index, MotionBox pill, double taskbarTop)
        => pill with { Y = taskbarTop - Gap - pill.Height - (index * (pill.Height + Gap)) };

    /// <summary>How many pills fit between the taskbar and <paramref name="screenTop"/> (at least 1); the rest are left out.</summary>
    public static int Fits(int count, double pillHeight, double taskbarTop, double screenTop)
    {
        var room = taskbarTop - screenTop - LiftMotion.ScreenMargin + Gap;
        return Math.Clamp((int)Math.Floor(room / (pillHeight + Gap)), 1, Math.Max(1, count));
    }

    /// <summary>
    /// The <paramref name="index"/>-th pill's share of its own way at <paramref name="elapsed"/> into the way out (0 until it sets off,
    /// past 1 a little while bouncing). The nearest pill (index 0) goes first.
    /// </summary>
    public static double Out(int index, TimeSpan elapsed, bool bounce, int staggerMs)
    {
        var t = (elapsed.TotalMilliseconds - (index * staggerMs)) / Length(bounce).TotalMilliseconds;
        return SwapMotion.Move(Math.Clamp(t, 0, 1), bounce);
    }

    /// <summary>The share of the way back at time share <paramref name="t"/> of <see cref="Close"/> (1 → 0, all together).</summary>
    public static double Back(double t) => SwapMotion.Ease(1 - t);

    /// <summary>A pill at share <paramref name="p"/> of its way from the ⋯ circle to its slot: the swap's box between the two, bowing up.</summary>
    public static MotionBox Box(MotionBox dots, MotionBox slot, double p) => SwapMotion.Between(dots, slot, p, -Arc);

    /// <summary>How much of the pill shows at share <paramref name="p"/>, and how much of its content.</summary>
    public static (double Pill, double Content) Shows(double p)
        => (Math.Clamp(p / Appear, 0, 1), LiftMotion.Window(p, ContentIn, 1));
}
