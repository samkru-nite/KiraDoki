using System.Diagnostics;
using System.Runtime.InteropServices;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// Copy confirmation: text → "Copied · first 24 chars" for 1.5 s, image → "Image copied", files → "3 files copied".
/// <para>
/// Why not WinRT <c>Clipboard.ContentChanged</c>: it must be subscribed on the UI thread (dispatcher), and
/// unpackaged apps can read the content only while their window is in front. Instead we hook
/// <c>AddClipboardFormatListener</c> on a <b>message-only window on a dedicated thread</b>, receive WM_CLIPBOARDUPDATE and read via Win32 directly —
/// independent of the UI thread, so <see cref="Start"/> can be called from any thread.
/// </para>
/// <para>
/// Passwords: password managers mark copies as "don't record" (<c>ExcludeClipboardContentFromMonitorProcessing</c>,
/// <c>CanIncludeInClipboardHistory</c>=0, <c>Clipboard Viewer Ignore</c>) — then we don't read the content and show "••••••".
/// Even without the mark, it's masked if the copying program is a known manager or the text looks like a password (<see cref="ClipboardRules"/>).
/// </para>
/// Browsers send WM_CLIPBOARDUPDATE several times per copy, so we coalesce for 150 ms and read only once.
/// </summary>
internal sealed class ClipboardWatcher : NoticeWatcher
{
    private const string Key = "clipboard";
    private const uint CoalesceMs = 150;
    private const int MaxTextChars = 4096;

    private const uint WmClipboardUpdate = 0x031D;
    private const uint WmTimer = 0x0113;
    private const uint WmQuit = 0x0012;
    private const uint CfBitmap = 2;
    private const uint CfDib = 8;
    private const uint CfUnicodeText = 13;
    private const uint CfHdrop = 15;
    private const uint CfDibV5 = 17;
    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly UIntPtr TimerId = new(1);

    private readonly uint _formatExclude = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private readonly uint _formatHistory = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private readonly uint _formatViewerIgnore = RegisterClipboardFormat("Clipboard Viewer Ignore");
    private readonly uint _formatPng = RegisterClipboardFormat("PNG");
    private readonly uint _formatDropEffect = RegisterClipboardFormat("Preferred DropEffect");

    private Thread? _thread;
    private uint _threadId;
    private WndProc? _proc;   // keep a reference so the GC doesn't collect it while native code calls it
    private uint _lastSequence;

    public ClipboardWatcher(Func<Core.Localization.LanguageStrings> strings)
        : base(strings)
    {
    }

    public override void Start() => Guard(() =>
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Loop) { IsBackground = true, Name = "IsleBar clipboard" };
        _thread.Start();
    });

    protected override void Stop()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        _proc = WindowProc;
        var className = "IsleBarClipboardWatch_" + Environment.ProcessId;
        var hInstance = GetModuleHandle(null);
        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInstance,
            lpszClassName = className,
        };

        if (RegisterClassEx(ref wc) == 0)
        {
            return;
        }

        var hwnd = CreateWindowEx(0, className, string.Empty, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            UnregisterClass(className, hInstance);
            return;
        }

        try
        {
            _lastSequence = GetClipboardSequenceNumber();   // don't notify what was already there at startup
            if (!AddClipboardFormatListener(hwnd) || IsDisposed)
            {
                return;
            }

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            RemoveClipboardFormatListener(hwnd);
            DestroyWindow(hwnd);
            UnregisterClass(className, hInstance);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam)
    {
        if (msg == WmClipboardUpdate)
        {
            SetTimer(hwnd, TimerId, CoalesceMs, IntPtr.Zero);   // even if it fires several times, only the last one counts
            return IntPtr.Zero;
        }

        if (msg == WmTimer && wParam == TimerId)
        {
            KillTimer(hwnd, TimerId);
            Guard(() => ReadAndAnnounce(hwnd));
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ReadAndAnnounce(IntPtr hwnd)
    {
        var sequence = GetClipboardSequenceNumber();
        if (sequence == _lastSequence)
        {
            return;
        }

        _lastSequence = sequence;
        var owner = OwnerProcessName();
        if (Read(hwnd, owner) is not { } notice)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var card = SystemNotice.Make(notice.Title, notice.Glyph, now, open: notice.Link);
        // the lifted card (TODO ⑤ 掀起卡片适配, user 10-11): "剪贴板", "已复制 / 已剪切" on the right, what it is, a grey line
        (card.AppName, card.Words, card.Subject, card.Note) = (Text.CardClipboard, notice.Words, notice.Subject ?? string.Empty, notice.Note);
        Board.Flash(Key, card, ClipboardRules.ShowFor(notice.Long), now);
    }

    /// <summary>The longest copied text the lifted card shows (a few lines; the pill shows far less).</summary>
    private const int LiftedText = 280;

    /// <summary>
    /// What a copy card says: its line and icon, the address its ↗ opens (<see cref="ClipboardRules.LinkOf"/>), and whether it stays
    /// the longer time (an address, files, an image — TODO ④, user 10-10).
    /// </summary>
    private (string Title, string Glyph, string? Link, bool Long, string Words, string? Subject, string? Note)? Read(IntPtr hwnd, string? owner)
    {
        // If another program holds the clipboard, wait briefly and retry (this thread is ours, so waiting is fine)
        var opened = false;
        for (var i = 0; i < 5 && !opened; i++)
        {
            opened = OpenClipboard(hwnd);
            if (!opened)
            {
                Thread.Sleep(30);
            }
        }

        if (!opened)
        {
            return null;
        }

        try
        {
            var s = Text;
            var copied = NoticeText.Head(NoticeText.Copied(s, "-"));   // "已复制"
            _files = [];
            _imageSequence = 0;
            if (IsFlaggedSecret() || ClipboardRules.IsPasswordManager(owner))
            {
                return (NoticeText.Copied(s, NoticeText.Masked), NoticeGlyphs.Copy, null, false, copied, NoticeText.Masked, null);
            }

            if (IsClipboardFormatAvailable(CfHdrop) && GetClipboardData(CfHdrop) is var drop && drop != IntPtr.Zero)
            {
                var count = (int)DragQueryFile(drop, 0xFFFF_FFFF, null, 0);
                _files = FilesOf(drop, count);
                var cut = IsCut();
                var first = _files.FirstOrDefault();
                // the first file's name ("+2" for the rest), its folder grey under it
                return count > 0
                    ? (NoticeText.CopiedFiles(s, count, cut), NoticeGlyphs.Files, null, true, cut ? NoticeText.Head(NoticeText.CopiedFiles(s, 2, cut)) : copied,
                        first is null ? null : Path.GetFileName(first.TrimEnd('\\')) + (count > 1 ? $" +{count - 1}" : string.Empty), first is null ? null : Path.GetDirectoryName(first))
                    : null;
            }

            if (IsClipboardFormatAvailable(CfUnicodeText) && ReadText() is { } text)
            {
                // only spaces / punctuation: "Copied" alone (user 10-07); the lifted card shows a few lines of it, grey
                var link = ClipboardRules.LinkOf(text);
                return string.IsNullOrEmpty(text) ? null
                    : (NoticeText.Copied(s, ClipboardRules.Preview(text)), NoticeGlyphs.Copy, link, link is not null, copied, null,
                        link ?? ClipboardRules.Preview(text, max: LiftedText));
            }

            if (IsClipboardFormatAvailable(CfDib) || IsClipboardFormatAvailable(CfDibV5)
                || IsClipboardFormatAvailable(CfBitmap) || (_formatPng != 0 && IsClipboardFormatAvailable(_formatPng)))
            {
                _imageSequence = GetClipboardSequenceNumber();
                return (s.NoticeCopiedImage, NoticeGlyphs.Image, null, true, copied, s.CardImage, null);
            }

            return null;   // other formats (app-private data) aren't notified
        }
        finally
        {
            CloseClipboard();
        }
    }

    // ---- the copy card's "加入中转站" (TODO ④, user 10-10) ----

    private volatile string[] _files = [];
    private volatile uint _imageSequence;

    /// <summary>The files of the last copy announced (none when it was text or an image).</summary>
    public IReadOnlyList<string> CopiedFiles => _files;

    /// <summary>The last copy announced was an image, and it's still what the clipboard holds.</summary>
    public bool HoldsImage => _imageSequence != 0 && _imageSequence == GetClipboardSequenceNumber();

    private static string[] FilesOf(IntPtr drop, int count)
    {
        var files = new List<string>();
        var name = new char[32768];
        for (var i = 0; i < count; i++)
        {
            var length = (int)DragQueryFile(drop, (uint)i, name, (uint)name.Length);
            if (length > 0)
            {
                files.Add(new string(name, 0, length));
            }
        }

        return [.. files];
    }

    /// <summary>
    /// The copied image saved as a PNG in <paramref name="folder"/> (a screenshot has no file to point at — TODO ④: saved first),
    /// named "<paramref name="name"/> 2026-10-11 030512.png". Null when the clipboard no longer holds it or it can't be read.
    /// </summary>
    public async Task<string?> SaveImageAsync(string folder, string name)
    {
        var bytes = await Task.Run(ReadImage);
        if (bytes is null)
        {
            return null;
        }

        using var source = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await source.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes.Value.Data));
        source.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(source);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            bytes.Value.Png ? Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied : Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);
        using var png = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, png);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{name} {DateTime.Now:yyyy-MM-dd HHmmss}.png");
        await using (var file = File.Create(path))
        {
            png.Seek(0);
            await png.AsStreamForRead().CopyToAsync(file);
        }

        return path;
    }

    /// <summary>The clipboard's image as a file's bytes: its PNG when it has one, else its DIB with a bitmap file header in front.</summary>
    private (byte[] Data, bool Png)? ReadImage()
    {
        if (!HoldsImage)
        {
            return null;
        }

        var opened = false;
        for (var i = 0; i < 5 && !opened; i++)
        {
            opened = OpenClipboard(IntPtr.Zero);
            if (!opened)
            {
                Thread.Sleep(30);
            }
        }

        if (!opened)
        {
            return null;
        }

        try
        {
            if (_formatPng != 0 && IsClipboardFormatAvailable(_formatPng) && Bytes(GetClipboardData(_formatPng)) is { Length: > 0 } png)
            {
                return (png, true);
            }

            if (!IsClipboardFormatAvailable(CfDib) || Bytes(GetClipboardData(CfDib)) is not { Length: >= 40 } dib)
            {
                return null;
            }

            // a .bmp file = 14-byte file header + the DIB; the pixels start after the info header, its colour masks and its palette
            var header = BitConverter.ToInt32(dib, 0);
            var bits = BitConverter.ToUInt16(dib, 14);
            var compression = BitConverter.ToUInt32(dib, 16);
            var used = BitConverter.ToUInt32(dib, 32);
            var masks = header == 40 && compression == 3 ? 12 : 0;   // BI_BITFIELDS after a plain BITMAPINFOHEADER
            var palette = used > 0 ? (int)used * 4 : bits <= 8 ? (1 << bits) * 4 : 0;
            var file = new byte[14 + dib.Length];
            file[0] = (byte)'B';
            file[1] = (byte)'M';
            BitConverter.GetBytes(file.Length).CopyTo(file, 2);
            BitConverter.GetBytes(14 + header + masks + palette).CopyTo(file, 10);
            dib.CopyTo(file, 14);
            return (file, false);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static byte[]? Bytes(IntPtr handle)
    {
        var ptr = handle == IntPtr.Zero ? IntPtr.Zero : GlobalLock(handle);
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var data = new byte[(int)GlobalSize(handle)];
            Marshal.Copy(ptr, data, 0, data.Length);
            return data;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    /// <summary>
    /// Whether the files were cut (Ctrl+X), not copied: File Explorer writes it down in "Preferred DropEffect" — move (2) without
    /// copy (1). Copy writes 5 (copy + link); no mark (another program) counts as a copy. TODO ④, 10-11.
    /// </summary>
    private bool IsCut()
    {
        if (_formatDropEffect == 0 || !IsClipboardFormatAvailable(_formatDropEffect) || Bytes(GetClipboardData(_formatDropEffect)) is not { Length: >= 4 } data)
        {
            return false;
        }

        var effect = BitConverter.ToUInt32(data, 0);
        return (effect & 2) != 0 && (effect & 1) == 0;
    }

    /// <summary>Whether the "don't record" mark added by password managers is present.</summary>
    private bool IsFlaggedSecret()
    {
        if ((_formatExclude != 0 && IsClipboardFormatAvailable(_formatExclude))
            || (_formatViewerIgnore != 0 && IsClipboardFormatAvailable(_formatViewerIgnore)))
        {
            return true;
        }

        // CanIncludeInClipboardHistory = DWORD 0 means "don't record"
        if (_formatHistory != 0 && IsClipboardFormatAvailable(_formatHistory))
        {
            var handle = GetClipboardData(_formatHistory);
            var ptr = handle == IntPtr.Zero ? IntPtr.Zero : GlobalLock(handle);
            if (ptr != IntPtr.Zero)
            {
                try
                {
                    return GlobalSize(handle) >= 4 && Marshal.ReadInt32(ptr) == 0;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
        }

        return false;
    }

    private static string? ReadText()
    {
        var handle = GetClipboardData(CfUnicodeText);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var chars = (int)Math.Min((long)GlobalSize(handle) / 2, MaxTextChars);
            var text = Marshal.PtrToStringUni(ptr, chars);
            var nul = text.IndexOf('\0', StringComparison.Ordinal);
            return nul >= 0 ? text[..nul] : text;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    /// <summary>Name of the program that last put data on the clipboard (for password-manager detection). Null if unknown.</summary>
    private static string? OwnerProcessName()
    {
        var owner = GetClipboardOwner();
        if (owner == IntPtr.Zero || GetWindowThreadProcessId(owner, out var pid) == 0 || pid == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    // ---- Win32 ----

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string className, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint elapseMs, IntPtr timerProc);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hwnd, UIntPtr id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr drop, uint index, char[]? file, uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
