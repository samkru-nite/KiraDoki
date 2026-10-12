using System.Text;

namespace IsleBar.App.Interop;

/// <summary>
/// The app's one log file (<c>%LOCALAPPDATA%\IsleBar\logs\islebar.log</c>). Appends a timestamped line and <b>rotates</b> when
/// the file grows past a cap, so a process that runs 24/7 can never quietly fill the disk — the old content moves to
/// <c>islebar.log.1</c> (one backup kept) and a fresh file starts. Thread-safe: the watchdog thread, the UI thread and the
/// taskbar host all log through here (added 10-01; before this each writer appended with no size limit).
/// <para>
/// 10-12: <see cref="Write"/> only queues the line (its time taken at once) and returns; a background thread writes the file.
/// Writing on the caller's thread put a file open + append + close on the UI thread — measured at several ms, a whole frame at
/// 165 Hz. <see cref="Flush"/> before the app ends so the last lines aren't lost.
/// </para>
/// </summary>
internal static class AppLog
{
    /// <summary>Rotate once the current file passes this size. One backup is kept, so the log costs at most ~2× this on disk.</summary>
    private const long MaxBytes = 1 << 20;   // 1 MB

    /// <summary>Lines waiting at most; more are dropped (and counted) so a stuck disk can't make the queue grow for ever.</summary>
    private const int MaxQueued = 5000;

    private static readonly object Gate = new();       // the queue
    private static readonly object FileGate = new();   // the file: the writer thread and Flush take turns
    private static readonly Queue<string> Pending = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static int _dropped;
    private static Thread? _writer;

    public static string LogFile => Path.Combine(AppPaths.LogDirectory, "islebar.log");

    /// <summary>The previous file, kept after a rotation (may not exist).</summary>
    public static string BackupFile => LogFile + ".1";

    /// <summary>Queues one timestamped line. Never throws and never waits on the disk — failing to log must not disturb the app.</summary>
    public static void Write(string message)
    {
        var line = $"{DateTime.Now:MM-dd HH:mm:ss} {message}";
        lock (Gate)
        {
            if (Pending.Count >= MaxQueued)
            {
                _dropped++;
                return;
            }

            Pending.Enqueue(line);
            if (_writer is null)
            {
                _writer = new Thread(WriterLoop) { IsBackground = true, Name = "IsleBar log writer", Priority = ThreadPriority.BelowNormal };
                _writer.Start();
            }
        }

        Wake.Set();
    }

    /// <summary>Writes out every queued line now, on this thread (waits at most a second for the writer thread's turn).</summary>
    public static void Flush()
    {
        if (!Monitor.TryEnter(FileGate, TimeSpan.FromSeconds(1)))
        {
            return;
        }

        try
        {
            WriteOut();
        }
        finally
        {
            Monitor.Exit(FileGate);
        }
    }

    private static void WriterLoop()
    {
        while (true)
        {
            Wake.WaitOne();
            lock (FileGate)
            {
                WriteOut();
            }
        }
    }

    /// <summary>Appends the queued lines in one go. Called with <see cref="FileGate"/> held.</summary>
    private static void WriteOut()
    {
        List<string> lines;
        int dropped;
        lock (Gate)
        {
            if (Pending.Count == 0 && _dropped == 0)
            {
                return;
            }

            lines = [.. Pending];
            Pending.Clear();
            dropped = _dropped;
            _dropped = 0;
        }

        if (dropped > 0)
        {
            lines.Add($"{DateTime.Now:MM-dd HH:mm:ss} log: {dropped} lines dropped (too many waiting to be written)");
        }

        var started = Perf.Now;
        try
        {
            var path = LogFile;
            RotateIfBig(path);
            File.AppendAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // failing to log is fine
        }

        Perf.LogWritten(started);
    }

    private static void RotateIfBig(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytes)
            {
                return;
            }

            var backup = path + ".1";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }

            File.Move(path, backup);   // the current file becomes the single backup; the next append starts a fresh one
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // if rotation fails, keep appending to the existing file rather than lose the message
        }
    }
}
