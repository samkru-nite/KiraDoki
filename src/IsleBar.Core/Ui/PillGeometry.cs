namespace IsleBar.Core.Ui;

/// <summary>One pill to clip, in window pixels. Right/bottom are one past the edge (the CreateRoundRectRgn rule).</summary>
public readonly record struct PillRect(int Left, int Top, int Right, int Bottom, int Diameter);

/// <summary>
/// The window holding both pills: its size and where each pill sits in it (window pixels). <see cref="Big"/> null = no card, only
/// the small pill shows.
/// </summary>
public sealed record PillsLayout(int Width, int Height, PillRect? Big, PillRect Small);

/// <summary>
/// Where the big and small pills sit in the bar's window (TODO 7.9 D final sizes, dip): small pill = 4 + 24 per cell + 4 on the left
/// (fixed 10-10, user), gap 8, big pill 220×32, and <see cref="ArcRoom"/> above and below for the swap animation. Pure math, so it can be checked here without a window —
/// being off by one pixel squashes a pill or shows a seam.
/// </summary>
public static class PillGeometry
{
    public const double BigWidth = 220;
    public const double Height = 32;
    public const double Gap = 8;

    /// <summary>Room above and below the pills: the swap animation's arc rises 7 (the taskbar has 7.5 there).</summary>
    public const double ArcRoom = 7;

    /// <summary>A task icon's cell on the small pill, and the space left and right of the cells.</summary>
    public const double Cell = 24;
    public const double CellMargin = 4;

    /// <summary>The circle's box: its cell with <see cref="CellMargin"/> either side.</summary>
    public const double BallBox = Cell + (2 * CellMargin);

    /// <summary>The line between the task cells and the circle.</summary>
    public const double Rule = 1;

    /// <summary>
    /// Width of the small pill with <paramref name="taskCells"/> task cells: the circle alone is 32; with tasks, the circle's 32 +
    /// the 1 line + 4 + 24 per cell + 4 (TODO 7.9 D). The 4 after the line came 10-11 (user: lopsided) — the circle sat 16 from the
    /// edge and 16 from the line, the first icon only 12 from the line; now both halves mirror each other.
    /// </summary>
    public static double SmallWidth(int taskCells) => BallBox + (taskCells > 0 ? Rule + CellMargin + (taskCells * Cell) + CellMargin : 0);

    /// <summary>Where the first task cell starts, from the small pill's left edge: after the circle, the line and 4.</summary>
    public const double FirstCell = BallBox + Rule + CellMargin;

    /// <summary>Dip → pixels, rounded the way XAML's layout rounding places elements.</summary>
    public static int Px(double dip, double scale) => (int)Math.Round(dip * scale);

    /// <param name="big">A card shows (or typing): the big pill is there.</param>
    /// <remarks>Corners are half the height, like Windows' search box (the 8 dip choice went 10-10).</remarks>
    public static PillsLayout Compute(double scale, bool big, double smallWidth)
    {
        var top = Px(ArcRoom, scale);
        var bottom = Px(ArcRoom + Height, scale);
        var diameter = bottom - top;
        PillRect Rect(double left, double width) => new(Px(left, scale), top, Px(left + width, scale) + 1, bottom + 1, diameter);

        var height = Px(Height + (2 * ArcRoom), scale);
        if (!big)
        {
            return new PillsLayout(Px(smallWidth, scale), height, null, Rect(0, smallWidth));
        }

        var total = BigWidth + Gap + smallWidth;
        return new PillsLayout(Px(total, scale), height, Rect(smallWidth + Gap, BigWidth), Rect(0, smallWidth));
    }
}
