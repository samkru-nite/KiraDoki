using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace IsleBar.App.Interop;

/// <summary>
/// The real icon of an app, from its AUMID — so the expanded card can show, say, WhatsApp's own icon instead of a generic
/// bubble (user 09-30). The icon is the vendor's own (asked for through the shell), never one we draw.
/// <para>
/// The shell hands the icon back as a GDI bitmap (HBITMAP); we copy its pixels into a WinRT <see cref="SoftwareBitmap"/> and
/// wrap it as an image source, with no temp files. Results are cached per AUMID on the UI thread. Any failure returns null and
/// the caller falls back to the glyph.
/// </para>
/// </summary>
internal static class AppIcon
{
    private const int Size = 48;           // asked-for icon size in pixels
    private const int SIIGBF_ICONONLY = 0x4;
    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    // the pixels, not an image source: one SoftwareBitmapSource shown at once in the lifted card's window and in the bar's (the icon
    // flying along) brought the whole app down in Microsoft.UI.Xaml.dll (0xc000027b) on the second lift of a song (10-09) — each
    // caller gets a source of its own
    private static readonly Dictionary<string, SoftwareBitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Calls <paramref name="apply"/> with the app's icon once it is ready — a new image source each time, never shared. Never
    /// throws; if the icon can't be had, it simply never calls back and the glyph the caller already showed stays.
    /// </summary>
    public static void Resolve(string? aumid, Action<ImageSource> apply)
    {
        if (!string.IsNullOrWhiteSpace(aumid))
        {
            Resolve(aumid, () => AppNames.StartMenuId(aumid) is { } id ? @"shell:AppsFolder\" + id : AppNames.ExePath(aumid), apply);
        }
    }

    /// <summary>
    /// The app's icon at once, or null if it has none (10-11: through <see cref="Resolve(string?, Action{ImageSource})"/> the bell
    /// showed for a frame before the app's icon, every time — WeChat's flashed in). A <see cref="WriteableBitmap"/> of its own each
    /// call (never shared between windows, see <see cref="Cache"/>), its pixels written right here: nothing waits for a later frame.
    /// </summary>
    public static ImageSource? Now(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return null;
        }

        try
        {
            var key = aumid;
            if (!Cache.TryGetValue(key, out var bitmap))
            {
                bitmap = LoadSoftwareBitmap(AppNames.StartMenuId(aumid) is { } id ? @"shell:AppsFolder\" + id : AppNames.ExePath(aumid));
                Cache[key] = bitmap;
            }

            if (bitmap is null)
            {
                return null;
            }

            var image = new WriteableBitmap(bitmap.PixelWidth, bitmap.PixelHeight);
            bitmap.CopyToBuffer(image.PixelBuffer);   // premultiplied BGRA, as WriteableBitmap takes it
            image.Invalidate();
            return image;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidCastException or OutOfMemoryException or ObjectDisposedException)
        {
            TaskbarHost.Log($"app icon: {aumid} failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// A file's or folder's own icon, as File Explorer shows it (a .docx = Word's) — the transit station's list (10-10). Same rules
    /// as <see cref="Resolve(string?, Action{ImageSource})"/>: called back only when there is one.
    /// </summary>
    public static void ResolveFile(string path, Action<ImageSource> apply) => Resolve("file|" + path, () => path, apply);

    private static async void Resolve(string key, Func<string?> target, Action<ImageSource> apply)
    {
        try
        {
            if (!Cache.TryGetValue(key, out var bitmap))
            {
                bitmap = LoadSoftwareBitmap(target());
                Cache[key] = bitmap;
            }

            if (bitmap is null)
            {
                return;
            }

            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);
            apply(source);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidCastException or OutOfMemoryException or ObjectDisposedException)
        {
            TaskbarHost.Log($"app icon: {(key.StartsWith("file|", StringComparison.Ordinal) ? "a file" : key)} failed — {ex.Message}");
        }
    }

    /// <param name="target">An app (<c>shell:AppsFolder\…</c>; a desktop player that reports just its exe ("cloudmusic.exe") isn't in
    /// the Start menu under that id: its file's icon, 10-09) or a file's path.</param>
    private static SoftwareBitmap? LoadSoftwareBitmap(string? target)
    {
        var factoryIid = typeof(IShellItemImageFactory).GUID;
        if (target is null || SHCreateItemFromParsingName(target, IntPtr.Zero, ref factoryIid, out var factoryObject) != 0 || factoryObject is null)
        {
            return null;
        }

        var factory = (IShellItemImageFactory)factoryObject;
        var hbitmap = IntPtr.Zero;
        try
        {
            var size = new SIZE { cx = Size, cy = Size };
            if (factory.GetImage(size, SIIGBF_ICONONLY, out hbitmap) != 0 || hbitmap == IntPtr.Zero)
            {
                return null;
            }

            return FromHBitmap(hbitmap);
        }
        finally
        {
            if (hbitmap != IntPtr.Zero)
            {
                DeleteObject(hbitmap);
            }

            Marshal.ReleaseComObject(factory);
        }
    }

    private static SoftwareBitmap? FromHBitmap(IntPtr hbitmap)
    {
        var bmp = default(BITMAP);
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), ref bmp) == 0 || bmp.bmWidth <= 0 || bmp.bmHeight <= 0)
        {
            return null;
        }

        var w = bmp.bmWidth;
        var h = bmp.bmHeight;
        var pixels = new byte[w * h * 4];
        var info = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,   // negative = top-down, so rows are in the order WinRT expects
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };

        var hdc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(hdc, hbitmap, 0, (uint)h, pixels, ref info, DIB_RGB_COLORS) == 0)
            {
                return null;
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        // The shell returns 32bpp with premultiplied alpha; GetDIBits gives BGRA byte order, which matches Bgra8.
        return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? item);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int count, ref BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr hdc);
}
