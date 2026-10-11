using IsleBar.Core.Island;
using IsleBar.Core.Launch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// Follows the session record of each running Claude Code session, for what no hook reports: Claude Code fires no Stop hook when
/// the user interrupts it (Esc) — the pill said "working" for hours (review 10-03). Its transcript gets "[Request interrupted by
/// user]"; the session's item is then cleared. (Codex's failed turns were read here too until 10-11 — its cards come from its
/// records altogether now, <see cref="CodexWatcher"/>.)
/// A session is followed while its item is "working" or "needs you". Only new bytes are read (records only grow); the first
/// read takes the end of the record, and from it only lines written after the current turn began count — an old failure or
/// interrupt further up used to come back on the next prompt (review 10-03). No slack: the turn's start, the state file and
/// the record lines all come from this PC's clock, and an interrupt is followed by the next prompt within a tenth of a second.
/// Before the item is changed the state file is checked again, so a hook that wrote meanwhile isn't overwritten.
/// </summary>
internal sealed class AgentRecordWatcher : NoticeWatcher
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private const int FirstReadBytes = 256 * 1024;   // when a session is first seen: enough of the end to find the latest usage

    private readonly object _gate = new();
    private readonly Dictionary<string, Followed> _followed = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;

    private sealed class Followed
    {
        public long Offset;
        public byte[] Pending = [];          // the end of the last read that isn't a whole line yet (bytes: a UTF-8 character may be split)
        public bool DropFirstLine;           // started mid-file: the first piece is the tail of a line
    }

    public AgentRecordWatcher(Func<Core.Localization.LanguageStrings> strings)
        : base(strings)
    {
    }

    public override void Start() => Guard(() =>
    {
        _timer ??= new Timer(_ => Guard(Scan), null, Interval, Interval);
    });

    protected override void Stop() => _timer?.Dispose();

    private void Scan()
    {
        if (!Monitor.TryEnter(_gate))
        {
            return;
        }

        try
        {
            var store = new ActivityStore(Interop.AppPaths.StateDirectory);
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(Interop.AppPaths.StateDirectory, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                var written = File.GetLastWriteTimeUtc(file);   // before reading it — a hook rewriting it after this is noticed below
                if (!id.StartsWith("claude_", StringComparison.OrdinalIgnoreCase)
                    || store.ReadById(id) is not { Transcript: { Length: > 0 } record } state
                    || state.Agent != AgentKind.Claude
                    || state.Kind is not (ActivityKind.AgentWorking or ActivityKind.AgentPermission)
                    || !File.Exists(record))
                {
                    continue;
                }

                live.Add(record);
                try
                {
                    // the turn's start: its own time, else when the state file was written (a permission state carries no t0)
                    var since = state.T0 is { } t0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(t0 * 1000)) : new DateTimeOffset(written, TimeSpan.Zero);
                    Follow(store, id, record, since, () => File.GetLastWriteTimeUtc(file) == written);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // one unreadable record must not stop the others (review 10-03); tried again next scan
                }
            }

            foreach (var gone in _followed.Keys.Where(k => !live.Contains(k)).ToList())
            {
                _followed.Remove(gone);
            }
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void Follow(ActivityStore store, string id, string record, DateTimeOffset since, Func<bool> unchanged)
    {
        using var stream = new FileStream(record, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var end = stream.Length;
        if (!_followed.TryGetValue(record, out var followed))
        {
            followed = new Followed { Offset = Math.Max(0, end - FirstReadBytes) };
            followed.DropFirstLine = followed.Offset > 0;
            _followed[record] = followed;
        }

        if (end < followed.Offset)
        {
            followed.Offset = 0;   // a new file under the same name
            followed.Pending = [];
            followed.DropFirstLine = false;
        }

        if (end == followed.Offset)
        {
            return;
        }

        // read exactly up to the length seen now — bytes appended meanwhile are read next time, not skipped (review 10-03)
        var fresh = new byte[end - followed.Offset];
        stream.Seek(followed.Offset, SeekOrigin.Begin);
        stream.ReadExactly(fresh);
        followed.Offset = end;

        var bytes = followed.Pending.Length == 0 ? fresh : [.. followed.Pending, .. fresh];
        var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        if (lastNewline < 0)
        {
            followed.Pending = bytes;
            return;
        }

        followed.Pending = bytes[(lastNewline + 1)..];
        var lines = System.Text.Encoding.UTF8.GetString(bytes, 0, lastNewline).Split('\n');
        var first = followed.DropFirstLine ? 1 : 0;
        followed.DropFirstLine = false;
        // the last interrupt of this turn with no prompt after it: stopped by the user, nothing is running any more
        var interrupted = false;
        for (var i = first; i < lines.Length; i++)
        {
            if (AgentRecord.IsClaudeInterrupt(lines[i]))
            {
                interrupted = AgentRecord.LineTime(lines[i]) is { } at && at > since;
            }
            else if (interrupted && AgentRecord.IsClaudePrompt(lines[i]))
            {
                interrupted = false;   // a new prompt right after — the interrupt belonged to the turn before
            }
        }

        if (interrupted && unchanged())
        {
            store.Remove(id);
            Interop.TaskbarHost.Log("claude: turn interrupted");
        }
    }
}
