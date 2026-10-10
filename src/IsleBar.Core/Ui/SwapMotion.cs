namespace IsleBar.Core.Ui;

/// <summary>A box in the window (dip).</summary>
public readonly record struct MotionBox(double X, double Y, double Width, double Height, double Radius);

/// <summary>
/// <b>Every number of the swap animation in one place</b> (TODO 7.9 E: change the timing or easing only here), and the path
/// math, so it can be checked without a window. How a task icon and the big pill swap places — straight lines (user 10-10: the
/// arc and flip choices went): the clicked icon's circle grows into the pill (on top); the old card shrinks into the emptied cell,
/// fading to <see cref="FadeTo"/>. The morph follows preview D v12: a cell is a 24 circle; it opens into the pill
/// box while the card inside slides so its icon stays at the circle's centre ("pulling the curtain open" — content laid out at
/// its final size, never stretched).
/// </summary>
public static class SwapMotion
{
    /// <summary>The same lengths as the lifted card's opening (user 10-08: one tempo for every card movement; was 0.34 / 0.46).</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(0.28);

    /// <summary>Easing: cubic-bezier(.4, 0, .2, 1) — Material's "standard" curve, the one the preview used.</summary>
    public const double X1 = 0.4, Y1 = 0, X2 = 0.2, Y2 = 1;

    /// <summary>With the bounce on (Windows' animation effects, 10-10; was a setting from 10-07) movements take a little longer and overshoot by about this share before settling.</summary>
    public static readonly TimeSpan BounceDuration = TimeSpan.FromSeconds(0.37);

    /// <summary>
    /// How much the spring bounces, Apple's way (10-10): 0 = none (SwiftUI <c>.smooth</c>), 0.15 = <c>.snappy</c>, 0.3 = <c>.bouncy</c>;
    /// damping ratio = 1 − bounce, so one overshoot of e^(−ζπ/√(1−ζ²)): 0.15 → 0.6%, 0.3 → 4.6% (was ζ 0.75 ≈ bounce 0.25, 2.8%).
    /// The pills (swap, list) bounce 0.15; the lifted card doesn't (<see cref="LiftMotion.Opening"/>) — a big reading surface
    /// that wobbles looks soft and its text shimmers (user 10-10 after trying 0.15 / 0.3).
    /// </summary>
    public const double Bounce = 0.15;

    /// <summary>Speed of the spring over a movement (t 0 → 1): settled by the end for any bounce up to 0.3.</summary>
    public const double Speed = 8.3;

    /// <summary>Room kept right of the pills while things fly, for the bounce to overshoot into (dip).</summary>
    public const double Overshoot = 16;

    /// <summary>The shrinking card's opacity at the end of the morph.</summary>
    public const double FadeTo = 0.55;

    /// <summary>The circle a cell stands for.</summary>
    public const double Circle = 24;

    /// <summary>
    /// The share of the way over which the circle's hover tint (the cell's lit look) gives way to the plain pill colour. The flying
    /// box itself stays opaque: where it was see-through, the window showed its flat taskbar colour over a gradient taskbar (10-07).
    /// </summary>
    public const double BackgroundTurn = 0.4;

    /// <summary>
    /// An agent's logo in its makers' colour on the big pill (and the lifted card), one colour + green / orange / red in a cell
    /// (user 10-08): going down into a cell, the cell's look comes in over the second half of the way, on top, while the big
    /// look under it goes a little later — so the two never both look faint. Shared by the swap and the lifted card going down.
    /// </summary>
    public static double CellLookIn(double p) => Math.Clamp((p - 0.45) / 0.4, 0, 1);

    /// <inheritdoc cref="CellLookIn"/>
    public static double BigLookOut(double p) => Math.Clamp((p - 0.65) / 0.3, 0, 1);

    /// <summary>A card with no cell to go into or come out of (it ends, or a new one arrives) drops out this far while fading;
    /// the new one comes down from as far above.</summary>
    public const double Drop = 8;

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
    /// <param name="lift">How far the way bows (negative = up; the ⋯ list's pills) — the swap and the lifted card go straight.</param>
    public static MotionBox Between(MotionBox from, MotionBox to, double p, double lift = 0)
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
    /// <summary>
    /// <see cref="Between"/> the way Apple bounces (10-10): within the way (0..1) the same; past an end the shape doesn't stretch
    /// on its own — its centre keeps going, and the size's overshoot becomes one <b>uniform scale</b> of the whole card, its content
    /// with it (the user saw "the far side's skin pulled out and back": the box overshot by up to 8 dip on the far side while the
    /// text stood still). Returns the drawn box (already scaled) and the scale for the content laid out at the end box's size.
    /// </summary>
    public static (MotionBox Box, double Scale) Settle(MotionBox from, MotionBox to, double p, double lift = 0)
    {
        if (p < 0)
        {
            return Settle(to, from, 1 - p);   // past the start (a card going back): the same, mirrored
        }

        if (p <= 1)
        {
            return (Between(from, to, p, lift), 1);
        }

        var over = p - 1;
        var cx = Lerp(from.X + (from.Width / 2), to.X + (to.Width / 2), p);
        var cy = Lerp(from.Y + (from.Height / 2), to.Y + (to.Height / 2), p);
        // how much it grew (or shrank) on the way, as a share of where it ends: the overshoot carries that on, both sides alike
        static double Grew(double a, double b) => b <= 0 ? 0 : (b - a) / b;
        var scale = Math.Clamp(1 + (over * (Grew(from.Width, to.Width) + Grew(from.Height, to.Height)) / 2), 0.5, 1.5);
        var w = to.Width * scale;
        var h = to.Height * scale;
        return (new MotionBox(cx - (w / 2), cy - (h / 2), w, h, to.Radius * scale), scale);
    }

    public static double ContentX(MotionBox circle, MotionBox pill, double iconX, double p)
        => Lerp(circle.X + (circle.Width / 2) - iconX, pill.X, p);

    public static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    /// <summary>The movement curve: <see cref="Ease"/>, or with the bounce <see cref="Spring"/> (sizes and positions pass the end a
    /// little and come back — that is the bounce; opacities clamp it).</summary>
    public static double Move(double t, bool bounce) => bounce ? Spring(t) : Ease(t);

    /// <summary>A damped spring from 0 to 1 (<see cref="Bounce"/>, <see cref="Speed"/>), exactly 1 at the end.</summary>
    public static double Spring(double t) => Spring(t, Bounce);

    /// <summary><see cref="Spring(double)"/> with a given bounce (0 … 0.9).</summary>
    public static double Spring(double t, double bounce)
    {
        var damping = 1 - Math.Clamp(bounce, 0, 0.9);
        t = Math.Clamp(t, 0, 1);
        if (t >= 1)
        {
            return 1;
        }

        if (damping >= 1)
        {
            return 1 - ((1 + (Speed * t)) * Math.Exp(-Speed * t));   // bounce 0: critically damped, no overshoot
        }

        var wd = Speed * Math.Sqrt(1 - (damping * damping));
        var decay = Math.Exp(-damping * Speed * t);
        return 1 - (decay * (Math.Cos(wd * t) + (damping * Speed / wd * Math.Sin(wd * t))));
    }

    public static TimeSpan Length(bool bounce) => bounce ? BounceDuration : Duration;

    /// <summary>Dropping out speeds up (quadratic), dropping in slows down (cubic).</summary>
    public static double EaseIn(double t) => Math.Clamp(t, 0, 1) * Math.Clamp(t, 0, 1);

    public static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    /// <summary>
    /// Put back from the hand (the transit station let go over its place, user 10-11): fast at once — the hand was moving — then
    /// slowing into its place, no bounce (it's set down, not popping up; the swap's spring overshot along the way, "flung to the
    /// bottom right"). Near is quick, far takes longer: <see cref="PutBackShortest"/> … <see cref="PutBackLongest"/> over
    /// <see cref="PutBackFar"/> dip.
    /// </summary>
    public static double PutBack(double t) => EaseOut(t);

    public static readonly TimeSpan PutBackShortest = TimeSpan.FromSeconds(0.15);
    public static readonly TimeSpan PutBackLongest = TimeSpan.FromSeconds(0.3);
    public const double PutBackFar = 300;

    /// <summary>How long putting back takes for a distance (dip).</summary>
    public static TimeSpan PutBackLength(double distance)
        => PutBackShortest + ((PutBackLongest - PutBackShortest) * Math.Clamp(distance / PutBackFar, 0, 1));
}
