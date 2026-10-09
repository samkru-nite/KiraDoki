namespace IsleBar.Core.Ui;

/// <summary>
/// The input panel's size and place (TODO 7.9 H, user 10-09): 361 wide — the big pill 220 + gap 8 + a small pill with four icons
/// 133, so with four icons both edges line up (and about Windows 11's 360 taskbar flyouts). Like the lifted card: centred on the
/// pills (the whole group, big + small; the small pill alone when there's no big one), pushed in to keep 12 from the screen's
/// edges, its bottom 8 above the taskbar. It grows out of the circle and shrinks back into it, in the lifted card's time.
/// </summary>
public static class PanelMotion
{
    /// <summary>The panel's width (dip).</summary>
    public const double Width = 361;

    /// <summary>The circle it grows out of (a cell's 24).</summary>
    public const double Circle = 24;

    /// <summary>Corner of the open panel (as the card's).</summary>
    public const double Radius = 8;

    /// <summary>Room inside the window around the panel for the bounce to overshoot into (dip).</summary>
    public const double Room = 12;

    /// <summary>Inside: 8 around the list and the input box, 4 between them.</summary>
    public const double Pad = 8;

    public const int MaxLines = 4;

    /// <summary>The box's text above and below its lines; the field's room around the box.</summary>
    public const double BoxPad = 4;

    public const double FieldPad = 4;

    /// <summary>The box's height with <paramref name="lines"/> lines <paramref name="pitch"/> apart (measured in the box's font: the
    /// lines of 14 text came 18.5 apart, not 20 — four of 20 showed a cut fifth line at the top, user 10-09), at least the mode
    /// button's 28; more than <see cref="MaxLines"/> scroll inside.</summary>
    public static double BoxHeight(int lines, double pitch) => Math.Max(28, (Math.Clamp(lines, 1, MaxLines) * pitch) + (2 * BoxPad));

    /// <summary>The input field (the box with the room around it).</summary>
    public static double FieldHeight(int lines, double pitch) => BoxHeight(lines, pitch) + (2 * FieldPad);

    /// <summary>
    /// The panel on screen (pixels), <paramref name="height"/> dip tall: centred on <paramref name="group"/> (the pills), kept
    /// <see cref="LiftMotion.ScreenMargin"/> inside the screen, its bottom <see cref="LiftMotion.TaskbarGap"/> above the taskbar's top.
    /// </summary>
    public static ScreenRect Place(ScreenRect group, int screenLeft, int screenRight, int taskbarTop, double scale, double height)
    {
        var w = (int)Math.Round(Width * scale);
        var h = (int)Math.Round(height * scale);
        var margin = (int)Math.Round(LiftMotion.ScreenMargin * scale);
        var centre = group.Left + (group.Width / 2);
        var left = Math.Clamp(centre - (w / 2), screenLeft + margin, Math.Max(screenLeft + margin, screenRight - margin - w));
        var bottom = taskbarTop - (int)Math.Round(LiftMotion.TaskbarGap * scale);
        return new ScreenRect(left, bottom - h, left + w, bottom);
    }

    /// <summary>The window: the panel and the circle it grows out of, with <see cref="Room"/> around for the bounce.</summary>
    public static ScreenRect Window(ScreenRect panel, ScreenRect circle, double scale)
    {
        var room = (int)Math.Round(Room * scale);
        return new ScreenRect(
            Math.Min(panel.Left, circle.Left) - room,
            panel.Top - room,
            Math.Max(panel.Right, circle.Right) + room,
            Math.Max(panel.Bottom, circle.Bottom) + room);
    }

    /// <summary>The shape at share <paramref name="p"/> of the way from the circle (0) to the panel (1), in the same units as both.</summary>
    public static MotionBox Between(MotionBox circle, MotionBox panel, double p)
    {
        double Lerp(double a, double b) => a + ((b - a) * p);
        var w = Math.Max(1, Lerp(circle.Width, panel.Width));
        var h = Math.Max(1, Lerp(circle.Height, panel.Height));
        // a circle while it's small (half its height), the panel's 8 once it's grown
        var r = Math.Min(Lerp(circle.Radius, panel.Radius) + Math.Max(0, (1 - p) * (Math.Min(w, h) / 2 - panel.Radius)), Math.Min(w, h) / 2);
        return new MotionBox(Lerp(circle.X, panel.X), Lerp(circle.Y, panel.Y), w, h, r);
    }
}
