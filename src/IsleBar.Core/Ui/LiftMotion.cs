using IsleBar.Core.Island;

namespace IsleBar.Core.Ui;

/// <summary>How a card lifts out of the big pill (setting, TODO 7.9 F, user 10-08: both are built, the user picks).</summary>
public enum LiftStyle
{
    /// <summary>The frame grows out of the big pill itself into the card, over the taskbar.</summary>
    Morph,

    /// <summary>The card slides up out of the taskbar's top edge.</summary>
    SlideUp,
}

/// <summary>A rectangle in screen pixels. Right/bottom are one past the edge.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>Where the lifted card goes (screen pixels) and the popup window that holds it and the big pill under it.</summary>
public sealed record LiftLayout(ScreenRect Card, ScreenRect Window);

/// <summary>
/// <b>Every number of the lifted card's frame in one place</b> (TODO 7.9 F, 10-08): its size and place, and the timing of both
/// animations. Pure math, so it can be checked without a window.
/// </summary>
public static class LiftMotion
{
    /// <summary>Fixed width (dip): the big pill 220 + 2 × 40, so with the small pill on the left and no tasks it lines up with the circle.</summary>
    public const double Width = 300;

    /// <summary>
    /// The card's heights (dip): standard 200 (3:2), 300 (1:1, user 10-10) and long 400 (3:4). Fixed cards (<see cref="CardSheet.Adaptive"/>
    /// false) stay 200; adaptive ones take the lowest whose middle holds what they say (<see cref="Fit"/>).
    /// </summary>
    public const double Height = 200;
    public const double MidHeight = 300;
    public const double TallHeight = 400;

    private static readonly double[] Heights = [Height, MidHeight, TallHeight];

    /// <summary>The lowest height whose middle zone holds <paramref name="content"/> (dip) — the tallest when none does (it scrolls).</summary>
    public static double Fit(CardSheet sheet, double content)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (!sheet.Adaptive)
        {
            return Height;
        }

        foreach (var h in Heights)
        {
            if (content <= CardSheets.MiddleHeight(sheet, h) + 0.5)
            {
                return h;
            }
        }

        return TallHeight;
    }

    /// <summary>The card's corners (dip) — Windows 11's popups.</summary>
    public const double Radius = 8;

    /// <summary>Space between the card's bottom and the taskbar's top, and the least space to the screen's sides (dip).</summary>
    public const double TaskbarGap = 8;
    public const double ScreenMargin = 12;

    /// <summary>
    /// Timing (user 10-08, quickened once: "a bit slow for a desktop"). Opening: the swap's standard curve, or with the bounce a
    /// soft spring (passes its size by ~3% and settles; the swap's 5% ease-out-back looked "too deliberate" here). Closing: the
    /// plain opening played backwards, a little quicker, never springing - the big pill then gives one press as the card lands
    /// (a frame that sank past the pill and sprang back looked "deliberate and messy", user 10-08).
    /// </summary>
    public static readonly TimeSpan Open = SwapMotion.Duration;          // 0.28, one tempo with the swap (user 10-08)
    public static readonly TimeSpan OpenBounce = SwapMotion.BounceDuration;   // 0.37
    public static readonly TimeSpan Close = TimeSpan.FromSeconds(0.22);

    /// <summary>
    /// The "paper" (user 10-08): the pill is a sheet leaning towards you, face tilted down; lifting stands it up. The sheet starts
    /// this many degrees from upright and turns up around its bottom edge, a hair past upright with the bounce. Kept light — "a
    /// little page-turn feel, not much".
    /// </summary>
    public const double Tilt = 30;

    /// <summary>How far the eye is from the screen for the tilt's perspective (dip; XAML's default).</summary>
    public const double Depth = 1000;

    /// <summary>Room around the card inside the window for the bounce to overshoot into (dip).</summary>
    public const double Room = 24;

    /// <summary>Morph: the pill's text fades out over this first share of the way; the card's content fades in from <see cref="ContentIn"/>.</summary>
    public const double PillTextOut = 0.3;
    public const double ContentIn = 0.5;

    /// <summary>Closing into a task cell (a swap while the card is up): over this last share of the way the card fades into the
    /// card flying there underneath, which by then has its shape.</summary>
    public const double FollowFade = 0.4;

    /// <summary>Morph: the share of the way over which the frame turns from the pill's colour to the card's.</summary>
    public const double ColourTurn = 0.4;

    public static LiftStyle Parse(string? value) => value == "slide" ? LiftStyle.SlideUp : LiftStyle.Morph;

    public static string Key(LiftStyle style) => style == LiftStyle.SlideUp ? "slide" : "morph";

    /// <summary>
    /// The card centred on the big pill, pushed back in when it would come closer than <see cref="ScreenMargin"/> to a screen side,
    /// its bottom <see cref="TaskbarGap"/> above the taskbar. The window spans the card and the big pill (the morph starts on the pill;
    /// the slide comes out of the taskbar's top edge). With <paramref name="bar"/> (both pills) it spans them too, a cell wider each
    /// side: a swap while the card is up sends the card down into a task cell (user 10-08), and the window can't grow mid-flight
    /// without a jump.
    /// </summary>
    /// <para>With <paramref name="reserveTall"/> (an adaptive card) the window has room for 400 from the start, so a card that grows
    /// while open doesn't move its window (a frame's jump).</para>
    /// <para>With <paramref name="from"/> (lifted from a pill of the ⋯ list, F3) the window reaches that pill too, however high up.</para>
    public static LiftLayout Place(ScreenRect pill, int screenLeft, int screenRight, int taskbarTop, double scale, double height, ScreenRect? bar = null, bool reserveTall = false, ScreenRect? from = null)
    {
        var w = PillGeometry.Px(Width, scale);
        var h = PillGeometry.Px(height, scale);
        var roomFor = PillGeometry.Px(reserveTall ? Math.Max(height, TallHeight) : height, scale);
        var margin = PillGeometry.Px(ScreenMargin, scale);
        var left = pill.Left + ((pill.Width - w) / 2);
        left = Math.Max(screenLeft + margin, Math.Min(left, screenRight - margin - w));
        var bottom = taskbarTop - PillGeometry.Px(TaskbarGap, scale);
        var card = new ScreenRect(left, bottom - h, left + w, bottom);
        var room = PillGeometry.Px(Room, scale);
        var reach = bar is { } b ? b : pill;
        var cell = bar is null ? 0 : PillGeometry.Px(PillGeometry.Cell + SwapMotion.Overshoot, scale);
        var window = new ScreenRect(
            Math.Min(card.Left - room, Math.Min(pill.Left, reach.Left - cell)),
            Math.Min(card.Bottom - roomFor - room, from is { } f ? f.Top - room : int.MaxValue),
            Math.Max(card.Right + room, Math.Max(pill.Right, reach.Right + cell)),
            Math.Max(Math.Max(pill.Bottom, reach.Bottom), taskbarTop));
        return new LiftLayout(card, window);
    }

    public static TimeSpan OpenLength(bool bounce) => bounce ? OpenBounce : Open;

    /// <summary>The share of the way from the pill (0) to the card (1) at time share <paramref name="t"/> while opening (passes 1 a little with the bounce).</summary>
    public static double Opening(double t, bool bounce) => SwapMotion.Move(t, bounce);   // the swap's curve, one style

    /// <summary>The same while closing: the plain opening backwards.</summary>
    public static double Closing(double t) => SwapMotion.Ease(1 - t);

    /// <summary>The sheet's lean at share <paramref name="q"/> (degrees from upright). It never leans back past upright, nor further
    /// forward than the start — only the size and place spring, the angle just follows (the lean overshooting looked fussy).</summary>
    public static double TiltAt(double q) => Tilt * Math.Clamp(1 - q, 0, 1);

    /// <summary>A fade over the part of the way from <paramref name="from"/> to <paramref name="to"/> (0 before, 1 after).</summary>
    public static double Window(double p, double from, double to) => Math.Clamp((p - from) / (to - from), 0, 1);
}
