using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IsleBar.App.Interop;

/// <summary>
/// The windows of chat apps whose links can't be opened from outside (TODO 四, measured 10-11): a card's ↗ brings the app's window
/// up instead. QQ refuses both its notification link (<c>ntqq-notification://…</c>, "当前QQ暂不支持该内容" — even the very string
/// Windows hands it) and <c>tencent://message/</c> ("链接有潜在风险"); starting QQ.exe again opens a second login. So QQ's main
/// window is brought up the way the person would: on the taskbar, restored as a taskbar click does it (<c>WM_SYSCOMMAND
/// SC_RESTORE</c>); only in the tray, its tray icon is "clicked" — a window shown from outside while hidden comes back drawn but
/// deaf to clicks (QQ still thinks it is hidden), while a tray click has QQ open it itself.
/// Log "chat window: …".
/// </summary>
internal static class ChatWindows
{
    /// <summary>QQ's notifications come under this app id (the notification database's handler, 10-11).</summary>
    public const string QqApp = "QQ";

    private const string QqExe = "QQ";
    private const string QqClass = "Chrome_WidgetWin_1";   // QQ NT is Electron; several hidden windows share the title, the main one is the shown one
    private const string QqTitle = "QQ";

    // Electron's tray (shell/browser/ui/win/notify_icon_host.cc): one hidden host window per app gets the icons' mouse messages as
    // WM_APP + 1, the icon's id in wParam and the mouse message in lParam. Ids start at 2 and grow when the icon is made again
    // (QQ's was 3, 10-11), so the one that exists is asked for. The Claude app is Electron too: the host is QQ's only by its process.
    private const string TrayHostClass = "Electron_NotifyIconHostWindow";
    private const uint TrayMessage = 0x8000 + 1;
    private const int MostIcons = 32;

    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(1);   // the pill asks every render; the window doesn't come and go faster than this
    private static DateTime _lookedAt;
    private static IntPtr _qq;
    private static Tray? _qqTray;

    private sealed record Tray(IntPtr Host, uint Icon, uint Process);

    /// <summary>Whether QQ's window can be brought up now: QQ is running, on the taskbar or in the tray.</summary>
    public static bool CanOpenQq()
    {
        Look();
        return _qq != IntPtr.Zero || _qqTray is not null;
    }

    /// <summary>Brings QQ's main window up (its ↗, TODO 四): restored from the taskbar, or opened by QQ itself from its tray icon.</summary>
    public static bool OpenQq()
    {
        _lookedAt = default;   // as it is now, not a second ago
        Look();
        if (_qq != IntPtr.Zero)
        {
            return Show(_qq, "QQ");
        }

        if (_qqTray is not { } tray)
        {
            TaskbarHost.Log("chat window: QQ — not running");
            return false;
        }

        AllowSetForegroundWindow(tray.Process);   // we have the click; QQ may come to the front with it
        PostMessage(tray.Host, TrayMessage, (IntPtr)tray.Icon, (IntPtr)MouseDown);
        PostMessage(tray.Host, TrayMessage, (IntPtr)tray.Icon, (IntPtr)MouseUp);
        TaskbarHost.Log($"chat window: QQ from its tray icon {tray.Icon}");
        return true;
    }

    private static void Look()
    {
        var now = DateTime.UtcNow;
        if (now - _lookedAt < Fresh)
        {
            return;
        }

        _lookedAt = now;
        var ids = ProcessIds(QqExe);
        _qq = Find(ids, QqClass, QqTitle, shown: true);
        _qqTray = null;
        if (_qq == IntPtr.Zero && Find(ids, TrayHostClass, string.Empty, shown: false) is var host && host != IntPtr.Zero)
        {
            GetWindowThreadProcessId(host, out var pid);
            for (uint icon = 1; icon <= MostIcons; icon++)
            {
                var asked = new IconId { Size = (uint)Marshal.SizeOf<IconId>(), Window = host, Id = icon };
                if (Shell_NotifyIconGetRect(ref asked, out _) == 0)   // only asks where it is: nothing is clicked
                {
                    _qqTray = new Tray(host, icon, pid);
                    break;
                }
            }
        }
    }

    // ---- WeChat (10-11): its card remembers the window that flashed; that window is brought up, even from the tray ----

    private const string WeChatExe = "weixin.exe";
    private const string LinkHead = "window:";

    /// <summary>Whether this window is WeChat's (by its program, so no other app's flash counts — QQ flashes too).</summary>
    public static bool IsWeChat(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return string.Equals(process.ProcessName + ".exe", WeChatExe, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>A window's title ("秦鹏宇" for a chat's own window, "微信" for the main one).</summary>
    public static string Title(IntPtr window)
    {
        var text = new char[256];
        var n = GetWindowText(window, text, text.Length);
        return new string(text, 0, Math.Max(0, n)).Trim();
    }

    /// <summary>What a card keeps in its <c>Open</c> to bring this window up later.</summary>
    public static string LinkTo(IntPtr window) => LinkHead + window.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The window a card's <c>Open</c> names (<see cref="LinkTo"/>), while it is still there; zero otherwise.</summary>
    public static IntPtr Linked(string? open)
        => open is not null && open.StartsWith(LinkHead, StringComparison.Ordinal)
           && long.TryParse(open.AsSpan(LinkHead.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var h)
           && IsWindow((IntPtr)h)
            ? (IntPtr)h
            : IntPtr.Zero;

    /// <summary>Whether this <c>Open</c> is a window link at all (then it is never handed to the shell).</summary>
    public static bool IsLink(string? open) => open?.StartsWith(LinkHead, StringComparison.Ordinal) == true;

    /// <summary>
    /// Brings a WeChat window up — shown again if closed to the tray (hidden: WeChat draws it and takes clicks, user 10-11), restored
    /// if minimized, then in front.
    /// </summary>
    public static bool ShowWeChat(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
        {
            TaskbarHost.Log("chat window: WeChat — its window is gone");
            return false;
        }

        if (!IsWindowVisible(window))
        {
            ShowWindow(window, ShowShown);
        }

        if (IsIconic(window))
        {
            ShowWindow(window, ShowRestore);
        }

        ForegroundHelper.Bring(window);
        TaskbarHost.Log("chat window: WeChat shown");
        return true;
    }

    private const int ShowShown = 5;     // SW_SHOW
    private const int ShowRestore = 9;   // SW_RESTORE

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    /// <summary>Brings a window up as a taskbar click does: restored if minimized, then in front. False when it is gone.</summary>
    public static bool Show(IntPtr window, string what)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
        {
            TaskbarHost.Log($"chat window: {what} — no window");
            return false;
        }

        if (IsIconic(window))
        {
            PostMessage(window, SysCommand, Restore, IntPtr.Zero);   // the app restores itself, as from its taskbar button
        }

        ForegroundHelper.Bring(window);
        TaskbarHost.Log($"chat window: {what} shown");
        return true;
    }

    private static HashSet<uint> ProcessIds(string process)
    {
        var ids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(process))
        {
            ids.Add((uint)p.Id);
            p.Dispose();
        }

        return ids;
    }

    /// <summary>The first top-level window of these processes with this class and title (shown ones only, if asked).</summary>
    private static IntPtr Find(HashSet<uint> ids, string windowClass, string title, bool shown)
    {
        if (ids.Count == 0)
        {
            return IntPtr.Zero;
        }

        var found = IntPtr.Zero;
        var text = new char[64];
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var pid);
            if (!ids.Contains(pid) || (shown && !IsWindowVisible(window))
                || !Same(text, GetClassName(window, text, text.Length), windowClass)
                || !Same(text, GetWindowText(window, text, text.Length), title))
            {
                return true;
            }

            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool Same(char[] text, int length, string expected)
        => length == expected.Length && expected.AsSpan().SequenceEqual(text.AsSpan(0, length));

    private const uint SysCommand = 0x0112;
    private static readonly IntPtr Restore = 0xF120;
    private const int MouseDown = 0x0201;   // WM_LBUTTONDOWN
    private const int MouseUp = 0x0202;

    [StructLayout(LayoutKind.Sequential)]
    private struct IconId   // NOTIFYICONIDENTIFIER
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public Guid Item;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref IconId icon, out Rect place);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, char[] text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, char[] text, int max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
