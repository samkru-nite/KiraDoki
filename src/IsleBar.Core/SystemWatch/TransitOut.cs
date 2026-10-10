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
    /// <param name="created">When the file or folder at a path was made (null = none there).</param>
    public static (IReadOnlyList<string> Moved, IReadOnlyList<string> Copied) Check(
        IReadOnlyList<string> paths, string? target, DateTimeOffset since, Func<string, bool> exists, Func<string, DateTimeOffset?> created)
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
                     && created(Path.Combine(target, Path.GetFileName(path))) is { } made && made >= since - Slack)
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
