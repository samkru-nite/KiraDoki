namespace IsleBar.Core.Launch;

/// <summary>
/// Finds the command-line <c>claude.exe</c> — its icon gives the island Claude's spark (<c>ClaudeGlyph</c>). PATH comes first;
/// otherwise <c>%USERPROFILE%\.local\bin\claude.exe</c> (where the non-npm install puts it). Launching agents was removed 10-10.
/// Only deals with path rules, so it is platform-independent — file checks go through the injected <paramref name="exists"/>.
/// </summary>
public static class ClaudeExecutable
{
    /// <summary>File name to look for on Windows.</summary>
    public const string WindowsFileName = "claude.exe";

    /// <summary>The path found. If nowhere, <c>%USERPROFILE%\.local\bin\claude.exe</c> as-is (it may not exist).</summary>
    /// <param name="searchPath">Value of the PATH environment variable.</param>
    /// <param name="userProfile">User folder (%USERPROFILE%).</param>
    /// <param name="exists">Function that checks whether a file exists.</param>
    /// <param name="fileName">File name to look for.</param>
    public static string Resolve(
        string? searchPath,
        string userProfile,
        Func<string, bool> exists,
        string fileName = WindowsFileName)
    {
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(userProfile);

        foreach (var dir in (searchPath ?? string.Empty).Split(Path.PathSeparator))
        {
            var trimmed = dir.Trim().Trim('"');
            if (trimmed.Length == 0)
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.Combine(trimmed, fileName);
            }
            catch (ArgumentException)
            {
                continue;    // entry containing characters not allowed in PATH
            }

            if (exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(userProfile, ".local", "bin", fileName);
    }

    /// <summary>Looks it up in the real environment.</summary>
    public static string Resolve()
        => Resolve(
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            File.Exists,
            OperatingSystem.IsWindows() ? WindowsFileName : "claude");
}
