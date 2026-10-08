using System.Diagnostics;
using System.Runtime.InteropServices;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.Interop;

/// <summary>
/// An app's name from its AUMID (the id Windows gives an app: notifications and media sessions carry it). The Start menu's name
/// first (<c>shell:AppsFolder\{AUMID}</c>); a desktop player often reports just its exe ("cloudmusic.exe") — then the running
/// program's own description ("网易云音乐"); else a guess from the id. Cached; safe from any thread.
/// </summary>
internal static class AppNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static string For(string appId)
    {
        lock (Gate)
        {
            if (Names.TryGetValue(appId, out var cached))
            {
                return cached;
            }
        }

        var name = (StartMenuId(appId) is { } id ? ShellName(id) : null) ?? ExeDescription(appId) ?? ToastText.GuessAppName(appId);
        lock (Gate)
        {
            Names[appId] = name;
        }

        TaskbarHost.Log($"app name: {appId} → {name}");
        return name;
    }

    /// <summary>Whether the Start menu knows the app (then <c>shell:AppsFolder\{AUMID}</c> opens it and gives its icon).</summary>
    public static bool InStartMenu(string appId)
    {
        try
        {
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(@"shell:AppsFolder\" + appId, IntPtr.Zero, ref iid, out var item) != 0 || item is null)
            {
                return false;
            }

            Marshal.ReleaseComObject(item);
            return true;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>
    /// The id the Start menu knows the app by: the id itself, or for a program reported by its exe the Start menu's own form —
    /// the known folder's GUID + the rest of the path (<c>{6D809377-…}\NetEase\CloudMusic\cloudmusic.exe</c>, checked 10-09 with
    /// Get-StartApps: that is how it gives "网易云音乐" while the exe only says "NetEase Cloud Music"). Null if it knows neither.
    /// </summary>
    public static string? StartMenuId(string appId)
    {
        if (InStartMenu(appId))
        {
            return appId;
        }

        if (ExePath(appId) is not { } path)
        {
            return null;
        }

        foreach (var (folder, guid) in KnownFolders)
        {
            var root = Environment.GetFolderPath(folder).TrimEnd('\\');
            if (root.Length > 0 && path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) && InStartMenu(guid + path[root.Length..]))
            {
                return guid + path[root.Length..];
            }
        }

        return null;
    }

    private static readonly (Environment.SpecialFolder Folder, string Guid)[] KnownFolders =
    [
        (Environment.SpecialFolder.ProgramFiles, "{6D809377-6AF0-444B-8957-A3773F02200E}"),
        (Environment.SpecialFolder.ProgramFilesX86, "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"),
        (Environment.SpecialFolder.System, "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"),
        (Environment.SpecialFolder.Windows, "{F38BF404-1D43-42F2-9305-67DE0B28FC23}"),
    ];

    /// <summary>An id that is an exe name ("Spotify.exe"): the running program's full path. Null if not running or not an exe.</summary>
    public static string? ExePath(string appId)
    {
        if (!appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || appId.IndexOfAny(['\\', '/']) >= 0)
        {
            return null;
        }

        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(appId)))
        {
            try
            {
                if (process.MainModule?.FileName is { } path)
                {
                    return path;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // elevated or gone — try the next one
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static string? ExeDescription(string appId)
    {
        if (ExePath(appId) is not { } path)
        {
            return null;
        }

        var info = FileVersionInfo.GetVersionInfo(path);
        var name = string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private static string? ShellName(string appId)
    {
        try
        {
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(@"shell:AppsFolder\" + appId, IntPtr.Zero, ref iid, out var item) != 0 || item is null)
            {
                return null;
            }

            try
            {
                item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out var pointer);
                var name = Marshal.PtrToStringUni(pointer);
                Marshal.FreeCoTaskMem(pointer);
                return string.IsNullOrWhiteSpace(name) || name.Contains('!', StringComparison.Ordinal) ? null : name;
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidCastException)
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint form, out IntPtr name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }
}
