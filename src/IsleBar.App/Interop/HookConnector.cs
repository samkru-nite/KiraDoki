namespace IsleBar.App.Interop;

/// <summary>
/// Connects or disconnects Claude Code / Codex from inside the app (10-01) — the Store build has no setup program to do it,
/// and the setup build gets a way to undo it without uninstalling. Runs the same <c>hooks.ps1</c> the installer uses,
/// pointed at this build's CLI, so both builds write identical hooks.
/// </summary>
internal static class HookConnector
{
    /// <summary>The "connect" card's open value that means "connect now" rather than "open a page".</summary>
    public const string Command = "islebar:connect";

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string ClaudeSettings = Path.Combine(Home, ".claude", "settings.json");
    private static readonly string CodexHooks = Path.Combine(
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } codexHome ? codexHome : Path.Combine(Home, ".codex"), "hooks.json");

    /// <summary>One connect/disconnect at a time: two scripts editing the same JSON at once lose one of the edits.</summary>
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    /// <summary>
    /// Whether Claude Code's settings already call IsleBar. Never throws. Codex has no hooks any more (10-11): the ChatGPT app
    /// runs them only once the person approves them, and the bar reads its session records instead (<c>CodexWatcher</c>).
    /// </summary>
    public static bool IsConnected => Calls(ClaudeSettings);

    /// <summary>
    /// Takes out the Codex hooks older builds added — unapproved, they only kept a "review hooks" dialog coming up in the ChatGPT
    /// app (user 10-11). Once per start, in the background, and only when there are some: the script also brings Claude's
    /// hooks up to date when they are connected, and leaves them out when they aren't.
    /// </summary>
    public static void TidyCodex() => Task.Run(() =>
    {
        if (Calls(CodexHooks))
        {
            AppLog.Write("hooks: taking out the old Codex hooks");
            Set(IsConnected);
        }
    });

    private static bool Calls(string file)
    {
        try
        {
            return File.Exists(file) && File.ReadAllText(file).Contains("islebar.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Adds (true) or removes (false) IsleBar's hooks. Returns whether the script ran cleanly.</summary>
    public static bool Set(bool connect)
    {
        OneAtATime.Wait();
        try
        {
            var ok = Run(connect);
            if (ok)
            {
                RememberChoice(connect);
            }

            return ok;
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>
    /// The person's choice, for the installer: a reinstall used to tick "connect" again and undo a disconnect made in settings
    /// (review 10-03). HKCU\Software\IsleBar\HooksOff = 1 hides that setup option.
    /// </summary>
    private static void RememberChoice(bool connect)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\IsleBar");
            key.SetValue("HooksOff", connect ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Write("hooks: could not remember the choice — " + ex.Message);
        }
    }

    private static bool Run(bool connect)
    {
        if (!File.Exists(AppPaths.HooksScript))
        {
            AppLog.Write("hooks: script missing at " + AppPaths.HooksScript);
            return false;
        }

        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", AppPaths.HooksScript,
                                        connect ? "-Apply" : "-Revert", "-Exe", AppPaths.CliPath })
            {
                start.ArgumentList.Add(arg);
            }

            using var process = System.Diagnostics.Process.Start(start)!;
            // read both streams at once (one after the other can deadlock when the other fills up) and really stop at 30 s
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                AppLog.Write("hooks: the script took too long and was stopped");
                return false;
            }

            var output = stdout.Result + stderr.Result;
            AppLog.Write($"hooks: {(connect ? "connect" : "disconnect")} → exit {process.ExitCode} {output.Trim()}");
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            AppLog.Write("hooks: could not run the script — " + ex.Message);
            return false;
        }
    }
}
