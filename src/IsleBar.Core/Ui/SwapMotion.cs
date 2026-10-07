namespace IsleBar.Core.Ui;

/// <summary>How a task icon and the big pill swap places (setting, TODO 7.9 D, user 10-07).</summary>
public enum SwapStyle
{
    /// <summary>Morph, symmetric, straight: the clicked icon's circle grows into the pill (on top); the old card shrinks into the
    /// emptied cell, fading to <see cref="SwapMotion.FadeTo"/>.</summary>
    MorphLine,

    /// <summary>Morph, symmetric, arcs: the same, the growing one rising <see cref="SwapMotion.Arc"/> and the shrinking one dipping.</summary>
    MorphArc,

    /// <summary>In place: the old content drops out, the new drops in; nothing moves between the pills.</summary>
    Flip,
}

/// <summary>A box in the window (dip).</summary>
public readonly record struct MotionBox(double X, double Y, double Width, double Height, double Radius);

/// <summary>
/// <b>Every number of the swap animation in one place</b> (TODO 7.9 E: change the timing, easing or arc only here), and the path
/// math, so it can be checked without a window. The morph follows preview D v12: a cell is a 24 circle; it opens into the pill
/// box while the card inside slides so its icon stays at the circle's centre ("pulling the curtain open" — content laid out at
/// its final size, never stretched).
/// </summary>
public static class SwapMotion
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(0.34);

    /// <summary>Easing: cubic-bezier(.4, 0, .2, 1) — Material's "standard" curve, the one the preview used.</summary>
    public const double X1 = 0.4, Y1 = 0, X2 = 0.2, Y2 = 1;

    /// <summary>With the bounce on (setting, user 10-07) movements take a little longer and overshoot by about this share before settling.</summary>
    public static readonly TimeSpan BounceDuration = TimeSpan.FromSeconds(0.46);
    public const double BounceStrength = 1.2;   // easeOutBack's s: 1.2 ≈ 5% past the end

    /// <summary>Room kept right of the pills while things fly, for the bounce to overshoot into (dip).</summary>
    public const double Overshoot = 16;

    /// <summary>How far the arcs rise / dip (the taskbar has 7.5 above and below the pills).</summary>
    public const double Arc = 7;

    /// <summary>The shrinking card's opacity at the end of the straight morph.</summary>
    public const double FadeTo = 0.55;

    /// <summary>The circle a cell stands for.</summary>
    public const double Circle = 24;

    /// <summary>
    /// The share of the way over which the circle's hover tint (the cell's lit look) gives way to the plain pill colour. The flying
    /// box itself stays opaque: where it was see-through, the window showed its flat taskbar colour over a gradient taskbar (10-07).
    /// </summary>
    public const double BackgroundTurn = 0.4;

    /// <summary>Flip: the old content drops this far while fading; the new comes down from as far above.</summary>
    public const double FlipDrop = 8;
    public static readonly TimeSpan FlipOut = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan FlipInDelay = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan FlipIn = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan FlipCellIn = TimeSpan.FromMilliseconds(220);

    public static SwapStyle Parse(string? value) => value switch
    {
        "arc" => SwapStyle.MorphArc,
        "flip" => SwapStyle.Flip,
        _ => SwapStyle.MorphLine,
    };

    public static string Key(SwapStyle style) => style switch
    {
        SwapStyle.MorphArc => "arc",
        SwapStyle.Flip => "flip",
        _ => "line",
    };

    /// <summary>The eased share of the way at time share <paramref name="t"/> (0–1).</summary>
    public static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        double lo = 0, hi = 1, u = t;
        for (var i = 0; i < 30; i++)   // solve bezierX(u) = t by halving: exact enough, no special cases
        {
            u = (lo + hi) / 2;
            if (Bezier(u, X1, X2) < t)
            {
                lo = u;
            }
            else
            {
                hi = u;
            }
        }

        return Bezier(u, Y1, Y2);
    }

    private static double Bezier(double u, double p1, double p2)
        => (3 * (1 - u) * (1 - u) * u * p1) + (3 * (1 - u) * u * u * p2) + (u * u * u);

    /// <summary>
    /// The 24 circle a cell stands for, around its centre (<paramref name="cellCentreX"/>), in the pill row (<paramref name="rowTop"/>,
    /// 32 high).
    /// </summary>
    public static MotionBox CircleAt(double cellCentreX, double rowTop)
        => new(cellCentreX - (Circle / 2), rowTop + 4, Circle, Circle, Circle / 2);

    /// <summary>
    /// The box at eased share <paramref name="p"/> going from <paramref name="from"/> to <paramref name="to"/>, raised by
    /// <paramref name="lift"/> × sin(πp) (negative = up).
    /// </summary>
    public static MotionBox Between(MotionBox from, MotionBox to, double p, double lift)
        => new(
            Lerp(from.X, to.X, p),
            Lerp(from.Y, to.Y, p) + (lift * Math.Sin(Math.PI * p)),
            Lerp(from.Width, to.Width, p),
            Lerp(from.Height, to.Height, p),
            Lerp(from.Radius, to.Radius, p));

    /// <summary>
    /// Where the card's content sits (its left edge, window x) while its box is <paramref name="box"/>: its icon (at
    /// <paramref name="iconX"/> inside the card) at the circle's centre at the cell end, at the pill's left edge at the pill end.
    /// </summary>
    public static double ContentX(MotionBox circle, MotionBox pill, double iconX, double p)
        => Lerp(circle.X + (circle.Width / 2) - iconX, pill.X, p);

    public static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    /// <summary>
    /// The movement curve: <see cref="Ease"/>, or with the bounce an ease-out-back that passes the end by ~5% and comes back
    /// (opacities clamp it; sizes and positions overshoot — that is the bounce).
    /// </summary>
    public static double Move(double t, bool bounce)
    {
        if (!bounce)
        {
            return Ease(t);
        }

        var u = Math.Clamp(t, 0, 1) - 1;
        return 1 + ((BounceStrength + 1) * u * u * u) + (BounceStrength * u * u);
    }

    public static TimeSpan Length(bool bounce) => bounce ? BounceDuration : Duration;

    /// <summary>Flip's curves: leaving speeds up (quadratic), arriving slows down (cubic).</summary>
    public static double EaseIn(double t) => Math.Clamp(t, 0, 1) * Math.Clamp(t, 0, 1);

    public static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
}
