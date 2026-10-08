using System.Runtime.InteropServices;

namespace IsleBar.App.Interop;

/// <summary>
/// Hears every mouse button press on the screen while armed, for popups that never take focus (they get no "lost focus" to close on).
/// The hook runs on its own thread with its own message loop: on the UI thread a low-level mouse hook made the cursor lag whenever
/// the UI was busy (ExpandedNotice, user 09-30). <see cref="OnPress"/> is called on that thread with the screen point; it must only
/// post work to the UI. Clicks always go on to whatever is under them.
/// </summary>
internal sealed class OutsideClick
{
    private readonly Action<int, int> _onPress;
    private HookProc? _proc;   // kept alive so the delegate isn't collected while hooked
    private IntPtr _hook;
    private Thread? _thread;
    private uint _threadId;

    public OutsideClick(Action<int, int> onPress) => _onPress = onPress;

    public void Arm()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _proc = Proc;
            _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, GetModuleHandleW(null), 0);
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }

            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            _proc = null;
        })
        { IsBackground = true, Name = "IsleBar outside-click" };
        _thread.Start();
    }

    public void Disarm()
    {
        if (_thread is null)
        {
            return;
        }

        PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);   // ends the loop → it unhooks
        _thread = null;
    }

    private IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN))
        {
            var pt = Marshal.PtrToStructure<NativeMethods.POINT>(lParam);   // MSLLHOOKSTRUCT starts with the screen point
            _onPress(pt.X, pt.Y);
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private const int WH_MOUSE_LL = 14;
    private const uint WM_QUIT = 0x0012;
    private static readonly IntPtr WM_LBUTTONDOWN = 0x0201;
    private static readonly IntPtr WM_RBUTTONDOWN = 0x0204;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativeMethods.POINT Point;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
}
