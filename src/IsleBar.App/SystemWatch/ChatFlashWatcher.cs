using System.Runtime.InteropServices;
using IsleBar.Core.Localization;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// WeChat's new messages (TODO 四, measured 10-11): WeChat 4 sends no Windows notification and opens no window of its own — it only
/// flashes its taskbar button, which Windows tells a shell hook (<c>HSHELL_FLASH</c>). The window that flashes is the chat's own when
/// the person has it open apart ("秦鹏宇", "文件传输助手"), else the main "微信" — even closed to the tray (hidden, it still flashes and
/// can be brought up). So a card "秦鹏宇 新消息" / "微信 新消息" — who wrote, not what (it isn't given) — as an app notification (the
/// app's icon, merged "+N", 6 s), its ↗ / "open" bringing up the window that flashed. A message flashes a few times over about two
/// seconds: flashes of one window this close are one. Only WeChat's windows count (QQ flashes too, but it notifies). Log "wechat: …".
/// </summary>
internal sealed class ChatFlashWatcher(Func<LanguageStrings> strings) : NoticeWatcher(strings)
{
    /// <summary>The app id the card goes under: its exe, which <see cref="Interop.AppNames"/> turns into WeChat's name and icon.</summary>
    public const string WeChatApp = "Weixin.exe";

    private const int HshellFlash = 0x8006;
    private static readonly TimeSpan SameMessage = TimeSpan.FromSeconds(4);   // one message = flashes over ~2 s (10-11)

    private readonly Dictionary<IntPtr, DateTimeOffset> _lastFlash = [];
    private Thread? _thread;
    private uint _threadId;
    private WndProc? _proc;   // held: the window calls it for as long as it lives
    private uint _shellMessage;
    private long _seq;

    public override void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "IsleBar chat flashes" };
        _thread.Start();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _proc = OnMessage;
        var instance = GetModuleHandle(null);
        var cls = new WindowClass { Proc = _proc, Instance = instance, Name = "IsleBarChatFlash" };
        RegisterClass(ref cls);
        var window = CreateWindowEx(0, "IsleBarChatFlash", "IsleBar chat flashes", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        _shellMessage = RegisterWindowMessage("SHELLHOOK");
        var hooked = window != IntPtr.Zero && RegisterShellHookWindow(window);
        Interop.TaskbarHost.Log($"wechat: listening for taskbar flashes {(hooked ? "on" : "failed")}");
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (window != IntPtr.Zero)
        {
            DeregisterShellHookWindow(window);
            DestroyWindow(window);
        }
    }

    private IntPtr OnMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _shellMessage && wParam.ToInt64() == HshellFlash)
        {
            Guard(() => Flashed(lParam));
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private void Flashed(IntPtr window)
    {
        if (!Interop.ChatWindows.IsWeChat(window))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var again = _lastFlash.TryGetValue(window, out var last) && now - last < SameMessage;
        _lastFlash[window] = now;
        if (again)
        {
            return;
        }

        foreach (var gone in _lastFlash.Where(f => now - f.Value > TimeSpan.FromMinutes(10)).Select(f => f.Key).ToList())
        {
            _lastFlash.Remove(gone);
        }

        // the window's title is who wrote: a chat's own window is named after it, the main one "微信"
        var who = Interop.ChatWindows.Title(window) is { Length: > 0 } title ? title : Interop.AppNames.For(WeChatApp);
        var text = Text;
        var notice = SystemNotice.Make(who, NoticeGlyphs.Bell, now, msg: text.NoticeNewMessage, open: Interop.ChatWindows.LinkTo(window));
        notice.Stage = "expand";
        notice.Name = Interop.AppNames.For(WeChatApp);
        notice.AppId = WeChatApp;
        notice.SourcePath = SystemNotice.SourcePrefix + "toast:" + WeChatApp;   // one card for WeChat, as one for each app's notifications
        Board.Flash("wechat:" + Interlocked.Increment(ref _seq).ToString(System.Globalization.CultureInfo.InvariantCulture), notice,
            NoticeLengths.ToRead(Core.Ui.TextGap.Plain(Core.Ui.TextGap.Join(notice.Title, notice.Msg))), now);
        Interop.TaskbarHost.Log($"wechat: a new message ({(who == notice.Name ? "main window" : "a chat's own window")})");
    }

    protected override void Stop()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);   // WM_QUIT: the loop ends, the hook goes with the window
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public WndProc Proc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? Menu;
        public string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WindowClass cls);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterShellHookWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeregisterShellHookWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg msg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
