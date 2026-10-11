using System.Runtime.InteropServices;
using IsleBar.App.Interop;

namespace IsleBar.App.Media;

/// <summary>
/// Everything SDK wrapper — used to find an archive being written (<see cref="FileOperationWatcher"/>), an editor's export
/// (<see cref="VideoExportWatcher"/>) and downloads saved anywhere (<see cref="DownloadWatcher"/>); file search was removed 10-10. The SDK keeps one global query state, so calls go through <see cref="Gate"/>. Do not call it from the UI
/// thread (a query waits for Everything's reply).
/// </summary>
internal sealed class EverythingSdk : IDisposable
{
    private readonly IntPtr _library;

    /// <summary>Serializes every query across instances and threads (the SDK's state is global to the process).</summary>
    private static readonly object Gate = new();

    private EverythingSdk(IntPtr library) => _library = library;

    /// <summary>Loads the DLL. Null if missing.</summary>
    public static EverythingSdk? TryLoad()
    {
        var path = AppPaths.EverythingDll;
        return NativeLibrary.TryLoad(path, out var handle) ? new EverythingSdk(handle) : null;
    }

    /// <summary>
    /// Full paths of files matching <paramref name="query"/> modified within <paramref name="within"/>, newest first. Empty when
    /// Everything isn't running. Everything's size for a file still being written lags behind — read the file itself for that.
    /// </summary>
    public IReadOnlyList<string> RecentlyChanged(string query, TimeSpan within, int max)
    {
        lock (Gate)
        {
            Everything_SetSearchW(query);
            Everything_SetOffset(0);
            Everything_SetMax((uint)max);
            Everything_SetSort(SortDateModifiedDescending);
            Everything_SetRequestFlags(RequestFileName | RequestPath | RequestDateModified);
            if (!Everything_QueryW(wait: true))
            {
                return [];
            }

            var since = DateTime.UtcNow - within;
            var paths = new List<string>();
            var count = Everything_GetNumResults();
            for (var i = 0u; i < count; i++)
            {
                if (!Everything_GetResultDateModified(i, out var fileTime) || fileTime <= 0 || fileTime == long.MaxValue
                    || DateTime.FromFileTimeUtc(fileTime) < since)
                {
                    break;   // newest first: the rest are older
                }

                var name = Marshal.PtrToStringUni(Everything_GetResultFileNameW(i));
                var dir = Marshal.PtrToStringUni(Everything_GetResultPathW(i));
                if (name is not null && dir is not null)
                {
                    paths.Add(Path.Combine(dir, name));
                }
            }

            return paths;
        }
    }

    /// <summary>Full paths of files matching <paramref name="query"/>, however old (a downloader's temporary files, 10-11). Empty when
    /// Everything isn't running.</summary>
    public IReadOnlyList<string> Find(string query, int max)
    {
        lock (Gate)
        {
            Everything_SetSearchW(query);
            Everything_SetOffset(0);
            Everything_SetMax((uint)max);
            Everything_SetSort(SortDateModifiedDescending);
            Everything_SetRequestFlags(RequestFileName | RequestPath);
            if (!Everything_QueryW(wait: true))
            {
                return [];
            }

            var paths = new List<string>();
            var count = Everything_GetNumResults();
            for (var i = 0u; i < count; i++)
            {
                var name = Marshal.PtrToStringUni(Everything_GetResultFileNameW(i));
                var dir = Marshal.PtrToStringUni(Everything_GetResultPathW(i));
                if (name is not null && dir is not null)
                {
                    paths.Add(Path.Combine(dir, name));
                }
            }

            return paths;
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            Everything_Reset();
            if (_library != IntPtr.Zero)
            {
                NativeLibrary.Free(_library);
            }
        }
    }

    private const uint RequestFileName = 0x0000_0001;
    private const uint RequestPath = 0x0000_0002;
    private const uint RequestDateModified = 0x0000_0040;
    private const uint SortDateModifiedDescending = 14;

    /// <summary>FILETIME as one 64-bit number (same layout).</summary>
    [DllImport("Everything64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_GetResultDateModified(uint index, out long fileTime);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern void Everything_SetSearchW(string search);

    [DllImport("Everything64.dll")]
    private static extern void Everything_SetMax(uint max);

    [DllImport("Everything64.dll")]
    private static extern void Everything_SetOffset(uint offset);

    [DllImport("Everything64.dll")]
    private static extern void Everything_SetSort(uint sort);

    [DllImport("Everything64.dll")]
    private static extern void Everything_SetRequestFlags(uint flags);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetNumResults();

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr Everything_GetResultFileNameW(uint index);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr Everything_GetResultPathW(uint index);

    [DllImport("Everything64.dll")]
    private static extern void Everything_Reset();
}
