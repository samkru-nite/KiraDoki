using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace IsleBar.App.Search;

/// <summary>
/// The icons Explorer shows for files, fetched on one background thread so the results list never waits for them.
/// Ordinary files are looked up <b>by extension only</b> (no disk access — every .json shares one icon); apps, shortcuts and
/// icon files have their own picture, so those are read from the file. The first version asked Windows' storage API for a
/// thumbnail of every row, which made typing lag 0.5–1 s (user 10-04).
/// </summary>
internal sealed class ShellIcons : IDisposable
{
    private static readonly HashSet<string> OwnIconTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".url", ".ico", ".appref-ms", ".msc", ".cpl", ".scr",
    };

    private readonly BlockingCollection<(string Key, string Path, bool Folder, Action<string, Icon?> Done)> _requests = new();
    private readonly Thread _thread;

    public ShellIcons()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "IsleBar icons" };
        _thread.SetApartmentState(ApartmentState.STA);   // the shell's icon lookup wants COM on its thread
        _thread.Start();
    }

    /// <summary>Top-down, premultiplied BGRA pixels of one icon.</summary>
    internal sealed record Icon(int Width, int Height, byte[] Pixels);

    /// <summary>Cache key: the extension for ordinary files, the path for files with their own icon, one key for all folders.</summary>
    public static string KeyFor(FileHit hit)
    {
        if (hit.IsFolder)
        {
            return "<folder>";
        }

        var extension = Path.GetExtension(hit.Name);
        return extension.Length == 0 || OwnIconTypes.Contains(extension) ? hit.FullPath : extension.ToLowerInvariant();
    }

    /// <summary>Queues a lookup; <paramref name="done"/> runs on the background thread (null when the shell had none).</summary>
    public void Request(FileHit hit, Action<string, Icon?> done)
    {
        var key = KeyFor(hit);
        if (!_requests.IsAddingCompleted)
        {
            _requests.Add((key, hit.FullPath, hit.IsFolder, done));
        }
    }

    private void Loop()
    {
        foreach (var (key, path, folder, done) in _requests.GetConsumingEnumerable())
        {
            Icon? icon = null;
            try
            {
                icon = Read(key, path, folder);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // an odd file — the list keeps its plain glyph
            }

            done(key, icon);
        }
    }

    private static Icon? Read(string key, string path, bool folder)
    {
        var byType = key != path;   // extension or folder key: ask by type, no disk access
        var flags = SHGFI_ICON | SHGFI_LARGEICON | (byType ? SHGFI_USEFILEATTRIBUTES : 0);
        var name = byType ? (folder ? "folder" : "file" + key) : path;
        var info = new SHFILEINFOW();
        if (SHGetFileInfoW(name, folder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), flags) == IntPtr.Zero
            || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Pixels(info.hIcon);
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    /// <summary>Icon → premultiplied BGRA. Old icons without alpha get it from their mask.</summary>
    private static Icon? Pixels(IntPtr icon)
    {
        if (!GetIconInfo(icon, out var info))
        {
            return null;
        }

        try
        {
            if (GetObjectW(info.hbmColor, Marshal.SizeOf<BITMAP>(), out var bitmap) == 0 || bitmap.bmWidth <= 0 || bitmap.bmHeight <= 0)
            {
                return null;
            }

            int w = bitmap.bmWidth, h = bitmap.bmHeight;
            var color = ReadBits(info.hbmColor, w, h);
            if (color is null)
            {
                return null;
            }

            var hasAlpha = false;
            for (var i = 3; i < color.Length; i += 4)
            {
                if (color[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            var mask = hasAlpha ? null : ReadBits(info.hbmMask, w, h);
            for (var i = 0; i < color.Length; i += 4)
            {
                var a = hasAlpha ? color[i + 3] : (byte)(mask is not null && mask[i] != 0 ? 0 : 255);   // mask white = transparent
                color[i + 0] = (byte)(color[i + 0] * a / 255);
                color[i + 1] = (byte)(color[i + 1] * a / 255);
                color[i + 2] = (byte)(color[i + 2] * a / 255);
                color[i + 3] = a;
            }

            return new Icon(w, h, color);
        }
        finally
        {
            DeleteObject(info.hbmColor);
            DeleteObject(info.hbmMask);
        }
    }

    private static byte[]? ReadBits(IntPtr bitmap, int w, int h)
    {
        var header = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var pixels = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(dc, bitmap, 0, (uint)h, pixels, ref header, 0) == h ? pixels : null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    public void Dispose()
    {
        _requests.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(1));
        _requests.Dispose();
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
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
        public int biSize;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string path, uint attributes, ref SHFILEINFOW info, uint size, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [DllImport("gdi32.dll")]
    private static extern int GetObjectW(IntPtr handle, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
