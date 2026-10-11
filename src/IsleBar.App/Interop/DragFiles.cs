using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace IsleBar.App.Interop;

/// <summary>
/// Files handed to a drag out of the bar (the transit station, the copy card's list). WinUI's file objects never give a shortcut:
/// <c>StorageFile.GetFileFromPathAsync</c> says "access denied" for any ".lnk" and a folder's listing leaves them out (measured
/// 10-11 — the shortcuts in the transit station couldn't be dragged). So with a shortcut among them the files go as File Explorer's
/// own "Shell IDList Array" — each file's place in the shell, which anything taking files from Explorer reads; without one, as
/// WinUI's storage items as before.
/// </summary>
internal static class DragFiles
{
    private const string ShellIdList = "Shell IDList Array";

    /// <summary>What a drag carries: the files still there, as storage items or (with a shortcut) as Explorer's list.</summary>
    public sealed record Ready(IReadOnlyList<string> Paths, IReadOnlyList<IStorageItem> Items, IRandomAccessStream? IdList);

    public static async Task<Ready> PrepareAsync(IEnumerable<string> paths)
    {
        var there = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (there.Any(IsShortcut))
        {
            TaskbarHost.Log($"drag: {there.Count} item(s) with a shortcut — as Explorer's list");
            return new Ready(there, [], await IdListAsync(there));
        }

        var items = new List<IStorageItem>();
        foreach (var path in there)
        {
            items.Add(Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path));
        }

        return new Ready(there, items, null);
    }

    /// <summary>Puts the files into the drag's package.</summary>
    public static void Put(DataPackage data, Ready ready)
    {
        if (ready.IdList is { } list)
        {
            list.Seek(0);
            data.SetData(ShellIdList, list);
        }
        else
        {
            data.SetStorageItems(ready.Items);
        }
    }

    private static bool IsShortcut(string path) => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CIDA: how many, then where each ID list starts — the parent's first (the desktop: an empty list), then every file's full one —
    /// then the lists themselves.
    /// </summary>
    private static async Task<IRandomAccessStream> IdListAsync(List<string> paths)
    {
        var lists = new List<byte[]> { new byte[2] };   // the desktop: just the terminating 0
        foreach (var path in paths)
        {
            var pidl = ILCreateFromPathW(path);
            if (pidl == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                var bytes = new byte[ILGetSize(pidl)];
                Marshal.Copy(pidl, bytes, 0, bytes.Length);
                lists.Add(bytes);
            }
            finally
            {
                ILFree(pidl);
            }
        }

        var count = lists.Count - 1;
        var head = 4 * (count + 2);
        var data = new byte[head + lists.Sum(l => l.Length)];
        BitConverter.GetBytes((uint)count).CopyTo(data, 0);
        var at = head;
        for (var i = 0; i < lists.Count; i++)
        {
            BitConverter.GetBytes((uint)at).CopyTo(data, 4 * (i + 1));
            lists[i].CopyTo(data, at);
            at += lists[i].Length;
        }

        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(data);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        return stream;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPathW(string path);

    [DllImport("shell32.dll")]
    private static extern uint ILGetSize(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);
}
