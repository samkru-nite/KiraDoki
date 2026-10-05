namespace IsleBar.Core.Ui;

/// <summary>Where on the taskbar the bar sits (TODO 7.1, user 10-05). Default: the left end.</summary>
public enum BarPosition
{
    /// <summary>The left end of the taskbar (icons centred, widgets off — measured from the screen's left edge, the steadiest spot).</summary>
    Left,

    /// <summary>The middle of the screen (for icons on the left); moves right when the icons reach it.</summary>
    Center,

    /// <summary>Just left of the tray (clock, network, …).</summary>
    BesideTray,

    /// <summary>A fixed distance from the taskbar's left end, set by hand (calibration when the others don't fit a setup).</summary>
    Manual,
}

/// <summary>
/// What is on the taskbar, in screen pixels: its own edges, and the stretches taken by Windows' buttons. Null = not there / not found.
/// </summary>
/// <param name="IconsLeft">Left end of the icon group (Start, search, pinned and running apps).</param>
/// <param name="IconsRight">Right end of the icon group.</param>
/// <param name="TrayLeft">Left end of the tray.</param>
/// <param name="WidgetsRight">Right end of the Widgets button when it sits left of the icons.</param>
public sealed record TaskbarLayout(int Left, int Top, int Right, int Bottom, int? IconsLeft, int? IconsRight, int? TrayLeft, int? WidgetsRight);

/// <summary>Where the bar goes. <paramref name="Fits"/> false = no room there without covering something (it is hidden).</summary>
public sealed record BarSpot(bool Fits, int X, int Y);

/// <summary>
/// Picks the bar's spot on the taskbar. Pure arithmetic over <see cref="TaskbarLayout"/>, so the rules can be tested; the app
/// reads the layout (UI Automation) and only moves the window. Never covers Windows' own buttons: no room → <see cref="BarSpot.Fits"/> false.
/// </summary>
public static class BarSlot
{
    /// <param name="topEdge">The light line along the taskbar's top (1 dip on Windows 11). It reads as a border, so the bar is centred
    /// in the taskbar below it — centred over the whole height, the gap above looked 2 px short at 200% (user screenshot 10-05).</param>
    public static BarSpot Place(BarPosition position, TaskbarLayout bar, int width, int height, int gap, int manualOffset, int topEdge = 0)
    {
        ArgumentNullException.ThrowIfNull(bar);
        var y = bar.Top + topEdge + ((bar.Bottom - bar.Top - topEdge - height) / 2);
        var trayLeft = bar.TrayLeft ?? bar.Right;
        switch (position)
        {
            case BarPosition.Manual:
            {
                // the person chose it: kept on the taskbar, nothing else checked
                var x = Math.Clamp(bar.Left + manualOffset, bar.Left, Math.Max(bar.Left, bar.Right - width));
                return new BarSpot(true, x, y);
            }

            case BarPosition.BesideTray:
            {
                var x = trayLeft - gap - width;
                return new BarSpot(x >= bar.Left && (bar.IconsRight is not { } icons || x >= icons + gap), x, y);
            }

            case BarPosition.Center:
            {
                var x = bar.Left + ((bar.Right - bar.Left - width) / 2);
                if (bar.IconsLeft is { } from && bar.IconsRight is { } to && x < to + gap && x + width > from - gap)
                {
                    x = to + gap;   // the icons reach the middle: step aside to their right
                }

                return new BarSpot(x + width <= trayLeft - gap, x, y);
            }

            default:
            {
                var x = (bar.WidgetsRight ?? bar.Left) + gap;
                return new BarSpot(bar.IconsLeft is not { } icons || x + width <= icons - gap, x, y);
            }
        }
    }

    /// <summary>Settings value ↔ position ("left" · "center" · "tray" · "manual"; anything else = left).</summary>
    public static BarPosition Parse(string? value) => value switch
    {
        "center" => BarPosition.Center,
        "tray" => BarPosition.BesideTray,
        "manual" => BarPosition.Manual,
        _ => BarPosition.Left,
    };

    public static string Key(BarPosition position) => position switch
    {
        BarPosition.Center => "center",
        BarPosition.BesideTray => "tray",
        BarPosition.Manual => "manual",
        _ => "left",
    };
}
