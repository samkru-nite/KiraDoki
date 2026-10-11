namespace IsleBar.Core.SystemWatch;

/// <summary>
/// What became of files dragged out of the transit station (TODO ④, user 10-10: tell whether they were copied or moved). Only facts:
/// <list type="bullet">
/// <item>moved — no longer where they were;</item>
/// <item>copied — still where they were, and in the folder they were let go on there is one of that name made just now (a copy is
/// made new; a same-named file already there is old — the user's desktop had some, 10-10 log).</item>
/// </list>
/// Anything else (let go in an app, cancelled, not there yet) is neither, and is looked at again later or never told.
/// </summary>
public static class TransitOut
{
    /// <summary>A copy made a little before the drag "started" by our clock still counts.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(2);

    /// <param name="target">The folder they were let go on (null = not a folder: an app, the pills).</param>
    /// <param name="since">When the drag began.</param>
    /// <param name="exists">Whether a file or folder is at a path.</param>
    /// <summary>FAT32 (most USB sticks) keeps "modified" to the even second.</summary>
    public static readonly TimeSpan WrittenSlack = TimeSpan.FromSeconds(2);

    /// <param name="created">When the file or folder at a path was made (null = none there).</param>
    /// <param name="written">When the file at a path was last written (null = no file there, or a folder). A copy is made at full
    /// size up front and only gets the original's "modified" as the last step (user 10-11: a big copy was told "已复制" at 10%) — so a
    /// file is copied once they agree. Folders have no such step.</param>
    /// <param name="hadBefore">Whether a path was there when the drag began (null = that folder wasn't looked at). Known, it decides
    /// "made just now" instead of <paramref name="created"/>: Windows hands a creation time on to a file of the same name put in the
    /// same folder within 15 s ("tunnelling"), and a copy also takes the original's "changed" (10-11 logs).</param>
    public static (IReadOnlyList<string> Moved, IReadOnlyList<string> Copied) Check(
        IReadOnlyList<string> paths, string? target, DateTimeOffset since, Func<string, bool> exists, Func<string, DateTimeOffset?> created,
        Func<string, DateTimeOffset?>? written = null, Func<string, bool?>? hadBefore = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(created);
        var moved = new List<string>();
        var copied = new List<string>();
        foreach (var path in paths)
        {
            if (!exists(path))
            {
                moved.Add(path);
            }
            else if (target is not null
                     && !string.Equals(Path.GetDirectoryName(path)?.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                     && Path.Combine(target, Path.GetFileName(path)) is var there
                     && (hadBefore?.Invoke(there) is { } had ? !had && exists(there) : created(there) is { } made && made >= since - Slack)
                     && (written?.Invoke(path) is not { } was || (written(there) is { } now && (now - was).Duration() <= WrittenSlack)))
            {
                copied.Add(path);
            }
        }

        return (moved, copied);
    }

    /// <summary>"DSC02580.ARW +1".</summary>
    public static string Names(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Count == 0 ? string.Empty
            : Path.GetFileName(paths[0].TrimEnd('\\')) + (paths.Count > 1 ? $" +{paths.Count - 1}" : string.Empty);
    }
}
