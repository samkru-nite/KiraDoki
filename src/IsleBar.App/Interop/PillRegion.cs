using IsleBar.Core.Ui;
using static IsleBar.App.Interop.NativeMethods;

namespace IsleBar.App.Interop;

/// <summary>
/// Clips the bar's window to the pills (TODO 7.9 E1): the union of the big and small pill's <b>rectangles</b>, so the real taskbar
/// shows in the gap between them. Rectangles, not rounded shapes: a clip edge can't be anti-aliased, and a clip cut along the round
/// ends left stair-steps on them (user 10-07). Inside the rectangles the window background is the taskbar colour and the pills draw
/// their own soft round edge on it (with no clip at all the user saw no box around the pills — E1 test).
/// </summary>
internal static class PillRegion
{
    /// <summary>
    /// Clips the window. Region ownership passes to the window, so <b>do not DeleteObject</b> it after success (deleting it breaks
    /// the next paint).
    /// </summary>
    public static void Apply(IntPtr window, PillsLayout layout)
    {
        var region = Box(layout.Small);
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (layout.Big is { } big)
        {
            Add(region, Box(big));
        }

        if (SetWindowRgn(window, region, redraw: true) == 0)
        {
            DeleteObject(region);   // on failure ownership was not transferred, so we delete it
        }
    }

    /// <summary>The whole window, no clip — E4's swap animation flies outside the pills this way (E1 test: clipping every frame
    /// left the edges looking discoloured, no clip looked right).</summary>
    public static void Clear(IntPtr window) => SetWindowRgn(window, IntPtr.Zero, redraw: true);

    private static IntPtr Box(PillRect r) => CreateRectRgn(r.Left, r.Top, r.Right - 1, r.Bottom - 1);

    private static void Add(IntPtr into, IntPtr part)
    {
        if (part == IntPtr.Zero)
        {
            return;
        }

        CombineRgn(into, into, part, RGN_OR);
        DeleteObject(part);
    }

    /// <summary>Turns off the system's rounded corners, border and shadow (we draw the shape ourselves).</summary>
    public static void DisableSystemFrame(IntPtr window)
    {
        var flat = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(window, DWMWA_WINDOW_CORNER_PREFERENCE, ref flat, sizeof(int));
        var none = DWMWA_COLOR_NONE;
        DwmSetWindowAttributeUInt(window, DWMWA_BORDER_COLOR, ref none, sizeof(uint));
    }
}
