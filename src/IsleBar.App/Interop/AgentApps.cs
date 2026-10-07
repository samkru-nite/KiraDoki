using Microsoft.Win32;

namespace IsleBar.App.Interop;

/// <summary>
/// The Claude and Codex (ChatGPT) desktop apps installed from the Store (MSIX). Their folders are found through the per-user package
/// list in the registry — the WindowsApps folder itself can't be listed. Their own icons give the agents' logos (user 10-07: every
/// stage shows the maker's logo), so IsleBar ships no third-party logo.
/// </summary>
internal static class AgentApps
{
    private const string Packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>The Claude app's claude.exe (its icon holds the spark), or null.</summary>
    public static string? ClaudeExe() => Root("Claude_") is { } root ? Existing(Path.Combine(root, "app", "claude.exe")) : null;

    /// <summary>The Codex app's logo on a transparent ground: white for a dark taskbar, dark for a light one. Null if not installed.</summary>
    public static string? CodexLogo(bool light)
        => Root("OpenAI.Codex_") is { } root
            ? Existing(Path.Combine(root, "assets", $"Square44x44Logo.targetsize-256_altform-{(light ? "lightunplated" : "unplated")}.png"))
            : null;

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    /// <summary>The install folder of the newest package whose full name starts with <paramref name="prefix"/>.</summary>
    private static string? Root(string prefix)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Packages);
            return key?.GetSubKeyNames()
                .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => key.OpenSubKey(n)?.GetValue("PackageRootFolder") as string)
                .FirstOrDefault(Directory.Exists);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
