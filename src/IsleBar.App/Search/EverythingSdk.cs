using System.Runtime.InteropServices;
using IsleBar.App.Interop;

namespace IsleBar.App.Search;

/// <summary>One file search result row.</summary>
/// <param name="Name">File or folder name.</param>
/// <param name="Directory">Containing folder.</param>
/// <param name="IsFolder">Whether it is a folder.</param>
/// <param name="Size">Bytes; null for folders or when Everything doesn't know it.</param>
/// <param name="Modified">Last change (local time); null when unknown.</param>
internal sealed record FileHit(string Name, string Directory, bool IsFolder, long? Size = null, DateTime? Modified = null)
{
    public string FullPath => Path.Combine(Directory, Name);
}

/// <summary>Why search is unavailable (the message text is picked from the language file).</summary>
internal enum SearchProblem
{
    None,

    /// <summary>Everything64.dll is missing → <c>NoSdk</c>.</summary>
    SdkMissing,

    /// <summary>Everything is not running → <c>EsOff</c>.</summary>
    ServiceOff,

    /// <summary>Any other failure → <c>EsFail</c>.</summary>
    Failed,
}

/// <summary>
/// Everything SDK wrapper.
///
/// <b>The SDK must be called from a single thread only.</b> So <see cref="FileSearchWorker"/>
/// owns this class exclusively on a dedicated thread. Do not call it directly from the UI thread.
///
/// <b>This file has never been built. Needs phase-0 verification on PC.</b>
/// </summary>
internal sealed class EverythingSdk : IDisposable
{
    /// <summary>Rows asked of Everything per round before hidden folders are dropped here (2000 rows take ~25 ms).</summary>
    private const int Page = 1000;

    /// <summary>Rounds at most — a search whose first thousands are all hidden folders stops here rather than lag.</summary>
    private const int MaxPages = 5;

    private const int EverythingErrorIpc = 2;   // Everything is not running

    private readonly IntPtr _library;

    private EverythingSdk(IntPtr library) => _library = library;

    /// <summary>Loads the DLL. Null if missing (file mode only shows a hint).</summary>
    public static EverythingSdk? TryLoad()
    {
        var path = AppPaths.EverythingDll;
        return NativeLibrary.TryLoad(path, out var handle) ? new EverythingSdk(handle) : null;
    }

    /// <summary>Searches. On failure <paramref name="problem"/> holds the reason and an empty list is returned.</summary>
    /// <param name="max">Rows to return (the settings' count).</param>
    /// <param name="sort">Sort setting value (<see cref="IsleBar.Core.Search.FileSearchQuery.Sorts"/>).</param>
    /// <param name="hidden">Folders to leave out (<see cref="IsleBar.Core.Search.FileExcludes"/>); ignored when the query names a path.</param>
    public IReadOnlyList<FileHit> Search(string query, int max, string sort, IReadOnlyList<IsleBar.Core.Search.FileExcludes.Rule> hidden, out SearchProblem problem)
    {
        problem = SearchProblem.None;
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Hidden folders are dropped here, page by page, not in the query: 32 "!path:" terms made Everything take ~820 ms a
        // search instead of ~60 (measured 10-04). Name order is what Everything's own window shows; the header switches it.
        max = IsleBar.Core.Search.FileSearchQuery.ClampCount(max);
        IReadOnlyList<IsleBar.Core.Search.FileExcludes.Rule> filter = IsleBar.Core.Search.FileSearchQuery.TargetsPath(query) ? [] : hidden;
        var hits = new List<FileHit>(max);
        for (var page = 0; page < MaxPages && hits.Count < max; page++)
        {
            Everything_SetSearchW(query.Trim());
            Everything_SetOffset((uint)(page * Page));
            Everything_SetMax(filter.Count == 0 ? (uint)max : Page);
            Everything_SetSort(IsleBar.Core.Search.FileSearchQuery.EverythingSort(sort));
            Everything_SetRequestFlags(RequestFileName | RequestPath | RequestSize | RequestDateModified);   // size + date: the list's columns (user 10-04)
            if (!Everything_QueryW(wait: true))
            {
                problem = Everything_GetLastError() == EverythingErrorIpc
                    ? SearchProblem.ServiceOff
                    : SearchProblem.Failed;
                return [];
            }

            var count = Everything_GetNumResults();
            for (var i = 0u; i < count && hits.Count < max; i++)
            {
                var name = Marshal.PtrToStringUni(Everything_GetResultFileNameW(i));
                var dir = Marshal.PtrToStringUni(Everything_GetResultPathW(i));
                if (name is null || dir is null || (filter.Count > 0 && IsleBar.Core.Search.FileExcludes.IsHidden(Path.Combine(dir, name), filter)))
                {
                    continue;
                }

                var folder = Everything_IsFolderResult(i);
                long? size = !folder && Everything_GetResultSize(i, out var bytes) && bytes >= 0 ? bytes : null;
                DateTime? modified = Everything_GetResultDateModified(i, out var fileTime) && fileTime > 0 && fileTime != long.MaxValue
                    ? DateTime.FromFileTimeUtc(fileTime).ToLocalTime()
                    : null;
                hits.Add(new FileHit(name, dir, folder, size, modified));
            }

            if (filter.Count == 0 || count < Page)
            {
                break;   // nothing filtered, or Everything has no more
            }
        }

        return hits;
    }

    public void Dispose()
    {
        Everything_Reset();
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
        }
    }

    private const uint RequestFileName = 0x0000_0001;
    private const uint RequestPath = 0x0000_0002;
    private const uint RequestSize = 0x0000_0010;
    private const uint RequestDateModified = 0x0000_0040;

    [DllImport("Everything64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_GetResultSize(uint index, out long size);

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_IsFolderResult(uint index);

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetLastError();

    [DllImport("Everything64.dll")]
    private static extern void Everything_Reset();
}
