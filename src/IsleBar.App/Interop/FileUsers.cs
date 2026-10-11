using System.Runtime.InteropServices;
using System.Text;

namespace IsleBar.App.Interop;

/// <summary>
/// Whether some program holds a file open — Windows' Restart Manager, the one installers ask "who's using this file". 迅雷 holds its
/// ".xltd" open while downloading and closes it when paused (10-11: its write time moved only at the pause), and keeps the whole
/// file reserved, so from the outside this is how a running download differs from a stopped one. A few milliseconds a file.
/// </summary>
internal static class FileUsers
{
    private const int MoreData = 234;   // ERROR_MORE_DATA: some processes hold it

    /// <summary>True when a process holds it open; false when none does or it can't be told.</summary>
    public static bool IsOpen(string path)
    {
        var key = new StringBuilder(64);   // CCH_RM_SESSION_KEY + 1
        if (RmStartSession(out var session, 0, key) != 0)
        {
            return false;
        }

        try
        {
            if (RmRegisterResources(session, 1, [path], 0, IntPtr.Zero, 0, null) != 0)
            {
                return false;
            }

            uint count = 0;
            uint reasons = 0;
            var result = RmGetList(session, out var needed, ref count, IntPtr.Zero, ref reasons);
            return result == MoreData || (result == 0 && needed > 0);
        }
        finally
        {
            RmEndSession(session);
        }
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, int flags, StringBuilder key);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint files, string[] names, uint applications, IntPtr apps, uint services, string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, IntPtr info, ref uint reasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);
}
