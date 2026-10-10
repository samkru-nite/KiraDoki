namespace IsleBar.Core.Ui;

/// <summary>
/// Where on the taskbar a drag is caught (TODO ④ 乙 / 丙, user 10-10): files coming in for the transit station, and the station
/// itself dragged out and brought back. The whole taskbar height, so nothing dragged ever crosses a "no drop" taskbar near the pills
/// (the 🚫 and the flicker). Only while something is being dragged — the rest of the time that stretch of taskbar is Windows'.
/// This part: how wide it is, in screen pixels.
/// <para>
/// The empty stretch the pills sit in, the wider the better (user, option B): left and right up to the nearest of Windows' own
/// things — the app icons, the Widgets button, the tray — keeping <c>gap</c> from them, so a file can still be held over an app's
/// icon to bring its window up. Never narrower than the pills themselves. The same for every position (left / centre / by hand).
/// </para>
/// </summary>
public static class DropZone
{
    /// <param name="pillsLeft">The pills' left end on screen (the circle's).</param>
    /// <param name="pillsRight">The pills' right end: the big pill's, or the small pill's when there's no big pill.</param>
    /// <param name="gap">Room kept from Windows' buttons (8 dip, in pixels).</param>
    public static (int Left, int Right) Span(TaskbarLayout? bar, int pillsLeft, int pillsRight, int gap)
    {
        if (bar is null)
        {
            return (pillsLeft, pillsRight);   // the taskbar isn't read yet: the pills only
        }

        var left = bar.Left;
        var right = bar.Right;
        foreach (var (from, to) in Taken(bar))
        {
            if (to <= pillsLeft)
            {
                left = Math.Max(left, to + gap);   // left of the pills
            }
            else if (from >= pillsRight)
            {
                right = Math.Min(right, from - gap);   // right of them
            }
        }

        return (Math.Min(left, pillsLeft), Math.Max(right, pillsRight));
    }

    /// <summary>The stretches Windows' own buttons take.</summary>
    private static IEnumerable<(int From, int To)> Taken(TaskbarLayout bar)
    {
        if (bar.IconsLeft is { } iconsLeft && bar.IconsRight is { } iconsRight)
        {
            yield return (iconsLeft, iconsRight);
        }

        if (bar.WidgetsRight is { } widgets)
        {
            yield return (bar.Left, widgets);
        }

        if (bar.TrayLeft is { } tray)
        {
            yield return (tray, bar.Right);
        }
    }
}
