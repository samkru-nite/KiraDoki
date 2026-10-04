using System.Runtime.InteropServices;

namespace IsleBar.App.Interop;

/// <summary>
/// The Windows Start menu. It is drawn above every other window, so with it open the file results list was hidden under it, and
/// clicking outside to close it also ended typing and closed the list (user 10-04). A click on the pill now closes it first.
/// <para>
/// What was measured on the user's PC (10-04), so it isn't tried again: Start is drawn by <c>SearchHost</c> (a CoreWindow), not
/// <c>StartMenuExperienceHost</c>; that window can't be found by listing windows; a click on the pill moves the foreground to
/// the taskbar while Start stays up; our window becoming the foreground doesn't close it; and Windows won't bring it forward
/// for us, so Esc can't reach it. What does work, from anywhere: the Windows key — Start's own toggle.
/// </para>
/// </summary>
internal static class StartMenu
{
    private const string WindowClass = "Windows.UI.Core.CoreWindow";
    private static readonly string[] HostProcesses = ["SearchHost", "StartMenuExperienceHost"];
    private const int DWMWA_CLOAKED = 14;
    private const byte VK_LWIN = 0x5B;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static IntPtr _window;

    /// <summary>
    /// A window came to the front (from the foreground notification): remember it if it's Start's. It's still cloaked for its
    /// opening animation at that moment, so it isn't required to be showing yet.
    /// </summary>
    public static void Notice(IntPtr window)
    {
        if (window != IntPtr.Zero && window != _window && ClassOf(window) == WindowClass && FromStartHost(window))
        {
            _window = window;
        }
    }

    /// <summary>
    /// Closes Start if it's showing, with the Windows key. Only called for a click on the pill: the Win+S search panel lives in
    /// the same window, and there the Windows key would switch to Start instead — but with that panel open the pill is covered
    /// by Windows' own search box, so it can't be clicked (user 10-04).
    /// </summary>
    public static void CloseIfOpen()
    {
        if (_window == IntPtr.Zero || !IsWindowVisible(_window) || Cloaked(_window))
        {
            return;   // closed (cloaked while hidden), or never seen
        }

        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        for (var i = 0; i < 30 && !Cloaked(_window); i++)
        {
            Thread.Sleep(10);   // let it go before the results list appears, so the list isn't drawn under it
        }
    }

    private static bool FromStartHost(IntPtr window)
    {
        _ = GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return HostProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;   // gone meanwhile
        }
    }

    /// <summary>Kept by the compositor but not drawn — how Start's window is while closed.</summary>
    private static bool Cloaked(IntPtr window)
        => DwmGetWindowAttribute(window, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string ClassOf(IntPtr window)
    {
        var buffer = new char[64];
        var length = GetClassNameW(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, [Out] char[] name, int size);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
