using System.Text.RegularExpressions;

namespace IsleBar.Core.Search;

/// <summary>
/// Folders the file search leaves out, editable in settings (user 10-04: "so I don't have to change the source"). One rule per
/// line: a full path from a drive (<c>C:\Recovery</c>) hides that folder; anything else is a folder name or the end of a path
/// (<c>node_modules</c>, <c>AppData\Local\Temp</c>) and hides it wherever it is. <c>*</c> stands for any part of one folder
/// name (<c>*.lrdata</c>); <c>%SystemRoot%</c>-style variables work; lines starting with # are notes.
/// <para>
/// Filtered by the bar after Everything answers, not inside the Everything query — 32 <c>!path:</c> terms made each search take
/// ~820 ms instead of ~60 (measured 10-04), while reading a thousand rows and checking them here takes a few ms.
/// </para>
/// </summary>
public static class FileExcludes
{
    /// <summary>
    /// A small list that suits any PC: Windows itself, system restore/upgrade folders, temp files and app caches — what no one
    /// searches for but fills every result list. Anything specific to how a person works (dev folders, app logs…) they add in
    /// settings. Kept on purpose: Program Files, ProgramData (Start menu, app libraries), AppData\Local\Programs, the recycle bin.
    /// </summary>
    public static readonly IReadOnlyList<string> Defaults =
    [
        "%SystemRoot%",
        @"C:\System Volume Information",
        @"C:\Recovery",
        @"C:\$WinREAgent",
        @"C:\$WINDOWS.~BT",
        @"C:\$WINDOWS.~WS",
        @"AppData\Local\Temp",
        "Cache",
        "INetCache",
        "GPUCache",
        "Code Cache",
    ];

    /// <summary>One parsed rule: from the drive (<see cref="Rooted"/>) or anywhere in the path. Always ends with \.</summary>
    public sealed record Rule(string Pattern, bool Rooted)
    {
        /// <summary>For a rule with *: the same test as a regular expression (* = any characters within one folder name).</summary>
        public Regex? Wildcard { get; } = Pattern.Contains('*')
            ? new Regex((Rooted ? "^" : string.Empty) + Regex.Escape(Pattern).Replace(@"\*", @"[^\\]*"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            : null;
    }

    /// <summary>Turns the settings lines into rules (blank lines and # notes skipped, variables expanded, duplicates dropped).</summary>
    public static IReadOnlyList<Rule> Parse(IEnumerable<string> lines, Func<string, string>? expand = null)
    {
        expand ??= Environment.ExpandEnvironmentVariables;
        var rules = new List<Rule>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            line = expand(line).Replace('/', '\\').Trim().TrimEnd('\\');
            if (line.Length == 0)
            {
                continue;
            }

            var rooted = line.Length >= 2 && line[1] == ':';
            var rule = rooted ? new Rule(line + "\\", true) : new Rule("\\" + line.TrimStart('\\') + "\\", false);
            if (!rules.Any(r => string.Equals(r.Pattern, rule.Pattern, StringComparison.OrdinalIgnoreCase)))
            {
                rules.Add(rule);
            }
        }

        return rules;
    }

    /// <summary>Whether a result is hidden — inside a hidden folder, or that folder itself.</summary>
    public static bool IsHidden(string fullPath, IReadOnlyList<Rule> rules)
    {
        var path = fullPath.TrimEnd('\\') + "\\";
        foreach (var rule in rules)
        {
            if (rule.Wildcard is { } wildcard
                    ? wildcard.IsMatch(path)
                    : rule.Rooted
                        ? path.StartsWith(rule.Pattern, StringComparison.OrdinalIgnoreCase)
                        : path.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
