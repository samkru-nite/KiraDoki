using System.Diagnostics;
using IsleBar.App.Interop;
using IsleBar.Core.Island;
using IsleBar.Core.Launch;

namespace IsleBar.App.Engine;

/// <summary>
/// The lifted card's "open chat" (TODO 7.9 F2b, user 10-08: the Claude app only, no terminals). The Claude app has one window for
/// all its chats, so there is no chat window to find; instead the app is asked to show the chat:
/// <c>claude://code/continue?session=local_…</c> (found in the app's own code, 10-08). The app names its sessions <c>local_…</c>;
/// which one holds a Claude Code session is in <c>%APPDATA%\Claude\claude-code-sessions\…\local_*.json</c> (<c>"cliSessionId"</c> =
/// the session record's name). Not found (a terminal session) → the app is only brought to the front. Logs "open chat: …".
/// UI thread only (the lookup cache isn't locked).
/// </summary>
internal static class ChatOpener
{
    /// <summary>
    /// True when <see cref="Open"/> would land on the chat itself (a Claude app session found), not just bring the app up — the done
    /// card's round "open" button shows only then (user 10-10: a button does what it says).
    /// </summary>
    public static bool CanOpen(ActivityState s, string? record)
        => s.Agent == AgentKind.Codex ? Thread(record) is not null : s.Agent == AgentKind.Claude && Desk(s, record).Desk is not null;

    /// <summary>Asks the Claude / ChatGPT app to show the chat. True when the chat itself was asked for and the request went out.</summary>
    public static bool Open(ActivityState s, string? record)
    {
        string url;
        bool exact;
        if (s.Agent == AgentKind.Codex)
        {
            var thread = Thread(record);
            (url, exact) = (thread is not null ? $"codex://threads/{thread}" : "codex://", thread is not null);
            TaskbarHost.Log($"open chat: ChatGPT thread {thread ?? "-"} · {url}");
        }
        else if (s.Agent == AgentKind.Claude)
        {
            var (id, desk) = Desk(s, record);
            (url, exact) = (desk is not null ? $"claude://code/continue?session={desk}" : "claude://", desk is not null);
            TaskbarHost.Log($"open chat: session {id ?? "-"} → {desk ?? "no desktop session (terminal?) — bring the app up"} · {url}");
        }
        else
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return exact;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            TaskbarHost.Log($"open chat: failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The session id and its desktop session. Looked up once per id (the pill asks every tick); a miss is asked again after
    /// <see cref="MissFor"/> — the app may write the session's file a moment after the hook fires.
    /// </summary>
    private static (string? Id, string? Desk) Desk(ActivityState s, string? record)
    {
        // the record's name is the whole session id; without it, the 8 characters in the state file's name
        var id = record is not null ? Path.GetFileNameWithoutExtension(record) : IdOf(s.SourcePath);
        if (id is not { Length: >= 8 })
        {
            return (id, null);
        }

        var now = DateTimeOffset.UtcNow;
        if (Known.TryGetValue(id, out var known) && (known.Desk is not null || now - known.At < MissFor))
        {
            return (id, known.Desk);
        }

        var desk = DesktopSession(id);
        Known[id] = (desk, now);
        return (id, desk);
    }

    private static readonly System.Text.RegularExpressions.Regex ThreadId = new(
        "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// The ChatGPT app's conversation behind a Codex record: the id at the end of its name (<c>rollout-…-{id}.jsonl</c>), shown with
    /// <c>codex://threads/{id}</c> — found in the app's own code and tried 10-11.
    /// </summary>
    private static string? Thread(string? record)
        => record is not null && ThreadId.Match(Path.GetFileNameWithoutExtension(record)) is { Success: true } m ? m.Value : null;

    private static readonly TimeSpan MissFor = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<string, (string? Desk, DateTimeOffset At)> Known = new(StringComparer.OrdinalIgnoreCase);

    private static string? IdOf(string? stateFile)
    {
        var name = stateFile is null ? null : Path.GetFileNameWithoutExtension(stateFile);
        var cut = name?.IndexOf('_', StringComparison.Ordinal) ?? -1;
        return cut >= 0 ? name![(cut + 1)..] : null;
    }

    /// <summary>The newest desktop session (<c>local_…</c>) whose <c>cliSessionId</c> starts with the id. Null if none.</summary>
    private static string? DesktopSession(string id)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var needle = "\"cliSessionId\":\"" + id;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "local_*.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    if (File.ReadAllText(file).Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        return Path.GetFileNameWithoutExtension(file);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }
}
