using IsleBar.App.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace IsleBar.App.Options;

/// <summary>Common handling for small windows popped up above the search box.</summary>
internal static class PopupPlacement
{
    /// <summary>
    /// Whether the mouse is over this popup right now. A click on a popup takes activation from the bar even with NOACTIVATE,
    /// and the bar ended typing on that — closing the popup before the click arrived (file results did nothing, a click on the
    /// long-question preview wiped the question; 10-03). The bar checks this before ending typing.
    /// </summary>
    public static bool UnderCursor(Window window)
    {
        if (!window.AppWindow.IsVisible || !NativeMethods.GetCursorPos(out var p)
            || !NativeMethods.GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(window), out var r))
        {
            return false;
        }

        return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
    }

    /// <summary>No title bar, fixed size, topmost, hidden from taskbar buttons/Alt+Tab. Rounded corners and shadow are the system's.</summary>
    public static void MakeToolPopup(Window window)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        window.AppWindow.IsShownInSwitchers = false;
    }

    /// <summary>
    /// Shows it off-screen first so the controls' templates are fully built before measuring, then moves it above the search box.
    /// (Measuring before showing gives a small width because button templates don't exist yet, and buttons overlap — measured on PC 09-29)
    /// </summary>
    public static void ShowSized(Window window, FrameworkElement content, int widthDip, int anchorX, int bandTop)
    {
        window.AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            content.Loaded -= OnLoaded;
            SizeToContent(window, content, widthDip);
            PlaceAbove(window, anchorX, bandTop);
        }

        content.Loaded += OnLoaded;
        window.Activate();
    }

    /// <summary>Fits to the content height (width is the larger of <paramref name="widthDip"/> and the content).</summary>
    public static void SizeToContent(Window window, FrameworkElement content, int widthDip)
    {
        // Width: the larger of the unconstrained content width (the minimum where buttons don't overlap) and the default width
        content.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Math.Ceiling(Math.Max(widthDip, content.DesiredSize.Width));
        // Lay out against the height the content wants at this width (a fixed 600px here clipped long prompts at ~13 lines; sizing to
        // the whole screen made the window flash full-height on every keystroke — user 10-01). Capped by the work area.
        content.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        var wanted = content.DesiredSize.Height;
        var workArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var layoutHeight = Math.Min(workArea.Height, Math.Max(DpiSetup.Px(600), DpiSetup.Px((int)Math.Ceiling(content.DesiredSize.Height)) + DpiSetup.Px(16)));
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(DpiSetup.Px((int)width), layoutHeight));

        // Height: the height actually arranged at that width (anchored at top, so leftover space isn't included)
        content.VerticalAlignment = VerticalAlignment.Top;
        content.UpdateLayout();
        // Size by the outer size: on a window without a title bar, ResizeClient grows by the height of the nonexistent title bar (measured on PC 09-29)
        var client = window.AppWindow.ClientSize;
        var outer = window.AppWindow.Size;
        var chromeW = outer.Width - client.Width;   // sum of left and right borders
        var hasTitle = window.AppWindow.Presenter is OverlappedPresenter { HasTitleBar: true };
        var chromeH = hasTitle ? outer.Height - client.Height : chromeW;   // includes the title bar height if there is one
        // ActualHeight can still be clipped to the window's previous size (the resize above lands asynchronously) — the long-prompt
        // compose window stayed ~100 tall under 40 lines (user 10-01) — so never go below the measured height.
        var height = DpiSetup.Px((int)Math.Ceiling(Math.Max(content.ActualHeight, wanted))) + chromeH;
        var work = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        height = Math.Min(height, work.Height - DpiSetup.Px(24));   // keep within the work area (above the taskbar) — if it overflows, scroll inside the window
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(DpiSetup.Px((int)width) + chromeW, height));
    }

    /// <summary>
    /// Tallest a popup above the search box may be, in DIP: from the top of the screen down to the taskbar, less a margin — long
    /// prompts grow up to this instead of stopping at a fixed number of lines (user 10-01: a long prompt was cut off).
    /// </summary>
    public static double MaxHeightAbove(int anchorX, int bandTop)
    {
        var area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(anchorX, bandTop - 1), DisplayAreaFallback.Primary);
        var px = bandTop - area.OuterBounds.Y - DpiSetup.Px(12) - DpiSetup.Px(24);
        return Math.Max(120, px / DpiSetup.Scale);
    }

    /// <summary>Places above the search box, centred on <paramref name="anchorX"/> (the pill's centre), without going off-screen.</summary>
    public static void PlaceAbove(Window window, int anchorX, int bandTop)
    {
        var size = window.AppWindow.Size;
        var area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(anchorX, bandTop - 1), DisplayAreaFallback.Primary);
        var right = area.OuterBounds.X + area.OuterBounds.Width;
        var x = Math.Max(area.OuterBounds.X + DpiSetup.Px(8), Math.Min(anchorX - (size.Width / 2), right - size.Width - DpiSetup.Px(8)));
        var y = Math.Max(area.OuterBounds.Y, bandTop - size.Height - DpiSetup.Px(12));
        window.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }
}
