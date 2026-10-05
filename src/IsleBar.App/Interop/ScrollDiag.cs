using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace IsleBar.App.Interop;

/// <summary>
/// TEMPORARY (10-05, TODO 7.9 step 1) — does a wheel / touchpad scroll over the pill reach us at all? The new design flips cards
/// with the wheel, and the pill is a child of the taskbar, so the wheel may go to the taskbar or only to the window in front.
/// Logs every wheel event over the pill from a low-level mouse hook (system layer) so it can be compared with what XAML's
/// PointerWheelChanged gets (UI layer, logged by MainWindow). All lines start with [scrolldiag]. Remove once the test is done.
/// </summary>
internal sealed class ScrollDiag : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const uint WM_QUIT = 0x0012;
    private const uint PM_NOREMOVE = 0;
    private const uint LLMHF_INJECTED = 0x1;

    private readonly Func<IntPtr> _pill;
    private readonly BlockingCollection<string> _lines = [];   // the hook must return fast — the file is written on another thread
    private readonly Thread _hookThread;
    private readonly Thread _writer;
    private uint _threadId;

    public ScrollDiag(Func<IntPtr> pill)
    {
        _pill = pill;
        Log("[scrolldiag] start — MouseWheelRouting=" + ReadRouting());
        _writer = new Thread(() =>
        {
            foreach (var line in _lines.GetConsumingEnumerable())
            {
                TaskbarHost.Log(line);
            }
        })
        { IsBackground = true, Name = "IsleBar scrolldiag log" };
        _writer.Start();

        var ready = new ManualResetEventSlim(false);
        HookProc proc = OnMouse;
        _hookThread = new Thread(() =>
        {
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            _threadId = GetCurrentThreadId();
            ready.Set();
            var hook = SetWindowsHookExW(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                _lines.Add("[scrolldiag] hook install FAILED, error " + Marshal.GetLastWin32Error());
            }

            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }

            UnhookWindowsHookEx(hook);
            GC.KeepAlive(proc);
        })
        { IsBackground = true, Name = "IsleBar scrolldiag hook" };
        _hookThread.Start();
        ready.Wait(1000);
    }

    public void Dispose()
    {
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _hookThread.Join(1000);
        _lines.CompleteAdding();
        _writer.Join(1000);
    }

    /// <summary>"Scroll inactive windows when I hover over them" — Control Panel\Desktop\MouseWheelRouting (2 = on, 0 = off).</summary>
    private static string ReadRouting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            return key?.GetValue("MouseWheelRouting")?.ToString() ?? "(missing)";
        }
        catch (Exception ex)
        {
            return "(read failed: " + ex.Message + ")";
        }
    }

    private static void Log(string line) => TaskbarHost.Log(line);

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam is WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var pill = _pill();
            if (GetWindowRect(pill, out var r) && info.pt.X >= r.Left && info.pt.X < r.Right && info.pt.Y >= r.Top && info.pt.Y < r.Bottom)
            {
                var under = WindowFromPoint(info.pt);
                var ours = under == pill || IsChild(pill, under);
                var front = GetForegroundWindow();
                _lines.Add($"[scrolldiag] HOOK {((int)wParam == WM_MOUSEHWHEEL ? "H" : "V")} delta={(short)(info.mouseData >> 16)}"
                    + $" injected={(info.flags & LLMHF_INJECTED) != 0} under={ClassOf(under)}{(ours ? "(ours)" : string.Empty)}"
                    + $" front={(front == pill ? "pill" : ClassOf(front))} t={info.time}");
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static string ClassOf(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return "none";
        }

        var name = new StringBuilder(128);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "?";
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int max);
}
