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
/// </summary>
internal static class ChatOpener
{
    public static void Open(ActivityState s, string? record)
    {
        if (s.Agent != AgentKind.Claude)
        {
            TaskbarHost.Log($"open chat: {s.Agent} — only the Claude app for now");
            return;
        }

        // the record's name is the whole session id; without it, the 8 characters in the state file's name
        var id = record is not null ? Path.GetFileNameWithoutExtension(record) : IdOf(s.SourcePath);
        var desk = id is { Length: >= 8 } ? DesktopSession(id) : null;
        var url = desk is not null ? $"claude://code/continue?session={desk}" : "claude://";
        TaskbarHost.Log($"open chat: session {id ?? "-"} → {desk ?? "no desktop session (terminal?) — bring the app up"} · {url}");
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            TaskbarHost.Log($"open chat: failed — {ex.Message}");
        }
    }

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
