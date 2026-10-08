using IsleBar.App.Interop;
using IsleBar.Core.Island;
using IsleBar.Core.Launch;

namespace IsleBar.App.Engine;

/// <summary>
/// An agent card's last answer / latest step for the lifted card (TODO 7.9 F2b), read from the end of the session record.
/// The record is the state's own <see cref="ActivityState.Transcript"/>; a done card has none (the hook's "Stop" carries no path in
/// CLIs before 10-08), so the path seen earlier for the same state file is remembered, and failing that the record is looked up by
/// the session id in the file name (<c>claude_9a7d9157</c> → <c>~/.claude/projects/*/9a7d9157*.jsonl</c>). Asked every tick while a
/// card is lifted: the file is read again only when it has grown, at most once a second. Logs "agent tail: …" when the result changes.
/// </summary>
internal sealed class AgentTails
{
    private static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(1);

    private sealed class Entry
    {
        public long Length = -1;
        public DateTimeOffset ReadAt;
        public AgentTail Tail = new(null, null);
    }

    private readonly Dictionary<string, string?> _paths = new(StringComparer.OrdinalIgnoreCase);   // state file → record (null = not found)
    private readonly Dictionary<string, Entry> _tails = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Remembers each agent state's record while it has one (working / needs you), for when it turns "done" without.</summary>
    public void Note(IEnumerable<ActivityState> items)
    {
        foreach (var s in items)
        {
            if (s.Agent is not null && s.SourcePath is { } file && s.Transcript is { Length: > 0 } record)
            {
                _paths[file] = record;
            }
        }
    }

    public AgentTail? For(ActivityState s)
    {
        if (s.Agent is null || s.Kind is not (ActivityKind.AgentDone or ActivityKind.AgentWorking or ActivityKind.AgentPermission)
            || RecordOf(s) is not { } record)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (!_tails.TryGetValue(record, out var entry))
        {
            entry = _tails[record] = new Entry();
        }

        if (now - entry.ReadAt < ReadEvery)
        {
            return entry.Tail;
        }

        entry.ReadAt = now;
        try
        {
            using var stream = new FileStream(record, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length == entry.Length)
            {
                return entry.Tail;
            }

            entry.Length = stream.Length;
            stream.Seek(Math.Max(0, stream.Length - AgentTranscript.TailBytes), SeekOrigin.Begin);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            var tail = AgentTranscript.FromTail(reader.ReadToEnd(), s.Agent);
            if (tail != entry.Tail)
            {
                TaskbarHost.Log($"agent tail: {Path.GetFileName(s.SourcePath)} {s.Kind} · answer {Short(tail.Answer)} · step {Short(tail.Step)} · said {Short(tail.Said)} ({Path.GetFileName(record)})");
            }

            entry.Tail = tail;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TaskbarHost.Log($"agent tail: {Path.GetFileName(record)} unreadable — {ex.Message}");
        }

        return entry.Tail;
    }

    /// <summary>The session record behind an agent card ("open chat" needs its session id). Null if not found.</summary>
    public string? RecordPath(ActivityState s) => s.Agent is null ? null : RecordOf(s);

    private string? RecordOf(ActivityState s)
    {
        if (s.Transcript is { Length: > 0 } own && File.Exists(own))
        {
            return own;
        }

        if (s.SourcePath is not { } file)
        {
            return null;
        }

        if (_paths.TryGetValue(file, out var known))
        {
            return known is not null && File.Exists(known) ? known : null;
        }

        var found = Find(s.Agent!, Path.GetFileNameWithoutExtension(file));
        _paths[file] = found;
        TaskbarHost.Log($"agent tail: {Path.GetFileName(file)} has no record path — looked it up: {found ?? "not found"}");
        return found;
    }

    /// <summary>The newest session record whose name starts with (Claude) or contains (Codex) the state file's session id.</summary>
    private static string? Find(string agent, string stateId)
    {
        var cut = stateId.IndexOf('_', StringComparison.Ordinal);
        var id = cut >= 0 ? stateId[(cut + 1)..] : string.Empty;
        if (id.Length < 8)
        {
            return null;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            var (folder, pattern) = agent == AgentKind.Codex
                ? (Path.Combine(home, ".codex", "sessions"), $"*{id}*.jsonl")
                : (Path.Combine(home, ".claude", "projects"), $"{id}*.jsonl");
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Short(string? s)
        => s is null ? "-" : $"{s.Length} chars \"{(s.Length > 40 ? s[..40] + "…" : s).ReplaceLineEndings(" ")}\"";
}
