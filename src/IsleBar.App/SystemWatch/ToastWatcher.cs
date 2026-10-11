using System.Runtime.InteropServices;
using IsleBar.Core.Localization;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// Pulls Windows notifications onto the island (user feedback 09-30: pull in Windows notifications). Notifications from any app, KakaoTalk, mail, etc.,
/// shown as "app name · content" for 6 s.
/// <para>
/// The official notification-reading API (UserNotificationListener) requires a signed package, so instead we read the database where Windows records
/// notifications in the user's profile (%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db)
/// <b>read-only</b> every second with the SQLite that ships with Windows (winsqlite3.dll) — only toasts with new IDs.
/// Nothing is written, so the notification center is unaffected. Since it reads all notification content, it must be enabled in settings (off by default).
/// </para>
/// </summary>
internal sealed class ToastWatcher(Func<LanguageStrings> strings) : NoticeWatcher(strings)
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(400);   // check often for low latency; the DB is only opened when its file actually changed
    private readonly CancellationTokenSource _stop = new();
    private long _lastId = -1;
    private DateTime _lastDbWrite = DateTime.MinValue;
    private Thread? _thread;

    /// <summary>Option "only on the bar": each time a notification lands, also silence the banners of apps that appeared since (set by <see cref="SystemWatchers"/>).</summary>
    public volatile bool HideBanners;

    /// <summary>Reads that failed in a row (the database changed but couldn't be read, or reading threw).</summary>
    private int _failures;

    /// <summary>Test switch (like the <c>slow</c> file): while <c>%LOCALAPPDATA%\IsleBar\toastfail</c> exists, every read fails.</summary>
    private static readonly string FailTestFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IsleBar", "toastfail");

    /// <summary>After this many failed reads in a row the bar can't show notifications: Windows' banners come back (TODO ② 10-10).</summary>
    private const int FailuresToGiveUp = 3;

    /// <summary>The notifications can be read (false after <see cref="FailuresToGiveUp"/> failures, true again on the next good read).</summary>
    public bool Healthy { get; private set; } = true;

    /// <summary>
    /// <see cref="Healthy"/> changed (on the watcher's thread): the bar gives Windows its banners back while it can't read the
    /// notifications — with "only on the bar" on, they'd be seen nowhere — and takes them again once it can.
    /// </summary>
    public Action<bool>? HealthChanged { get; set; }

    internal static string DatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Notifications", "wpndatabase.db");

    public override void Start()
    {
        if (_thread is not null || !File.Exists(DatabasePath))
        {
            return;
        }

        _thread = new Thread(Loop) { IsBackground = true, Name = "IsleBar toasts" };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_stop.Token.WaitHandle.WaitOne(Poll))
        {
            if (DatabaseChanged())   // a cheap stat gate — only open the SQLite file when a notification actually landed
            {
                var read = false;
                Guard(() => read = Check());
                Track(read);
                if (HideBanners && Healthy)
                {
                    ToastBanners.Hide();   // a new app's first notice pops up once; from then on it's silenced too
                }
            }
        }
    }

    /// <summary>True when the notification DB (or its write-ahead log) has been written since the last check.</summary>
    private bool DatabaseChanged()
    {
        var latest = DateTime.MinValue;
        foreach (var path in new[] { DatabasePath, DatabasePath + "-wal" })
        {
            try
            {
                var written = File.GetLastWriteTimeUtc(path);
                if (written > latest)
                {
                    latest = written;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (latest <= _lastDbWrite)
        {
            return false;
        }

        _lastDbWrite = latest;
        return true;
    }

    /// <summary>Counts failed reads; a few in a row → <see cref="Healthy"/> false, a good one → true again. Logged when it changes.</summary>
    private void Track(bool read)
    {
        if (read)
        {
            _failures = 0;
            if (!Healthy)
            {
                Healthy = true;
                Interop.TaskbarHost.Log("toasts: reading again" + (HideBanners ? " → Windows' banners hidden again" : string.Empty));
                HealthChanged?.Invoke(true);
            }

            return;
        }

        _failures++;
        Interop.TaskbarHost.Log($"toasts: couldn't read the notification database ({_failures} in a row)");
        if (Healthy && _failures >= FailuresToGiveUp)
        {
            Healthy = false;
            Interop.TaskbarHost.Log("toasts: giving up for now" + (HideBanners ? " → Windows' banners back" : string.Empty));
            HealthChanged?.Invoke(false);
        }
    }

    /// <summary>Reads what landed since last time onto the board. False = the database couldn't be read.</summary>
    private bool Check()
    {
        if (File.Exists(FailTestFile))
        {
            return false;   // test switch: reading "fails" while the file is there (to see the banners come back)
        }

        // null = the database couldn't be read just now (busy, mid-checkpoint): try again on the next change. Taking it as
        // "no notifications" on the first read set the start to 0, and the 20 oldest stored toasts then flashed by (review 10-03).
        if (Read(_lastId < 0 ? long.MaxValue : _lastId) is not { } rows)
        {
            return false;
        }

        if (_lastId < 0)
        {
            // At first, only remember the latest ID so far (so old notifications don't flood in right after enabling)
            _lastId = rows.Count > 0 ? rows.Max(r => r.Id) : 0;
            return true;
        }

        // Do Not Disturb (also on by itself while presenting or duplicating the screen) or presentation mode: Windows shows no
        // banner then, and neither does the bar — a private message used to appear on a shared screen (review 10-03).
        // The notifications stay in Windows' notification centre.
        var quiet = Interop.NativeMethods.DoNotDisturbOn()
                    || (Interop.NativeMethods.SHQueryUserNotificationState(out var state) == 0
                        && state is Interop.NativeMethods.QUNS_PRESENTATION_MODE or Interop.NativeMethods.QUNS_QUIET_TIME
                            or Interop.NativeMethods.QUNS_BUSY or Interop.NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN);   // a full-screen game or video: the card popped over it (review 10-03)
        foreach (var row in rows.OrderBy(r => r.Id))
        {
            _lastId = Math.Max(_lastId, row.Id);
            if (quiet)
            {
                continue;
            }

            var texts = ToastText.Texts(row.Payload);
            if (texts.Count == 0)
            {
                continue;
            }

            // each its own time, even two read in one go: the island takes each in once by its card and time (CardStack)
            var now = DateTimeOffset.UtcNow;
            now = _lastStamp is { } last && now <= last ? last.AddTicks(1) : now;
            _lastStamp = now;
            // QQ opens no link from outside (TODO 四, 10-11): its card has no "open" — the pill's ↗ brings QQ's window up instead
            var open = row.AppId == Interop.ChatWindows.QqApp ? null : ToastText.LaunchProtocol(row.Payload) ?? AppLaunch(row.AppId);
            var notice = Expandable(SystemNotice.Make(texts[0], NoticeGlyphs.Bell, now, msg: ToastText.Line([.. texts.Skip(1)]), open: open), AppName(row.AppId), row.AppId);
            // one card per app (user 10-10: its notifications in a row merge, "+N"); each kept on the board under its own id, so two
            // read in one go both reach the island
            notice.SourcePath = SystemNotice.SourcePrefix + "toast:" + row.AppId;
            Board.Flash(
                "toast:" + row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                // Content first: the notice's own title (sender, subject) in bold, then the body and the app name dim —
                // leading with the app name meant only "Windows PowerShell" showed before the notice was gone (user feedback 09-30)
                notice,
                NoticeLengths.ToRead(Core.Ui.TextGap.Plain(Core.Ui.TextGap.Join(notice.Title, notice.Msg, notice.Name))),   // as the pill writes it; until it has scrolled through once (6–10 s, user 10-10)
                now);
        }

        return true;
    }

    private DateTimeOffset? _lastStamp;

    private readonly record struct Row(long Id, string AppId, string Payload);

    /// <summary>Marks a message notice for the expanded card (the island grows it out of the pill); Name carries the app, AppId the AUMID (for the app icon).</summary>
    private static IsleBar.Core.Island.ActivityState Expandable(IsleBar.Core.Island.ActivityState notice, string app, string appId)
    {
        notice.Stage = "expand";
        notice.Name = app;
        notice.AppId = appId;
        return notice;
    }

    /// <summary>
    /// Toasts with IDs greater than <paramref name="after"/>. If after is long.MaxValue, only the last one (to learn the starting ID).
    /// Opens and closes every time — holding it open can block the notification service's WAL cleanup.
    /// </summary>
    private static List<Row>? Read(long after)
    {
        var rows = new List<Row>();
        if (Sqlite.sqlite3_open_v2(DatabasePath, out var db, Sqlite.OpenReadOnly, IntPtr.Zero) != 0)
        {
            Sqlite.sqlite3_close(db);
            return null;
        }

        try
        {
            var sql = after == long.MaxValue
                ? "select n.Id, h.PrimaryId, n.Payload from Notification n join NotificationHandler h on h.RecordId = n.HandlerId order by n.Id desc limit 1"
                : "select n.Id, h.PrimaryId, n.Payload from Notification n join NotificationHandler h on h.RecordId = n.HandlerId where n.Type = 'toast' and n.Id > ?1 order by n.Id limit 20";
            if (Sqlite.sqlite3_prepare16_v2(db, sql, -1, out var statement, IntPtr.Zero) != 0)
            {
                return null;
            }

            try
            {
                if (after != long.MaxValue)
                {
                    Sqlite.sqlite3_bind_int64(statement, 1, after);
                }

                int step;
                while ((step = Sqlite.sqlite3_step(statement)) == Sqlite.Row)
                {
                    rows.Add(new Row(Sqlite.sqlite3_column_int64(statement, 0), Sqlite.Text(statement, 1), Sqlite.Blob(statement, 2)));
                }

                if (step != 101)   // SQLITE_DONE — anything else (busy, locked) stopped the read part-way
                {
                    return null;
                }
            }
            finally
            {
                Sqlite.sqlite3_finalize(statement);
            }
        }
        finally
        {
            Sqlite.sqlite3_close(db);
        }

        return rows;
    }

    /// <summary>App name: the Start menu's, else the program's own description, else guessed from the id (cached).</summary>
    private static string AppName(string appId) => Interop.AppNames.For(appId);

    /// <summary>How to launch the app when the toast has no deep link: <c>shell:AppsFolder\{AUMID}</c> if the shell can
    /// resolve it (opens the app), otherwise the action center. Opens the app to wherever it was — not necessarily the chat.</summary>
    private static string AppLaunch(string appId)
        => Interop.AppNames.InStartMenu(appId) ? @"shell:AppsFolder\" + appId : "ms-actioncenter:";

    protected override void Stop()
    {
        _stop.Cancel();
        // dispose only once the thread is out: a thread still busy (a slow first read) touched the disposed token on its next
        // round and the unhandled ObjectDisposedException ended the app (review 10-03)
        if (_thread is null || _thread.Join(TimeSpan.FromSeconds(2)))
        {
            _stop.Dispose();
        }
    }

    /// <summary>The SQLite that ships with Windows (winsqlite3.dll) — nothing extra to install.</summary>
    private static class Sqlite
    {
        public const int OpenReadOnly = 0x00000001;
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
}
