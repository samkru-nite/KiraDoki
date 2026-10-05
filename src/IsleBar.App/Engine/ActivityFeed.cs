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
/// chimes, clearing agent states whose terminal was closed or looked at. No UI here; the window only draws what this returns.
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
        Downloads = new DownloadWatcher(() => _settings().DownloadFolders);
    }

    /// <summary>The state files (<c>%LOCALAPPDATA%\IsleBar\state</c>) — written by the CLI, the hooks and the bar itself.</summary>
    public ActivityStore Store { get; } = ActivityStore.CreateDefault();

    public MediaWatcher Media { get; }

    public DownloadWatcher Downloads { get; }

    public PhoneDrop PhoneDrop { get; } = new();

    /// <summary>Brief notices (charging, Bluetooth, internet, focus, copy, overload, calendar, mic/camera; features added 09-30). Null until <see cref="Start"/>.</summary>
    public SystemWatchers? Watchers { get; private set; }

    /// <summary>Timer file name — one at a time (starting a new one replaces it; same slot as typing "25min" in the input box).</summary>
    public static string TimerSlot => $"timer_{Environment.ProcessId}";

    public void Start()
    {
        Media.Start();
        Downloads.Start();
        Watchers = new SystemWatchers(_text);
        Watchers.Start(WatchOptions(_settings()));
    }

    /// <summary>Settings changed: pass the notice switches on to the watchers.</summary>
    public void ApplySettings() => Watchers?.Apply(WatchOptions(_settings()));

    /// <summary>
    /// Notice settings for the watchers. The Store build leaves updates to the Store (its policy forbids self-updating) and
    /// doesn't offer hiding Windows' banners (it has no uninstaller that could give them back).
    /// </summary>
    private static SystemWatchOptions WatchOptions(IsleBarSettings settings)
    {
        var options = SystemWatchOptions.From(settings);
        return AppPaths.IsPackaged ? options with { Updates = false, HideBanners = false } : options;
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
        DismissSeenAgents(items);   // first, so a completion whose terminal is already gone is cleared without a chime
        ChimeFinishedAgents(items);
        if (Media.Current is { } music)
        {
            items.Add(music);
        }

        items.AddRange(Downloads.Current);   // browser downloads (in progress / just finished)
        items.AddRange(PhoneDrop.Current(items));   // dropped files being sent to the phone
        if (Watchers is not null)
        {
            items.AddRange(Watchers.Current);   // brief notices
        }

        return items;
    }

    // ---------------- Timers ----------------

    public PomodoroLengths PomodoroLengths()
        => IsleBar.Core.Island.PomodoroLengths.FromMinutes(_settings().PomodoroFocus, _settings().PomodoroBreak, _settings().PomodoroLong);

    /// <summary>A timer is running or paused (a finished one waiting to be dismissed doesn't count).</summary>
    public bool TimerRunning(DateTimeOffset now)
        => Store.Read(now).Any(s => s.Kind == ActivityKind.Timer
            && (s.IsPaused || TimerParser.IsStopwatch(s) || TimerParser.Remaining(s, now) > TimeSpan.Zero));

    /// <summary>Writes the timer, removing any other timer first — one left behind by an earlier bar process would otherwise stay too.</summary>
    public void ReplaceTimer(ActivityState next)
    {
        foreach (var old in Store.Read(DateTimeOffset.UtcNow).Where(s => s.Kind == ActivityKind.Timer && s.SourcePath is not null))
        {
            DeleteState(old.SourcePath!);
        }

        Store.Write(TimerSlot, next);
    }

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
        }

        _chimed.RemoveWhere(slot => !present.Contains(slot));   // a dismissed timer can ring again if it comes back
    }

    // ---------------- Agents ----------------

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

    // Terminal window of each finished task's session, looked up once per state file (finding it briefly attaches to the console).
    // A failed lookup (no visible terminal — e.g. an editor's built-in terminal) is retried after a while rather than kept forever.
    private readonly Dictionary<string, IntPtr> _doneWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _doneWindowRetry = new(StringComparer.OrdinalIgnoreCase);
    private readonly TerminalInputWatch _terminalInput = new();
    private HashSet<IntPtr> _watchedWindows = [];

    /// <summary>
    /// A finished task's green border clears once you've actually gone back to its terminal — clicked it or typed in it — or when that
    /// terminal is closed (user 10-01). Merely having the window in front doesn't count: it can be left open while you're away, and
    /// if you were watching it when the task ended the green vanished before you could see it. ✕, a click on the bar and the next
    /// prompt still clear it too.
    /// </summary>
    private void DismissSeenAgents(List<ActivityState> items)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in items)
        {
            // A question/permission prompt whose terminal was closed can't be answered any more — clear the orange too.
            // Same for "working": closing the window kills the session before its SessionEnd hook can run, which left a dead
            // session "working" for 6 h (found 10-01 when a terminal holding three sessions closed).
            if (state is { Kind: ActivityKind.AgentPermission or ActivityKind.AgentWorking, SourcePath: { } asked, Pid: { } askPid }
                && !ProcessAlive(askPid))
            {
                TaskbarHost.Log($"{(state.Kind == ActivityKind.AgentWorking ? "working" : "permission")} cleared: {Path.GetFileName(asked)} (its terminal was closed)");
                DeleteState(asked);
                continue;
            }

            if (state.Kind != ActivityKind.AgentDone || state.SourcePath is not { } path || state.Pid is not { } pid)
            {
                continue;
            }

            if (!ProcessAlive(pid))
            {
                TaskbarHost.Log($"done cleared: {Path.GetFileName(path)} (its terminal was closed)");
                DeleteState(path);
                continue;
            }

            present.Add(path);
            if (!_doneWindows.TryGetValue(path, out var window)
                || (window == IntPtr.Zero && DateTimeOffset.UtcNow >= _doneWindowRetry.GetValueOrDefault(path)))
            {
                window = AgentWindow.Find(pid);
                _doneWindows[path] = window;
                _doneWindowRetry[path] = DateTimeOffset.UtcNow.AddSeconds(30);
            }

            if (window != IntPtr.Zero && _terminalInput.TakeTouched(window))
            {
                TaskbarHost.Log($"done seen: {Path.GetFileName(path)} (clicked or typed in its terminal)");
                DeleteState(path);
                present.Remove(path);
            }
        }

        foreach (var gone in _doneWindows.Keys.Where(p => !present.Contains(p)).ToList())
        {
            _doneWindows.Remove(gone);
            _doneWindowRetry.Remove(gone);
        }

        // Hooks only while a finished task is waiting (installing/removing only when that changes)
        var windows = _doneWindows.Values.Where(w => w != IntPtr.Zero).ToHashSet();
        if (!windows.SetEquals(_watchedWindows))
        {
            _watchedWindows = windows;
            _terminalInput.Watch(windows);
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

        var notice = SystemNotice.Make("Claude Code · Codex", "", DateTimeOffset.UtcNow,
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
        _terminalInput.Dispose();
    }
}
