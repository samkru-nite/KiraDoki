namespace IsleBar.Core.Search;

/// <summary>
/// File search (Everything) rules that don't need Everything itself: which folders are left out, the query sent, the sort and
/// how many rows. Kept close to Everything's own window (user 10-04: "Everything already finds what I want — why not just use
/// that"): same name order with folders first, only the Windows folder hidden (prefetch files, system DLLs).
/// AppData stays — apps install there (…\AppData\Local\Programs) and so do Start menu shortcuts.
/// </summary>
public static class FileSearchQuery
{
    /// <summary>Sort setting values.</summary>
    public const string SortName = "name";
    public const string SortModified = "modified";
    public const string SortSize = "size";

    public static readonly IReadOnlyList<string> Sorts = [SortName, SortModified, SortSize];

    /// <summary>Rows shown: default and allowed range (the list scrolls past what fits).</summary>
    public const int DefaultCount = 8;
    public const int MinCount = 3;
    public const int MaxCount = 50;

    /// <summary>A known sort value, or the default (name) for anything else.</summary>
    public static string NormalizeSort(string? sort) => Sorts.Contains(sort) ? sort! : SortName;

    public static int ClampCount(int count) => Math.Clamp(count, MinCount, MaxCount);

    /// <summary>
    /// Everything SDK sort code: name ascending (as its window shows it — folders first), date modified newest first,
    /// size largest first.
    /// </summary>
    public static uint EverythingSort(string? sort) => NormalizeSort(sort) switch
    {
        SortModified => 14,   // EVERYTHING_SORT_DATE_MODIFIED_DESCENDING
        SortSize => 6,        // EVERYTHING_SORT_SIZE_DESCENDING
        _ => 1,               // EVERYTHING_SORT_NAME_ASCENDING
    };

    /// <summary>Folders never shown: only Windows itself.</summary>
    public static IReadOnlyList<string> SystemFolders(string windowsDir) => [windowsDir.TrimEnd('\\') + "\\"];

    /// <summary>
    /// The query sent to Everything: the person's words, grouped, then each system folder excluded. A query that names a path
    /// itself (contains \ or /) is sent untouched — the person is deliberately looking inside a folder, maybe a system one.
    /// </summary>
    public static string Build(string query, IReadOnlyList<string> excluded)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0 || TargetsPath(trimmed) || excluded.Count == 0)
        {
            return trimmed;
        }

        return $"<{trimmed}> " + string.Join(" ", excluded.Select(folder => $"!path:\"{folder}\""));
    }

    /// <summary>Whether the query names a path itself (then nothing is filtered).</summary>
    public static bool TargetsPath(string query) => query.Contains('\\') || query.Contains('/');

    /// <summary>Whether a result's full path is inside a system folder (a second check after Everything's own filter).</summary>
    public static bool IsSystem(string fullPath, IReadOnlyList<string> excluded)
        => excluded.Any(folder => fullPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
}
