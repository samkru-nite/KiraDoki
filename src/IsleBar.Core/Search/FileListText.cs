namespace IsleBar.Core.Search;

/// <summary>Text shown in the file search list: sizes the way Explorer writes them, and long paths shortened in the middle.</summary>
public static class FileListText
{
    /// <summary>
    /// "3 KB" · "238,752 KB" read badly in a narrow list, so KB up to 1 MB (rounded up like Explorer — a 10-byte file is
    /// "1 KB", never "0 KB"), then "12.3 MB", "1.24 GB".
    /// </summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        <= 0 => "0 KB",
        < 1024 * 1024 => FormattableString.Invariant($"{Math.Ceiling(bytes / 1024.0):0} KB"),
        < 1024L * 1024 * 1024 => FormattableString.Invariant($"{bytes / 1024.0 / 1024:0.0} MB"),
        _ => FormattableString.Invariant($"{bytes / 1024.0 / 1024 / 1024:0.00} GB"),
    };

    /// <summary>Characters of the name kept before the extension when it's shortened ("…1004.xlog").</summary>
    private const int NameTail = 4;

    /// <summary>
    /// Shortens a file name to fit by cutting the middle: the start (what it is) and the end with the extension (which one, what
    /// type) stay — "MM_RENDER_20…1004.xlog". Cutting the end hid the extension and made names that differ only at the end
    /// (dates, versions) look the same (user 10-04).
    /// </summary>
    /// <param name="fits">Whether a candidate text fits the width (the app measures with the real font).</param>
    public static string MiddleEllipsisName(string name, Func<string, bool> fits)
    {
        if (fits(name))
        {
            return name;
        }

        var dot = name.LastIndexOf('.');
        var extension = dot > 0 && name.Length - dot <= 12 ? name[dot..] : string.Empty;
        var stem = name[..^extension.Length];
        var tail = stem[^Math.Min(NameTail, stem.Length / 2)..] + extension;
        var room = name.Length - tail.Length;

        // longest start that still fits — binary search, so a long name takes a handful of measurements, not dozens
        int low = 1, high = room - 1, best = 0;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (fits(name[..mid] + "…" + tail))
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best > 0 ? name[..best] + "…" + tail : "…" + tail;
    }

    /// <summary>
    /// Shortens a folder path to fit by dropping folders from the middle: the drive and first folder stay (where it is) and as
    /// many of the last folders as fit (what it is) — "C:\Users\…\Claude\sentry". Cutting the end, as the list did, hid exactly
    /// the part that tells similar results apart (user 10-04).
    /// </summary>
    /// <param name="fits">Whether a candidate text fits the width (the app measures with the real font).</param>
    public static string MiddleEllipsis(string path, Func<string, bool> fits)
    {
        if (fits(path))
        {
            return path;
        }

        var parts = path.Split('\\');
        if (parts.Length >= 4)
        {
            var head = parts[0] + "\\" + parts[1];
            for (var keep = parts.Length - 3; keep >= 1; keep--)
            {
                var candidate = head + "\\…\\" + string.Join("\\", parts[^keep..]);
                if (fits(candidate))
                {
                    return candidate;
                }
            }
        }

        // even the last folder alone is too long: keep its end
        for (var take = path.Length - 1; take > 0; take--)
        {
            var candidate = "…" + path[^take..];
            if (fits(candidate))
            {
                return candidate;
            }
        }

        return "…";
    }
}
