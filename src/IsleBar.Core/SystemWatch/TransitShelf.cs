namespace IsleBar.Core.SystemWatch;

/// <summary>
/// The transit station (中转站, TODO ④, user 10-10): files dropped on the pill wait here until dragged out somewhere else.
/// It keeps <b>only where they are</b> — nothing is copied (a big file costs no space and goes in at once); a file deleted or moved
/// by someone else shows "not found" in the list. Dragging out follows File Explorer's rules (same drive = move, another drive = copy,
/// Ctrl / Shift change it) — the receiver does it; what was moved away leaves the station, what was copied stays.
/// This part: the list itself, in the order things came in, and how it is saved (one path per line). Not thread-safe — the owner locks.
/// </summary>
public sealed class TransitShelf
{
    private readonly List<string> _paths = [];

    /// <summary>What is in the station, oldest first.</summary>
    public IReadOnlyList<string> Paths => _paths;

    public int Count => _paths.Count;

    /// <summary>Puts paths in; ones already there or blank are skipped. Returns how many were new.</summary>
    public int Add(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var added = 0;
        foreach (var raw in paths)
        {
            if (Clean(raw) is { } path && !Contains(path))
            {
                _paths.Add(path);
                added++;
            }
        }

        return added;
    }

    /// <summary>Takes these paths out (the ✕, or moved away by a drag). Returns how many were there.</summary>
    public int Remove(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var removed = 0;
        foreach (var raw in paths)
        {
            if (Clean(raw) is { } path)
            {
                removed += _paths.RemoveAll(p => Same(p, path));
            }
        }

        return removed;
    }

    public void Clear() => _paths.Clear();

    public bool Contains(string path) => Clean(path) is { } clean && _paths.Any(p => Same(p, clean));

    /// <summary>One path per line, for the file that keeps the station over a restart.</summary>
    public string Save() => string.Join('\n', _paths);

    /// <summary>Reads what <see cref="Save"/> wrote. Anything unreadable gives an empty station.</summary>
    public static TransitShelf Load(string? text)
    {
        var shelf = new TransitShelf();
        if (!string.IsNullOrEmpty(text))
        {
            shelf.Add(text.Split('\n'));
        }

        return shelf;
    }

    /// <summary>The card's grey words: "1 file" / "3 files" (<paramref name="many"/> has "{n}").</summary>
    public static string CountText(int count, string one, string many)
        => count == 1 ? one : many.Replace("{n}", count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>A file's name as the list shows it: the last part of the path ("E:\" for a drive).</summary>
    public static string NameOf(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Trimmed, without a trailing slash (but "E:\" stays a root); null when blank.</summary>
    private static string? Clean(string? raw)
    {
        var path = raw?.Trim().Trim('"');
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 0 || trimmed.EndsWith(':') ? path : trimmed;
    }
}
