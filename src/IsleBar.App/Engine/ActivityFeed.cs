using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.App.Media;
using IsleBar.App.SystemWatch;
using IsleBar.Core.Configuration;
using IsleBar.Core.Island;
using IsleBar.Core.Localization;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.Engine;

/// <summary>
/// Everything the pill can show, gathered in one list each tick: the state files (agents, timers, notices) plus the in-memory
/// sources (music, downloads, phone drop, system notices). Also runs the rules that act on that list — Pomodoro phases, the
/// chimes, marking agent turns whose terminal was closed as interrupted. No UI here; the window only draws what this returns.
/// </summary>
internal sealed class ActivityFeed : IDisposable
{
    private readonly Func<IsleBarSettings> _settings;
    private readonly Func<LanguageStrings> _text;
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    public ActivityFeed(Func<IsleBarSettings> settings, Func<LanguageStrings> text)
    {
        _settings = settings;
        _text = text;
        Media = new MediaWatcher(() => _settings().BrowserMedia);
        Downloads = new DownloadWatcher(() => _settings().DownloadFolders, text);
    }

    /// <summary>The state files (<c>%LOCALAPPDATA%\IsleBar\state</c>) — written by the CLI, the hooks and the bar itself.</summary>
    public ActivityStore Store { get; } = ActivityStore.CreateDefault();

    public MediaWatcher Media { get; }

    /// <summary>Agent cards' last answer / latest step for the lifted card (F2b).</summary>
    public AgentTails Tails { get; } = new();

    public DownloadWatcher Downloads { get; }


    /// <summary>Brief notices (charging, Bluetooth, internet, focus, copy, overload, calendar, mic/camera; features added 09-30). Null until <see cref="Start"/>.</summary>
    public SystemWatchers? Watchers { get; private set; }

    /// <summary>A new timer's file name. Timers run side by side since the input box went (10-10, TODO 三-④ B): each gets its own
    /// file, so its own card.</summary>
    private static string NewTimerSlot() => $"timer_{Environment.ProcessId}_{DateTime.UtcNow.Ticks:x}";

    public void Start()
    {
        Media.Start();
        Downloads.Start();
        Watchers = new SystemWatchers(_text);
        Watchers.Start(WatchOptions(_settings()));
        HookConnector.TidyCodex();   // older builds' Codex hooks only kept a "review hooks" dialog coming up (10-11)
    }

    /// <summary>Settings changed: pass the notice switches on to the watchers.</summary>
    public void ApplySettings() => Watchers?.Apply(WatchOptions(_settings()));

    /// <summary>
    /// Notice settings for the watchers. The Store build doesn't offer hiding Windows' banners (it has no uninstaller that could
    /// give them back).
    /// </summary>
    private static SystemWatchOptions WatchOptions(IsleBarSettings settings)
    {
        var options = SystemWatchOptions.From(settings);
        return AppPaths.IsPackaged ? options with { HideBanners = false } : options;
    }

    /// <summary>One tick: every item the pill could show right now, after the rules that act on them have run.</summary>
    public List<ActivityState> Collect(DateTimeOffset now)
    {
        if (now - _lastPrune > TimeSpan.FromMinutes(1))
        {
            _lastPrune = now;
            Store.PruneDead(_lastPrune);   // so finished timer files don't pile up
        }

        var items = Store.Read(now).ToList();
        AdvancePomodoros(items);
        ChimeFinishedTimers(items);
        InterruptDeadAgents(items);
        DropFinishedSameChat(items, now);
        ChimeFinishedAgents(items);
        Tails.Note(items);
        LogAgentChanges(items);
        items.AddRange(Media.Cards);   // one card per media player (user 10-07)

        items.AddRange(Downloads.Current);   // browser downloads (in progress / just finished)
        if (Watchers is not null)
        {
            items.AddRange(Watchers.Current);   // brief notices
            DropAgentAppToasts(items);
            DropDriveToasts(items);
            AddPrivacy(items, now);
        }

        return items;
    }

    /// <summary>How near an agent card's change an AI app's own Windows notification counts as the same news.</summary>
    private static readonly TimeSpan SameNews = TimeSpan.FromSeconds(20);

    private readonly HashSet<string> _droppedToasts = new(StringComparer.Ordinal);

    /// <summary>
    /// The Claude / Codex app's own Windows notification ("Claude finished") when that agent's card has just turned done / needs you /
    /// failed: the big pill showed the same news twice (user 10-10, 甲①). Kept when no card changed near it — a plain chat in the
    /// Claude app has no hook, so its notification is the only news. Log "toast: … dropped".
    /// </summary>
    private void DropAgentAppToasts(List<ActivityState> items)
    {
        static string? AgentOf(string? appId)
            => appId is null ? null
                : appId.StartsWith("Claude_", StringComparison.OrdinalIgnoreCase) ? IsleBar.Core.Launch.AgentKind.Claude
                : appId.StartsWith("OpenAI.", StringComparison.OrdinalIgnoreCase) ? IsleBar.Core.Launch.AgentKind.Codex
                : null;

        var changes = items.Where(s => s.Agent is not null && s.Kind is ActivityKind.AgentDone or ActivityKind.AgentPermission or ActivityKind.Error)
            .ToList();
        items.RemoveAll(toast =>
        {
            if (toast is not { Kind: ActivityKind.Notice, Stage: "expand" } || AgentOf(toast.AppId) is not { } agent
                || !changes.Any(c => c.Agent == agent && (c.UpdatedAt - toast.UpdatedAt).Duration() <= SameNews))
            {
                return false;
            }

            if (_droppedToasts.Add($"{toast.AppId}|{toast.UpdatedAt.ToUnixTimeMilliseconds()}"))
            {
                TaskbarHost.Log($"toast: {toast.AppId} \"{toast.Title}\" dropped — the {agent} card just said it");
            }

            return true;
        });
    }

    /// <summary>
    /// Windows' "AutoPlay" notification ("choose what happens with removable drives"): the USB card says the drive is in and its ↗
    /// opens it (TODO ④, 10-10: one thing, one card). Its banner was hidden already; now it doesn't reach the island either.
    /// </summary>
    private void DropDriveToasts(List<ActivityState> items)
        => items.RemoveAll(toast =>
        {
            if (toast is not { Kind: ActivityKind.Notice, Stage: "expand" }
                || !string.Equals(toast.AppId, "Windows.SystemToast.AutoPlay", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_droppedToasts.Add($"{toast.AppId}|{toast.UpdatedAt.ToUnixTimeMilliseconds()}"))
            {
                TaskbarHost.Log("toast: AutoPlay dropped — the USB card says it");
            }

            return true;
        });

    private DateTimeOffset? _privacySince;

    /// <summary>
    /// Mic / camera in use (TODO 7.9 D, 10-06): a status card while it lasts (its icon waits on the small pill, uncoloured — it is
    /// your own doing). It is noticed by its debut on the big pill (CardStack, user 10-07 — it used to send a separate temporary card,
    /// whose icon then popped up apart from it). Gone by itself when it stops.
    /// </summary>
    private void AddPrivacy(List<ActivityState> items, DateTimeOffset now)
    {
        var w = Watchers!;
        if (!w.MicInUse && !w.CameraInUse)
        {
            _privacySince = null;
            return;
        }

        var since = _privacySince ??= now;
        var camera = w.CameraInUse;
        ActivityState Card(string source, string? stage) => new()
        {
            RawKind = ActivityState.KindNotice,
            Title = camera ? _text().NoticeCameraInUse : _text().NoticeMicInUse,
            Name = w.PrivacyApp,
            Apps = w.PrivacyApps,
            Glyph = camera ? NoticeGlyphs.Camera : NoticeGlyphs.Microphone,
            State = "run",
            Stage = stage,
            SourcePath = source,
            UpdatedAt = since,
        };

        items.Add(Card("privacy", "privacy"));   // a status card: it shows on the big pill when it starts (debut, CardStack) and goes down
    }

    // ---------------- Timers ----------------

    public PomodoroLengths PomodoroLengths()
        => IsleBar.Core.Island.PomodoroLengths.FromMinutes(_settings().PomodoroFocus, _settings().PomodoroBreak, _settings().PomodoroLong);

    /// <summary>Starts a timer next to any that are running (each its own card).</summary>
    public void StartTimer(ActivityState next) => Store.Write(NewTimerSlot(), next);

    /// <summary>"Again" on a finished timer: the new run takes the same file, so the same card (one card per thing).</summary>
    public void RestartTimer(ActivityState old, ActivityState next)
        => Store.Write(old.SourcePath is { } path ? Path.GetFileNameWithoutExtension(path) : NewTimerSlot(), next);

    /// <summary>Timer pause ↔ resume. Rewrites the state file (so other windows/commands see the same state).</summary>
    public void TogglePause(ActivityState timer)
    {
        if (timer is not { Kind: ActivityKind.Timer, SourcePath: { } path })
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var next = timer.IsPaused ? TimerParser.Resume(timer, now) : TimerParser.Pause(timer, now);
        try
        {
            Store.Write(Path.GetFileName(path), next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void DeleteState(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Advance Pomodoro phases: after focus (green border 3s) comes a break, after a break the next focus round, and after round 4's long break it ends.
    /// Rewrites the same file, so the island treats it as "the same item" and doesn't jump — only the text and ring change.
    /// </summary>
    private void AdvancePomodoros(List<ActivityState> items)
    {
        var now = DateTimeOffset.UtcNow;
        var text = _text();
        for (var i = 0; i < items.Count; i++)
        {
            var state = items[i];
            if (state.Kind != ActivityKind.Timer || TimerParser.PomodoroPhase(state) is null || state.SourcePath is not { } path)
            {
                continue;
            }

            var next = TimerParser.AdvancePomodoro(state, now, text.Focus, text.Break, PomodoroLengths());
            if (ReferenceEquals(next, state))
            {
                continue;
            }

            // write the next phase first, then ring: a write that failed (file locked) used to ring again every tick (review 10-03)
            var slot = Path.GetFileNameWithoutExtension(path);
            try
            {
                if (next is null)
                {
                    File.Delete(path);
                    items.RemoveAt(i--);
                }
                else
                {
                    next.SourcePath = Store.Write(slot, next);
                    items[i] = next;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;   // tried again next tick, still silent
            }

            Chime.Play(_settings().MuteChimeFullscreen, alarm: true);   // a pomodoro phase just ended (focus → break, or the whole set finished)
        }
    }

    private readonly HashSet<string> _chimed = new();   // timer slots we have already sounded, so a finished timer chimes once, not every tick

    /// <summary>
    /// Rings once the moment a plain (non-pomodoro) timer reaches zero. Pomodoro phase-ends chime from <see cref="AdvancePomodoros"/>.
    /// The slot is remembered so it does not ring every tick while the finished timer sits there; the memory is cleared when the
    /// timer file is gone (dismissed) so the same slot can ring again next time (user 09-30: play a sound when timers finish).
    /// </summary>
    private void ChimeFinishedTimers(List<ActivityState> items)
    {
        var now = DateTimeOffset.UtcNow;
        var present = new HashSet<string>();
        foreach (var state in items)
        {
            if (state.Kind != ActivityKind.Timer || state.SourcePath is not { } path || TimerParser.PomodoroPhase(state) is not null)
            {
                continue;   // pomodoros ring from AdvancePomodoros; stopwatches have no end
            }

            var slot = Path.GetFileNameWithoutExtension(path);
            present.Add(slot);
            var finished = !state.IsPaused && !TimerParser.IsStopwatch(state) && TimerParser.Remaining(state, now) <= TimeSpan.Zero;
            if (finished && _chimed.Add(slot))
            {
                Chime.Play(_settings().MuteChimeFullscreen, alarm: true);
            }
            else if (!finished)
            {
                _chimed.Remove(slot);   // running again ("Again" keeps the slot): it rings again at the end
            }
        }

        _chimed.RemoveWhere(slot => !present.Contains(slot));   // a dismissed timer can ring again if it comes back
    }

    // ---------------- Agents ----------------

    private readonly Dictionary<string, string> _chatRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _chatRootTried = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A new turn in a chat clears the chat's old done / interrupted / stopped card (user 10-07). Within one session the hook already
    /// overwrites it; but the desktop app can carry a chat on in a new session (new id, new state file), which left the old card.
    /// The sessions are matched by their transcripts' first message (<see cref="ChatRoot"/>); the old one's transcript is found by its
    /// id (the state file name holds its first 8 characters) next to the new one's.
    /// </summary>
    private void DropFinishedSameChat(List<ActivityState> items, DateTimeOffset now)
    {
        var busy = items.Where(s => s.Agent == IsleBar.Core.Launch.AgentKind.Claude && s.Kind is ActivityKind.AgentWorking or ActivityKind.AgentPermission
                                    && s.Transcript is not null && s.SourcePath is not null).ToList();
        if (busy.Count == 0)
        {
            return;
        }

        foreach (var live in busy)
        {
            if (RootOf(live.Transcript!, now) is not { } root || Path.GetDirectoryName(live.Transcript!) is not { } folder)
            {
                continue;
            }

            foreach (var old in items.Where(s => s.Agent == IsleBar.Core.Launch.AgentKind.Claude && s.Kind is not (ActivityKind.AgentWorking or ActivityKind.AgentPermission)
                                                  && s.SourcePath is not null && s.SourcePath != live.SourcePath).ToList())
            {
                var file = Path.GetFileNameWithoutExtension(old.SourcePath!);   // claude_9a7d9157
                var id = file.Contains('_', StringComparison.Ordinal) ? file[(file.IndexOf('_', StringComparison.Ordinal) + 1)..] : string.Empty;
                if (id.Length < 8)
                {
                    continue;
                }

                string? transcript = null;
                try
                {
                    transcript = Directory.EnumerateFiles(folder, id + "*.jsonl").FirstOrDefault();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }

                if (transcript is null || RootOf(transcript, now) != root)
                {
                    continue;
                }

                TaskbarHost.Log($"agent: {Path.GetFileName(old.SourcePath)} ({old.Kind}) dropped — {Path.GetFileName(live.SourcePath)} goes on in the same chat");
                Store.Remove(file);
                items.Remove(old);
            }
        }
    }

    /// <summary>A transcript's chat id, cached (it never changes); a transcript without its first message yet is tried again after 2 s.</summary>
    private string? RootOf(string transcript, DateTimeOffset now)
    {
        if (_chatRoots.TryGetValue(transcript, out var known))
        {
            return known;
        }

        if (_chatRootTried.TryGetValue(transcript, out var tried) && now - tried < TimeSpan.FromSeconds(2))
        {
            return null;
        }

        _chatRootTried[transcript] = now;
        if (ChatRoot.Read(transcript) is { } root)
        {
            _chatRoots[transcript] = root;
            return root;
        }

        return null;
    }

    private Dictionary<string, string> _agentSeen = new(StringComparer.Ordinal);

    /// <summary>
    /// Logs every agent item that comes, changes stage or goes ("agent: claude_9a7d9157.json done → working · name · session file"),
    /// to see whether a new turn in the same chat reuses the session (user 10-07: an old "done" card seemed to stay).
    /// </summary>
    private void LogAgentChanges(List<ActivityState> items)
    {
        var now = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in items)
        {
            if (s.Agent is null || s.SourcePath is not { } path)
            {
                continue;
            }

            var file = Path.GetFileName(path);
            now[file] = $"{s.Kind}";
            var before = _agentSeen.TryGetValue(file, out var was) ? was : "(new)";
            if (before != now[file])
            {
                TaskbarHost.Log($"agent: {file} {before} → {s.Kind} · {s.Name} · {Path.GetFileNameWithoutExtension(s.Transcript ?? "-")}");
                Tails.For(s);   // logs "agent tail: …" — what the lifted card would say at this stage (F2b)
            }
        }

        foreach (var gone in _agentSeen.Keys.Where(k => !now.ContainsKey(k)))
        {
            TaskbarHost.Log($"agent: {gone} {_agentSeen[gone]} → (gone)");
        }

        _agentSeen = now;
    }

    private readonly HashSet<string> _chimedDone = new();   // agent-done states already sounded, so a finished task chimes once
    private bool _chimeSeeded;

    /// <summary>
    /// Rings once the moment an agent task finishes. The completion already outranks music on the pill, but a silent visual change
    /// is easy to miss while looking elsewhere (browsing, music playing) — a sound means you notice it (user 10-01).
    /// Keyed by the state file, and forgotten when it ages out, so a later run rings again.
    /// </summary>
    private void ChimeFinishedAgents(List<ActivityState> items)
    {
        var present = new HashSet<string>();
        var ring = false;
        foreach (var state in items)
        {
            if (state.Kind != ActivityKind.AgentDone || state.SourcePath is not { } path || !File.Exists(path))
            {
                continue;   // (a state cleared earlier in this tick — e.g. its terminal was closed — stays silent)
            }

            // Keyed by file + when it was written, so a second completion in the same session (rewriting the same file) rings too.
            var key = path + "|" + state.UpdatedAt.UtcTicks;
            present.Add(key);
            // Ring once per finished task. (We tried muting it while a terminal was focused, but it kept swallowing the sound during
            // testing and normal use — the user wants to hear every completion; 10-01.) The card/border still shows on the pill too.
            ring |= _chimedDone.Add(key);
        }

        // Completions already there when the bar starts (they linger 12 h) were rung when they happened — a restart
        // (or a watchdog relaunch) must not ring them all again (code review 10-01).
        if (ring && _chimeSeeded)
        {
            Chime.Play(_settings().MuteChimeFullscreen);
        }

        _chimeSeeded = true;
        _chimedDone.RemoveWhere(p => !present.Contains(p));
    }

    /// <summary>
    /// A session whose terminal was closed: a turn still working or waiting for an answer becomes an "interrupted" inform card
    /// (closing the window kills the session before its SessionEnd hook can run — user 10-06: say so, don't drop it). A finished
    /// task stays until closed by hand; looking at its terminal no longer clears it (TODO 7.9 D, 10-06).
    /// </summary>
    private void InterruptDeadAgents(List<ActivityState> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not { Kind: ActivityKind.AgentPermission or ActivityKind.AgentWorking, SourcePath: { } path, Pid: { } pid } state
                || ProcessAlive(pid))
            {
                continue;
            }

            TaskbarHost.Log($"{(state.Kind == ActivityKind.AgentWorking ? "working" : "permission")} interrupted: {Path.GetFileName(path)} (its terminal was closed)");
            var card = AgentNotices.Interrupted(state.Agent ?? IsleBar.Core.Launch.AgentKind.Claude, state.Name, _text(), DateTimeOffset.UtcNow);
            try
            {
                card.SourcePath = Store.Write(Path.GetFileNameWithoutExtension(path), card);
                items[i] = card;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // tried again next tick
            }
        }
    }

    private static bool ProcessAlive(int pid)
    {
        try
        {
            using var process = global::System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (global::System.ComponentModel.Win32Exception)
        {
            return true;   // it exists but we may not look inside (e.g. an agent in an Administrator terminal) — not "gone" (code review 10-02)
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Store build, first start: there was no setup program to connect Claude Code / Codex, so offer it once with a card
    /// (clicking it connects; settings has the same switch).
    /// </summary>
    public void OfferToConnectAgents()
    {
        var flag = Path.Combine(AppPaths.DataDirectory, "connect_offered.txt");
        if (!AppPaths.IsPackaged || HookConnector.IsConnected || File.Exists(flag))
        {
            return;
        }

        try
        {
            File.WriteAllText(flag, "1");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var notice = SystemNotice.Make("Claude Code", NoticeGlyphs.Bell, DateTimeOffset.UtcNow,
            msg: _text().ConnectAgentsCard, open: HookConnector.Command);
        notice.Stage = "expand";
        notice.Name = "IsleBar";
        Store.Write("islebar_connect", notice);
    }

    public void Dispose()
    {
        Media.Dispose();
        Downloads.Dispose();
        Watchers?.Dispose();
    }
}
