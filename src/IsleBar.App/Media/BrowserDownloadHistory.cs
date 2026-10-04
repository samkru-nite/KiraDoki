using IsleBar.App.Interop;

namespace IsleBar.App.Media;

/// <summary>
/// The total size of downloads under way, from the browser's own download list — the partial file only says how much has
/// arrived. Edge and Chrome keep that list in the profile's <c>History</c> database (<c>downloads</c> table: <c>current_path</c> is
/// the .crdownload file, <c>target_path</c> the final name, <c>total_bytes</c> 0 when the server doesn't say, <c>state</c> 0 while
/// in progress — paused included).
/// <para>
/// The browser holds that database locked while it runs: an ordinary read-only open fails ("database is locked", measured
/// 10-04 with Edge open), so it's opened <c>immutable</c> — no locks taken, nothing written. A read that lands while the
/// browser is writing can fail or miss a fresh row; the caller just asks again a few seconds later.
/// </para>
/// </summary>
internal static class BrowserDownloadHistory
{
    /// <summary>A download in progress: the partial file, the final path, and the total (0 = unknown).</summary>
    public sealed record Row(string CurrentPath, string TargetPath, long Total);

    private static readonly string[] Browsers =
    [
        @"Microsoft\Edge\User Data",
        @"Google\Chrome\User Data",
    ];

    /// <summary>Downloads in progress in every Edge/Chrome profile. Empty when none can be read.</summary>
    public static List<Row> InProgress()
    {
        var rows = new List<Row>();
        foreach (var database in Databases())
        {
            try
            {
                Read(database, rows);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>"Default" and "Profile N" folders that have a History database.</summary>
    private static IEnumerable<string> Databases()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var browser in Browsers)
        {
            var root = Path.Combine(local, browser);
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> profiles;
            try
            {
                profiles = Directory.EnumerateDirectories(root).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var profile in profiles)
            {
                var name = Path.GetFileName(profile);
                var file = Path.Combine(profile, "History");
                if ((name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal)) && File.Exists(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static void Read(string database, List<Row> rows)
    {
        var uri = new Uri(database).AbsoluteUri + "?immutable=1";
        if (WinSqlite.sqlite3_open_v2(uri, out var db, WinSqlite.OpenReadOnly | WinSqlite.OpenUri, IntPtr.Zero) != 0)
        {
            WinSqlite.sqlite3_close(db);
            return;
        }

        try
        {
            if (WinSqlite.sqlite3_prepare16_v2(db, "SELECT current_path, target_path, total_bytes FROM downloads WHERE state = 0", -1, out var statement, IntPtr.Zero) != 0)
            {
                return;
            }

            try
            {
                while (WinSqlite.sqlite3_step(statement) == WinSqlite.Row)
                {
                    rows.Add(new Row(WinSqlite.Text(statement, 0), WinSqlite.Text(statement, 1), WinSqlite.sqlite3_column_int64(statement, 2)));
                }
            }
            finally
            {
                WinSqlite.sqlite3_finalize(statement);
            }
        }
        finally
        {
            WinSqlite.sqlite3_close(db);
        }
    }
}
