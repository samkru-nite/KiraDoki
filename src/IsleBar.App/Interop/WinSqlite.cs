using System.Runtime.InteropServices;

namespace IsleBar.App.Interop;

/// <summary>The SQLite that ships with Windows (winsqlite3.dll) — nothing extra to install.</summary>
internal static class WinSqlite
{
    public const int OpenReadOnly = 0x00000001;
    public const int OpenUri = 0x00000040;   // the path is a file: URI (for "?immutable=1")
    public const int Row = 100;
    private const string Dll = "winsqlite3.dll";

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    public static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    public static extern int sqlite3_close(IntPtr db);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    public static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int bytes, out IntPtr statement, IntPtr tail);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    public static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    public static extern int sqlite3_step(IntPtr statement);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    public static extern int sqlite3_finalize(IntPtr statement);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    public static extern long sqlite3_column_int64(IntPtr statement, int column);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr sqlite3_column_text16(IntPtr statement, int column);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    private static extern int sqlite3_column_bytes(IntPtr statement, int column);

    public static string Text(IntPtr statement, int column)
        => Marshal.PtrToStringUni(sqlite3_column_text16(statement, column)) ?? string.Empty;

    /// <summary>Notification content is stored as a BLOB (UTF-8 XML).</summary>
    public static string Blob(IntPtr statement, int column)
    {
        var pointer = sqlite3_column_blob(statement, column);
        var length = sqlite3_column_bytes(statement, column);
        if (pointer == IntPtr.Zero || length <= 0)
        {
            return string.Empty;
        }

        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
