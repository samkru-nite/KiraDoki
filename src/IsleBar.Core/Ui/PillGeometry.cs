namespace IsleBar.Core.Ui;

/// <summary>One pill to clip, in window pixels. Right/bottom are one past the edge (the CreateRoundRectRgn rule).</summary>
public readonly record struct PillRect(int Left, int Top, int Right, int Bottom, int Diameter);

/// <summary>
/// The window holding both pills: its size and where each pill sits in it (window pixels). <see cref="Big"/> null = no card, only
/// the small pill shows.
/// </summary>
public sealed record PillsLayout(int Width, int Height, PillRect? Big, PillRect Small);

/// <summary>
/// Where the big and small pills sit in the bar's window (TODO 7.9 D final sizes, dip): big pill 220×32, gap 8, small pill = 4 + 24 per
/// cell + 4, and <see cref="ArcRoom"/> above and below for the swap animation. Pure math, so it can be checked here without a window —
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

    /// <summary>The "small" corner choice (setting): 8, like Windows' cards. "round" = half the height.</summary>
    public const double SmallCorner = 8;

    /// <summary>Width of the small pill holding <paramref name="cells"/> cells (the circle counts as one).</summary>
    public static double SmallWidth(int cells) => (2 * CellMargin) + (Math.Max(1, cells) * Cell);

    /// <summary>Dip → pixels, rounded the way XAML's layout rounding places elements.</summary>
    public static int Px(double dip, double scale) => (int)Math.Round(dip * scale);

    /// <param name="big">A card shows (or typing): the big pill is there.</param>
    /// <param name="smallLeft">The small pill sits left of the big one (setting).</param>
    /// <param name="round">Corners are half the height; false = <see cref="SmallCorner"/>.</param>
    public static PillsLayout Compute(double scale, bool big, bool smallLeft, double smallWidth, bool round)
    {
        var top = Px(ArcRoom, scale);
        var bottom = Px(ArcRoom + Height, scale);
        var diameter = round ? bottom - top : Px(2 * SmallCorner, scale);
        PillRect Rect(double left, double width) => new(Px(left, scale), top, Px(left + width, scale) + 1, bottom + 1, diameter);

        var height = Px(Height + (2 * ArcRoom), scale);
        if (!big)
        {
            return new PillsLayout(Px(smallWidth, scale), height, null, Rect(0, smallWidth));
        }

        var total = BigWidth + Gap + smallWidth;
        return smallLeft
            ? new PillsLayout(Px(total, scale), height, Rect(smallWidth + Gap, BigWidth), Rect(0, smallWidth))
            : new PillsLayout(Px(total, scale), height, Rect(0, BigWidth), Rect(BigWidth + Gap, smallWidth));
    }
}
