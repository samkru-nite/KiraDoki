using System.Runtime.InteropServices;

namespace IsleBar.App.Interop;

/// <summary>
/// Windows tells us the moment another window comes to the front (a click on the desktop, another app, Alt+Tab) — so typing can
/// end right then, instead of relying on the input box losing focus. That never happened while the keyboard sat on the results
/// list, and a click away then left the list open (user 10-04). Must be created on a thread with a message loop (the UI thread).
/// </summary>
internal sealed class ForegroundEvents : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private readonly WinEventProc _proc;   // kept: the hook calls it for as long as it's installed
    private IntPtr _hook;

    /// <param name="changed">Called with the window that just came to the front.</param>
    public ForegroundEvents(Action<IntPtr> changed)
    {
        _proc = (_, _, window, _, _, _, _) => changed(window);
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Whether a window belongs to IsleBar (the bar, the results list, its menus, the compose box…).</summary>
    public static bool IsOurs(IntPtr window)
    {
        _ = GetWindowThreadProcessId(window, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
