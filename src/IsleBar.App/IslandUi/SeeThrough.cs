using System.Runtime.InteropServices;
using IsleBar.App.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using static IsleBar.App.Interop.NativeMethods;

namespace IsleBar.App.IslandUi;

/// <summary>
/// Makes a window a see-through, never-focused, topmost popup over the taskbar (the lifted card F1, the ⋯ list F3): no frame, no
/// system edge or rounding, per-pixel alpha so XAML draws its own anti-aliased shapes over whatever is behind. Starts off-screen.
/// </summary>
internal static class SeeThrough
{
    /// <param name="typing">The input panel (TODO 7.9 H): it takes the keyboard, so it can be activated — everything else as the others.</param>
    public static IntPtr Make(Window window, string name, bool typing = false)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        window.AppWindow.IsShownInSwitchers = false;
        window.SystemBackdrop = new TransparentBackdrop();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var ex = Unsigned32(GetWindowLong(hwnd, GWL_EXSTYLE));
        SetWindowLong(hwnd, GWL_EXSTYLE, Signed32(ex | (typing ? 0 : WS_EX_NOACTIVATE) | WS_EX_TOOLWINDOW));

        // A plain popup, no frame at all: with the frame styles left on (even without a title bar) a 1 px white system edge showed
        // around the see-through window (user 10-08 screenshot).
        SetWindowLong(hwnd, GWL_STYLE, Signed32(WS_POPUP | WS_CLIPSIBLINGS));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        var none = DWMWA_COLOR_NONE;
        var square = DWMWCP_DONOTROUND;
        var hrBorder = DwmSetWindowAttributeUInt(hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(uint));
        var hrCorner = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));
        // The transparent backdrop alone came out black where nothing is drawn (user 10-08 screenshots). DWM only composes a
        // window with its per-pixel alpha once "blur behind" is on; an empty, off-window blur region means nothing gets blurred.
        var nothing = CreateRectRgn(-2, -2, -1, -1);
        var blur = new BlurBehind { Flags = DWM_BB_ENABLE | DWM_BB_BLURREGION, Enable = true, Region = nothing };
        var hrAlpha = DwmEnableBlurBehindWindow(hwnd, ref blur);
        DeleteObject(nothing);
        var host = 1;   // the host backdrop brush (the card's live glass: what's behind the window) only draws with this on
        var hrHost = DwmSetWindowAttribute(hwnd, 17 /* DWMWA_USE_HOSTBACKDROPBRUSH */, ref host, sizeof(int));
        TaskbarHost.Log($"{name}: host backdrop hr={hrHost:X}");
        TaskbarHost.Log($"{name}: per-pixel alpha (blur behind) hr={hrAlpha:X}");
        TaskbarHost.Log($"{name}: window made, style={Unsigned32(GetWindowLong(hwnd, GWL_STYLE)):X} dwm border={hrBorder:X} corner={hrCorner:X}");
        window.AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
        return hwnd;
    }

    /// <summary>Puts the window back on top: a click on the taskbar lifts the taskbar over every topmost window (log 10-08).</summary>
    public static void OnTop(IntPtr hwnd) => SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    private const uint DWM_BB_ENABLE = 0x1;
    private const uint DWM_BB_BLURREGION = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Enable;
        public IntPtr Region;
        [MarshalAs(UnmanagedType.Bool)]
        public bool TransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr window, ref BlurBehind blur);
}
