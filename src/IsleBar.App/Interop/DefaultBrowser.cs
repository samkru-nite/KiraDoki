using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace IsleBar.App.Interop;

/// <summary>
/// The default browser's name, for the copied address's ↗ hint "Open in Microsoft Edge" (TODO ④, user 10-10): the https handler the
/// user chose (<c>UrlAssociations\https\UserChoice</c> → ProgId, e.g. <c>MSEdgeHTM</c>) and that ProgId's <c>Application\ApplicationName</c>
/// ("Microsoft Edge"; a "@…" resource reference is resolved). Null when it can't be read — the hint then says "Browser".
/// Read again at most once a minute (the user may switch browsers).
/// </summary>
internal static class DefaultBrowser
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);
    private static (string? Name, DateTime At) _cache = (null, DateTime.MinValue);

    public static string? Name()
    {
        var now = DateTime.UtcNow;
        if (now - _cache.At < Fresh)
        {
            return _cache.Name;
        }

        var name = Read();
        _cache = (name, now);
        TaskbarHost.Log($"default browser: {name ?? "(unknown)"}");
        return name;
    }

    private static string? Read()
    {
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            if (choice?.GetValue("ProgId") is not string progId || string.IsNullOrWhiteSpace(progId))
            {
                return null;
            }

            using var app = Registry.ClassesRoot.OpenSubKey(progId + @"\Application");
            if (app?.GetValue("ApplicationName") is not string name || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (!name.StartsWith('@'))
            {
                return name.Trim();
            }

            var buffer = new StringBuilder(260);
            return SHLoadIndirectString(name, buffer, buffer.Capacity, IntPtr.Zero) == 0 && buffer.Length > 0 ? buffer.ToString().Trim() : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, int size, IntPtr reserved);
}
