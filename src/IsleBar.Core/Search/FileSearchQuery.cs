namespace IsleBar.Core.Search;

/// <summary>
/// File search (Everything) rules that don't need Everything itself: the sort and how many rows. Kept close to Everything's own
/// window (user 10-04: "Everything already finds what I want — why not just use that"): same name order with folders first.
/// Hidden folders are <see cref="FileExcludes"/>.
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

    /// <summary>
    /// What the list shows before anything is typed (user 10-04: "like the Start menu, open with something in it"), when Windows'
    /// own "recently opened" list is off: files changed in the last week. Hidden folders are exactly the search's
    /// (<see cref="FileExcludes"/>) — one list in settings governs both (user 10-04).
    /// </summary>
    public const string RecentQuery = "file: dm:last7days";

    /// <summary>
    /// Sorts rows here the way Everything would for <paramref name="sort"/> — for lists that don't come from Everything
    /// (Windows' recently opened files): name A→Z with folders first, newest first, largest first.
    /// </summary>
    public static List<T> Order<T>(IEnumerable<T> items, string? sort, Func<T, string> name, Func<T, DateTime?> modified, Func<T, long?> size, Func<T, bool> folder)
        => NormalizeSort(sort) switch
        {
            SortModified => [.. items.OrderByDescending(i => modified(i) ?? DateTime.MinValue)],
            SortSize => [.. items.OrderByDescending(i => size(i) ?? -1)],
            _ => [.. items.OrderByDescending(folder).ThenBy(name, StringComparer.CurrentCultureIgnoreCase)],
        };

    /// <summary>Whether the query names a path itself — then nothing is hidden: the person is deliberately looking in a folder.</summary>
    public static bool TargetsPath(string query) => query.Contains('\\') || query.Contains('/');
}
