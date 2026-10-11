namespace IsleBar.Core.SystemWatch;

/// <summary>What an archiver's progress window is doing: packing into an archive, or unpacking one.</summary>
public enum ArchiveJob
{
    Zip,
    Unzip,
}

/// <summary>One look at an archiver's progress window: the job, the archive (its full path when the window shows it), the overall
/// percentage, and whether it is paused; the file at hand and the time left as the window says them (read, not shown yet: for the
/// lifted card, TODO ⑤ 掀起卡片适配, user 10-11).</summary>
public sealed record ArchiveLook(ArchiveJob Job, string Archive, int? Percent, bool Paused, string? Current = null, TimeSpan? Left = null);

/// <summary>
/// WinRAR's progress window (TODO 四, measured 10-11 on WinRAR 7 in Chinese): a dialog whose title names the job and the archive —
/// "正在创建压缩文件 X.rar" / "正在从 X.rar 中解压" (English "Creating archive X" / "Extracting from X"); its first text is the archive's
/// full path; the overall percentage is the text after "进度" ("Progress"; the one before it is the file at hand). Paused, the title
/// is "暂停，点击"继续"恢复" — and "Cancel" pauses it first, so a cancelled job was paused at the end. Only the window's own words:
/// nothing is guessed.
/// </summary>
public static class ArchiverReader
{
    // seen 10-11: the Chinese create / extract; not seen yet (from WinRAR's own wording, a miss only means no card): updating, English
    private static readonly (string Head, string Tail, ArchiveJob Job)[] Titles =
    [
        ("正在创建压缩文件 ", string.Empty, ArchiveJob.Zip),
        ("正在更新压缩文件 ", string.Empty, ArchiveJob.Zip),
        ("正在从 ", " 中解压", ArchiveJob.Unzip),
        ("Creating archive ", string.Empty, ArchiveJob.Zip),
        ("Updating archive ", string.Empty, ArchiveJob.Zip),
        ("Extracting from ", string.Empty, ArchiveJob.Unzip),
    ];

    private static readonly string[] PausedHeads = ["暂停", "Paused"];
    private static readonly string[] ProgressLabels = ["进度", "Progress"];

    // the line before the file at hand ("正在添加" seen 10-11 packing; unpacking showed none that time) and the time-left label
    private static readonly string[] CurrentLabels = ["正在添加", "正在解压", "正在更新", "Adding", "Extracting", "Updating"];
    private static readonly string[] LeftLabels = ["剩余时间", "Time left"];

    /// <summary>
    /// Reads the window: a progress window's title and texts (top to bottom). A paused window names no job: it keeps
    /// <paramref name="before"/>'s. Null when this is no progress window (or a paused one never seen running).
    /// </summary>
    public static ArchiveLook? Read(string title, IReadOnlyList<string> texts, ArchiveLook? before)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(texts);
        var percent = Overall(texts);
        var current = After(texts, CurrentLabels) is { } file && !file.EndsWith('%') ? file : null;
        var left = After(texts, LeftLabels) is { } t && TimeSpan.TryParseExact(t, @"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture, out var span) ? span : (TimeSpan?)null;
        if (PausedHeads.Any(p => title.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return before is null ? null : before with { Paused = true, Percent = percent ?? before.Percent, Current = current ?? before.Current, Left = left };
        }

        foreach (var (head, tail, job) in Titles)
        {
            if (title.StartsWith(head, StringComparison.Ordinal) && title.EndsWith(tail, StringComparison.Ordinal) && title.Length > head.Length + tail.Length)
            {
                var named = title[head.Length..^tail.Length].Trim();
                // the first text is the archive's whole path — the same file
                var path = texts.FirstOrDefault(t => t.EndsWith(named, StringComparison.OrdinalIgnoreCase) && t.Contains('\\', StringComparison.Ordinal));
                return new ArchiveLook(job, path ?? named, percent ?? before?.Percent, Paused: false, current ?? before?.Current, left);
            }
        }

        return null;
    }

    // the options asked before a job starts (seen 10-11; English from WinRAR's own wording): going away as the job starts, it is no question
    private static readonly string[] SetupTitles = ["压缩文件名和参数", "解压路径和选项", "Archive name and parameters", "Extraction path and options"];

    /// <summary>
    /// Whether a window of the archiver beside a job asks something (a file already there, a password, an error, "really cancel?"):
    /// any shown dialog but the options one (10-11: the replace question comes up at the very moment the job's window does, so "came
    /// later" missed it). A miss on the safe side only: no "done" card.
    /// </summary>
    public static bool Asks(string title) => !SetupTitles.Contains(title.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The text right after one of these labels (the window lists a label, then its value), or null.</summary>
    private static string? After(IReadOnlyList<string> texts, string[] labels)
    {
        for (var i = 0; i + 1 < texts.Count; i++)
        {
            if (labels.Contains(texts[i].Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var value = texts[i + 1].Trim();
                return value.Length == 0 || labels.Contains(value, StringComparer.OrdinalIgnoreCase) ? null : value;
            }
        }

        return null;
    }

    /// <summary>The overall percentage: the "12%" right after the "进度" label.</summary>
    private static int? Overall(IReadOnlyList<string> texts)
    {
        for (var i = 0; i + 1 < texts.Count; i++)
        {
            if (ProgressLabels.Contains(texts[i].Trim(), StringComparer.OrdinalIgnoreCase)
                && texts[i + 1].Trim() is { Length: > 1 } value && value.EndsWith('%')
                && int.TryParse(value[..^1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p))
            {
                return Math.Clamp(p, 0, 100);
            }
        }

        return null;
    }
}

/// <summary>
/// One archiver job from its window's first look to its window going away (TODO 四, user 10-11: only say what is sure). Done = it
/// went away neither paused (cancel pauses first) nor having asked anything (another window of it came up: a file already there,
/// a password, an error — what was answered isn't said); else the card just goes.
/// </summary>
public sealed class ArchiveTally
{
    public ArchiveLook? Last { get; private set; }

    /// <summary>Another window of the archiver came up while this one worked.</summary>
    public bool Asked { get; private set; }

    public bool EverPaused { get; private set; }

    public void See(ArchiveLook look, bool asking)
    {
        ArgumentNullException.ThrowIfNull(look);
        Last = look;
        Asked |= asking;
        EverPaused |= look.Paused;
    }

    /// <summary>Surely through: never asked, not paused at the end; paused on the way and resumed only counts when it got to 99%.</summary>
    public bool Finished => Last is { Paused: false } last && !Asked && (!EverPaused || last.Percent >= 99);
}
