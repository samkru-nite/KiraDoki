using System.Text.Json;
using System.Text.RegularExpressions;
using IsleBar.Core.Island;
using IsleBar.Core.Launch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// The ChatGPT app's (the old Codex app's) cards, from its session records alone (user 10-11): it runs hooks only once the person
/// approves them in a "review hooks" dialog, and ours sat there for days unapproved. Each record is followed as it grows
/// (<see cref="CodexRollout"/> says working / asking / done / failed) and written as the session's state file, so everything
/// downstream — the pill, the lifted card's answer, closing — works as it did with the hooks.
/// <list type="bullet">
/// <item>Which records: a file watcher on the sessions folder (a conversation from last week continued today grows in last week's
/// folder), plus at start the ones written in the last day. Each followed file is looked at every second (its own size — the
/// folder listing's sizes lag behind for files held open).</item>
/// <item>The first read of a record only sets where it stands: a turn under way gets its working card, an answer from before the
/// bar started gets none. After that every change of look is written; a name the app gives the conversation later is too.</item>
/// <item>The name: the app's own (<c>session_index.jsonl</c>, <c>thread_name</c>), else the first prompt.</item>
/// <item>Sub-agents and the approvals reviewer have records of their own (<c>parent_thread_id</c>) — no cards.</item>
/// <item>The app closed mid-turn writes nothing more: no Codex process for <see cref="GoneAfter"/> → "interrupted", as the
/// SessionEnd hook did.</item>
/// </list>
/// Logs "codex: …" for every card written and once for each record line type it doesn't know.
/// </summary>
internal sealed class CodexWatcher : NoticeWatcher
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GoneAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(12);
    private const long WholeFileUpTo = 32L * 1024 * 1024;
    private const int TailBytes = 1024 * 1024;

    private static readonly string Home = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } codexHome
        ? codexHome
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    private static readonly string Sessions = Path.Combine(Home, "sessions");
    private static readonly string Index = Path.Combine(Home, "session_index.jsonl");
    private static readonly Regex ThreadId = new("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly Dictionary<string, Followed> _followed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _touched = new(StringComparer.OrdinalIgnoreCase);   // from the file watcher, any thread
    private readonly HashSet<string> _logged = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private long _indexLength = -1;
    private DateTimeOffset? _noProcessSince;
    private FileSystemWatcher? _files;
    private Timer? _timer;

    private sealed class Followed
    {
        public long Offset;
        public byte[] Pending = [];
        public bool DropFirstLine;
        public bool Seen;                  // the first read is done
        public CodexRollout Rollout = new();
        public CodexLook Written = CodexLook.None;
        public string? WrittenName;
        public DateTimeOffset GrewAt;
    }

    public CodexWatcher(Func<Core.Localization.LanguageStrings> strings)
        : base(strings)
    {
    }

    public override void Start() => Guard(() =>
    {
        if (!Directory.Exists(Sessions))
        {
            return;   // no Codex on this PC (checked again on the next start)
        }

        _files = new FileSystemWatcher(Sessions, "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };
        _files.Changed += (_, e) => Touch(e.FullPath);
        _files.Created += (_, e) => Touch(e.FullPath);
        _files.Renamed += (_, e) => Touch(e.FullPath);
        _files.EnableRaisingEvents = true;

        var since = DateTime.UtcNow.AddDays(-1);
        foreach (var file in new DirectoryInfo(Sessions).EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories))
        {
            if (file.LastWriteTimeUtc > since)
            {
                Touch(file.FullName);
            }
        }

        _timer ??= new Timer(_ => Guard(Scan), null, TimeSpan.Zero, Interval);
    });

    protected override void Stop()
    {
        _files?.Dispose();
        _timer?.Dispose();
    }

    private void Touch(string path)
    {
        lock (_touched)
        {
            _touched.Add(path);
        }
    }

    private void Scan()
    {
        if (!Monitor.TryEnter(_gate))
        {
            return;
        }

        try
        {
            lock (_touched)
            {
                foreach (var path in _touched)
                {
                    if (!_followed.ContainsKey(path))
                    {
                        _followed[path] = new Followed { GrewAt = DateTimeOffset.UtcNow };
                    }
                }

                _touched.Clear();
            }

            ReadNames();
            var store = new ActivityStore(Interop.AppPaths.StateDirectory);
            var now = DateTimeOffset.UtcNow;
            foreach (var (path, followed) in _followed.ToList())
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        _followed.Remove(path);
                        continue;
                    }

                    if (Grow(path, followed, now))
                    {
                        Show(store, path, followed, now);
                    }
                    else if (followed.Seen && followed.Written == CodexLook.Working)
                    {
                        Show(store, path, followed, now);   // still waiting for an answer → orange after AskAfter
                    }

                    if (now - followed.GrewAt > ForgetAfter && followed.Written is not (CodexLook.Working or CodexLook.Asking))
                    {
                        _followed.Remove(path);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // one unreadable record must not stop the others; tried again next scan
                }
            }

            CheckGone(store, now);
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    /// <summary>Reads what was added since last time into the record's reader. False if nothing was.</summary>
    private static bool Grow(string path, Followed followed, DateTimeOffset now)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var end = stream.Length;
        if (!followed.Seen && followed.Offset == 0 && end > WholeFileUpTo)
        {
            followed.Offset = end - TailBytes;
            followed.DropFirstLine = true;
        }

        if (end < followed.Offset)
        {
            followed.Offset = 0;   // a new file under the same name
            followed.Pending = [];
            followed.DropFirstLine = false;
            followed.Rollout = new CodexRollout();
        }

        if (end == followed.Offset)
        {
            return !followed.Seen;
        }

        // exactly up to the length seen now — bytes appended meanwhile are read next time
        var fresh = new byte[end - followed.Offset];
        stream.Seek(followed.Offset, SeekOrigin.Begin);
        stream.ReadExactly(fresh);
        followed.Offset = end;
        followed.GrewAt = now;

        var bytes = followed.Pending.Length == 0 ? fresh : [.. followed.Pending, .. fresh];
        var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        if (lastNewline < 0)
        {
            followed.Pending = bytes;
            return !followed.Seen;
        }

        followed.Pending = bytes[(lastNewline + 1)..];
        var lines = System.Text.Encoding.UTF8.GetString(bytes, 0, lastNewline).Split('\n');
        for (var i = followed.DropFirstLine ? 1 : 0; i < lines.Length; i++)
        {
            followed.Rollout.Read(lines[i].TrimEnd('\r'));
        }

        followed.DropFirstLine = false;
        return true;
    }

    private void Show(ActivityStore store, string path, Followed followed, DateTimeOffset now)
    {
        var rollout = followed.Rollout;
        foreach (var type in rollout.Unknown)
        {
            if (_logged.Add(type))
            {
                Interop.TaskbarHost.Log($"codex: record line type not known here: {type} ({Path.GetFileName(path)})");
            }
        }

        if (rollout.ParentId is not null)
        {
            followed.Seen = true;
            return;   // a sub-agent or the approvals reviewer: the session it works for has the card
        }

        var look = rollout.LookAt(now);
        var name = ThreadId.Match(Path.GetFileNameWithoutExtension(path)) is { Success: true } m && _names.TryGetValue(m.Value, out var named)
            ? named
            : null;
        name ??= rollout.Prompt;
        if (!followed.Seen)
        {
            followed.Seen = true;
            followed.WrittenName = name;
            if (look is not (CodexLook.Working or CodexLook.Asking))
            {
                followed.Written = look;   // over before the bar started: nothing to say now
                return;
            }
        }
        // a new name only while under way: rewriting a done card would bring it back after it was closed
        else if (look == followed.Written && (name == followed.WrittenName || look is not (CodexLook.Working or CodexLook.Asking)))
        {
            return;
        }

        var id = CodexRollout.IdFor(path);
        var shown = followed.Written;
        followed.Written = look;
        followed.WrittenName = name;
        if (rollout.Card(look, name, path, Text, now) is { } card)
        {
            store.Write(id, card);
        }
        else if (shown is CodexLook.Working or CodexLook.Asking)
        {
            store.Remove(id);   // stopped by the person: nothing is running any more (Claude's Esc does the same)
        }

        Interop.TaskbarHost.Log($"codex: {id} {shown} → {look}{(name is null ? "" : " · " + name)}");
    }

    /// <summary>The app's names for its conversations (each line: id + thread_name; a later line wins). Read when the file grows.</summary>
    private void ReadNames()
    {
        try
        {
            var length = File.Exists(Index) ? new FileInfo(Index).Length : 0;
            if (length == _indexLength)
            {
                return;
            }

            _indexLength = length;
            using var stream = new FileStream(Index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                        && root.TryGetProperty("thread_name", out var name) && name.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(name.GetString()))
                    {
                        _names[id.GetString()!] = name.GetString()!.Trim();
                    }
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A turn showing as under way while no Codex runs any more (the app was closed mid-turn) → "interrupted".</summary>
    private void CheckGone(ActivityStore store, DateTimeOffset now)
    {
        var open = _followed.Where(f => f.Value.Written is CodexLook.Working or CodexLook.Asking).ToList();
        if (open.Count == 0)
        {
            _noProcessSince = null;
            return;
        }

        if (Running())
        {
            _noProcessSince = null;
            return;
        }

        _noProcessSince ??= now;
        if (now - _noProcessSince < GoneAfter)
        {
            return;
        }

        foreach (var (path, followed) in open)
        {
            followed.Written = CodexLook.None;
            var id = CodexRollout.IdFor(path);
            store.Write(id, AgentNotices.Interrupted(AgentKind.Codex, followed.WrittenName, Text, now));
            Interop.TaskbarHost.Log($"codex: {id} no Codex running any more → interrupted");
        }
    }

    private static bool Running()
    {
        var found = false;
        foreach (var name in (string[])["codex", "ChatGPT"])
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(name);
            found |= processes.Length > 0;
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }

        return found;
    }
}
