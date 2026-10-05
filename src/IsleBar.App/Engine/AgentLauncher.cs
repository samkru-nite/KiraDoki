using IsleBar.Core.Configuration;
using IsleBar.Core.Launch;
using IsleBar.Core.Models;

namespace IsleBar.App.Engine;

/// <summary>Starts Claude Code / Codex in a new console with the question. No UI — the caller shows a failure and keeps history.</summary>
internal static class AgentLauncher
{
    /// <returns>Null when the agent was started; otherwise the reason it couldn't be (shown on the pill).</returns>
    public static string? Start(IsleBarSettings settings, string question)
    {
        var profile = AgentProfiles.Get(settings.Agent);
        var args = profile.BuildArgs(ClaudeExecutable.Resolve(profile), question, settings.ValuesFor(settings.Agent)).ToList();
        if ((args[0].EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || args[0].EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            && File.Exists(args[0]))   // not installed at all: starting it below says so
        {
            // npm's codex.cmd: a .cmd runs through cmd.exe, which re-reads the prompt — "&" ran other commands, %NAME% was replaced
            // and only the first line arrived (review 10-03). Start node with the CLI's script instead; never through cmd.exe.
            if (NpmShim.Unwrap(args[0], File.Exists, File.ReadAllText, Environment.GetEnvironmentVariable("PATH")) is not { } direct)
            {
                return $"{Path.GetFileName(args[0])}: node / script not found";
            }

            args = [.. direct, .. args.Skip(1)];
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string workDir;
        if (Directory.Exists(settings.Folder))
        {
            workDir = settings.Folder;
        }
        else
        {
            // Claude Code doesn't persist trust for the home folder, so the Security guide shows every time → use a dedicated folder
            workDir = Path.Combine(home, FolderTrust.DefaultFolderName);
            Directory.CreateDirectory(workDir);
        }

        // Only IsleBar's own folder is marked trusted: for a folder the person picked (a repository just cloned, say) the agent's
        // own trust question stays — it guards against that folder's hooks and settings (review 10-03).
        if (string.Equals(Path.GetFullPath(workDir).TrimEnd('\\'), Path.Combine(home, FolderTrust.DefaultFolderName), StringComparison.OrdinalIgnoreCase))
        {
            if (settings.Agent != AgentKind.Codex)
            {
                FolderTrust.Ensure(Path.Combine(home, ".claude.json"), workDir);
            }
            else
            {
                var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } custom ? custom : Path.Combine(home, ".codex");
                FolderTrust.EnsureCodex(Path.Combine(codexHome, "config.toml"), workDir);
            }
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(args[0])
            {
                WorkingDirectory = workDir,
                UseShellExecute = false,
            };
            // if the bar was launched from inside another Claude session, that session marker is inherited and the new session's history saving is disabled
            // ("inherited CLAUDE_CODE_CHILD_SESSION marker", measured on PC 09-29) → don't pass session variables through.
            // A variable the person set themselves (user or system environment — CLAUDE_CODE_GIT_BASH_PATH, Bedrock / Vertex
            // switches, …) is theirs and stays; the bar used to drop those too, and claude failed only when started from it (review 10-03).
            foreach (var key in startInfo.Environment.Keys
                         .Where(k => (k.StartsWith("CLAUDECODE", StringComparison.OrdinalIgnoreCase)
                                      || k.StartsWith("CLAUDE_CODE_", StringComparison.OrdinalIgnoreCase)
                                      || k is "CLAUDE_PID" or "CLAUDE_EFFORT")
                                     && Environment.GetEnvironmentVariable(k, EnvironmentVariableTarget.User) is null
                                     && Environment.GetEnvironmentVariable(k, EnvironmentVariableTarget.Machine) is null)
                         .ToList())
            {
                startInfo.Environment.Remove(key);
            }

            // Launching into a fresh console (a GUI app has no TERM/COLORTERM to inherit) made the agent decide there was no colour
            // support and print monochrome — ugly (user 10-01). Force colour on; the CLI turns on the console's VT processing itself,
            // so the codes render. FORCE_COLOR covers Claude (Node), CLICOLOR_FORCE covers Codex, COLORTERM advertises truecolor.
            startInfo.Environment["FORCE_COLOR"] = "3";
            startInfo.Environment["CLICOLOR_FORCE"] = "1";
            startInfo.Environment["COLORTERM"] = "truecolor";

            foreach (var arg in args.Skip(1))
            {
                startInfo.ArgumentList.Add(arg);   // don't leave quoting to the shell
            }

            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return ex.Message;
        }

        return null;
    }
}
