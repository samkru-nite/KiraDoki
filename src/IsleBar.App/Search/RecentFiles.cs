using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace IsleBar.App.Search;

/// <summary>
/// Windows' own "recently opened" list — the shortcuts in <c>%APPDATA%\Microsoft\Windows\Recent</c> that Explorer's Home and the
/// Start menu show. Only used while the person keeps that list on (Settings › Personalization › Start › "Show recently opened
/// items"); with it off the folder stays empty and the file list falls back to files changed this week (user 10-04).
/// Call from an STA thread (shortcuts are read through the shell's COM object).
/// </summary>
internal static class RecentFiles
{
    /// <summary>Whether Windows records recently opened files (missing value = on, the Windows default).</summary>
    public static bool WindowsKeepsThem()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackDocs", 1) is not int on || on != 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>The most recently opened items that still exist, newest first, up to <paramref name="max"/>.</summary>
    public static List<FileHit> Read(int max, Func<string, bool> hidden)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        var hits = new List<FileHit>();
        if (!Directory.Exists(folder))
        {
            return hits;
        }

        var shortcuts = new DirectoryInfo(folder).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shortcut in shortcuts)
        {
            if (hits.Count >= max)
            {
                break;
            }

            if (Target(shortcut.FullName) is not { Length: > 0 } target || !seen.Add(target) || hidden(target))
            {
                continue;
            }

            if (File.Exists(target))
            {
                var info = new FileInfo(target);
                hits.Add(new FileHit(info.Name, info.DirectoryName ?? string.Empty, false, info.Length, info.LastWriteTime));
            }
            else if (Directory.Exists(target) && Path.GetDirectoryName(target) is { } parent)
            {
                hits.Add(new FileHit(Path.GetFileName(target), parent, true, null, Directory.GetLastWriteTime(target)));
            }
        }

        return hits;
    }

    /// <summary>Where a shortcut points (null if it can't be read).</summary>
    private static string? Target(string shortcutPath)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);
            var buffer = new char[1024];
            ((IShellLinkW)link).GetPath(buffer, buffer.Length, IntPtr.Zero, 0);
            var path = new string(buffer).TrimEnd('\0');
            return path.Length > 0 ? path : null;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] file, int size, IntPtr findData, uint flags);
    }
}
