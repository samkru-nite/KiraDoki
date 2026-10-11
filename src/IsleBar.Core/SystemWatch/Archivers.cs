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
public sealed record ArchiveLook(ArchiveJob Job, string Archive, int? Percent, bool Paused, string? Current = null, TimeSpan? Left = null)
{
    /// <summary>Bytes a second, when the window says it (7-Zip does; WinRAR doesn't) — for the lifted card too.</summary>
    public double? Speed { get; init; }
}

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
        if (Bandizip.Match(title) is { Success: true } bandi)
        {
            return BandizipLook(bandi, texts, before);   // first: its title reads like 7-Zip's too
        }

        if (SevenZip.Match(title) is { Success: true } seven)
        {
            return SevenZipLook(seven, texts, before);
        }

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

    /// <summary>
    /// 7-Zip's progress window (measured 10-11, 7-Zip in Chinese, 7zG.exe from the right-click menu): the percentage and the job in
    /// the title — "40% 正在压缩 C:\Users\Kasumi\De ... 阳-扫街\0207-南阳-扫街.7z" (the path cut in the middle, the name whole),
    /// "62% 正在解压 …", paused "已暂停 30% 正在压缩 …"; for a moment at the start just " 正在压缩" (no job yet: nothing to show). English
    /// from 7-Zip's own wording, not seen. Its texts: the labels first, then their values in the same order — the times
    /// (elapsed, left) as "00:00:17", the speed "15 MB/s", and the file at hand after "正在添加".
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex SevenZip = new(
        @"^\s*(?<paused>已暂停|Paused)?\s*(?<p>\d{1,3})%\s+(?<job>正在压缩|正在解压|Compressing|Extracting)\s+(?<path>.+)$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static ArchiveLook SevenZipLook(System.Text.RegularExpressions.Match m, IReadOnlyList<string> texts, ArchiveLook? before)
    {
        var path = m.Groups["path"].Value.Trim();
        var job = m.Groups["job"].Value is "正在解压" or "Extracting" ? ArchiveJob.Unzip : ArchiveJob.Zip;
        var times = texts.Select(t => t.Trim()).Where(t => TimeSpan.TryParseExact(t, @"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture, out _)).ToList();
        var current = After(texts, CurrentLabels) is { } file && !file.EndsWith('%') ? file : before?.Current;
        return new ArchiveLook(job, path[(path.LastIndexOf('\\') + 1)..], int.Parse(m.Groups["p"].Value, System.Globalization.CultureInfo.InvariantCulture),
            m.Groups["paused"].Success, current, times.Count >= 2 ? TimeSpan.ParseExact(times[1], @"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture) : null)
        {
            Speed = texts.Select(Rate).FirstOrDefault(r => r is not null),
        };
    }

    /// <summary>
    /// Bandizip's progress window (measured 10-11, Bandizip 7 Standard in Chinese): "33% 正在压缩 X.zip - Bandizip (Standard)", and
    /// unpacking "14% 已解压 X.zip - Bandizip (Standard)" — "已解压" while it is still at it (its translation); for a moment just
    /// "正在压缩" / "已解压". Stop asks "是 / 否" in a window titled "Bandizip" (no pause first). Its texts: the overall %, "elapsed /
    /// left", the file's %, the file's times, then the file at hand. English and paused not seen (from Bandizip's own wording).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex Bandizip = new(
        @"^\s*(?<paused>已暂停|暂停|Paused)?\s*(?<p>\d{1,3})%\s+(?<job>正在压缩|正在解压|已解压|Compressing|Extracting)\s+(?<name>.+?)\s+-\s+Bandizip\b.*$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static ArchiveLook BandizipLook(System.Text.RegularExpressions.Match m, IReadOnlyList<string> texts, ArchiveLook? before)
    {
        var job = m.Groups["job"].Value is "正在压缩" or "Compressing" ? ArchiveJob.Zip : ArchiveJob.Unzip;
        var pairs = texts.Select((t, i) => (Text: t.Trim(), At: i))
            .Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.Text, @"^\d\d:\d\d:\d\d\s*/\s*\d\d:\d\d:\d\d$")).ToList();
        TimeSpan? left = pairs.Count > 0 && TimeSpan.TryParseExact(pairs[0].Text.Split('/')[1].Trim(), @"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : null;
        var current = pairs.Count >= 2 && pairs[1].At + 1 < texts.Count && texts[pairs[1].At + 1].Trim() is { Length: > 0 } f && !f.EndsWith('%') ? f : before?.Current;
        return new ArchiveLook(job, m.Groups["name"].Value.Trim(), int.Parse(m.Groups["p"].Value, System.Globalization.CultureInfo.InvariantCulture),
            m.Groups["paused"].Success, current, left);
    }

    /// <summary>"15 MB/s" in bytes a second, or null.</summary>
    private static double? Rate(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^(?<n>\d+(\.\d+)?)\s*(?<u>B|KB|MB|GB)/s$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return null;
        }

        var unit = m.Groups["u"].Value switch { "KB" => 1024d, "MB" => 1024d * 1024, "GB" => 1024d * 1024 * 1024, _ => 1d };
        return double.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) * unit;
    }

    // the options asked before a job starts (seen 10-11; English from the programs' own wording): going away as the job starts,
    // they are no question — WinRAR's, 7-Zip's "添加到压缩包", and its "解压 : C:\…\X.7z"
    private static readonly string[] SetupTitles = ["压缩文件名和参数", "解压路径和选项", "添加到压缩包", "Archive name and parameters", "Extraction path and options", "Add to Archive"];
    private static readonly string[] SetupHeads = ["解压 : ", "Extract : "];

    /// <summary>
    /// Whether a window of the archiver beside a job asks something (a file already there, a password, an error, "really cancel?"
    /// — 7-Zip's is titled "正在压缩"): any shown dialog but the options one (10-11: the replace question comes up at the very moment
    /// the job's window does, so "came later" missed it). A miss on the safe side only: no "done" card.
    /// </summary>
    public static bool Asks(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var t = title.Trim();
        return t.Length > 0 && !SetupTitles.Contains(t, StringComparer.OrdinalIgnoreCase) && !SetupHeads.Any(h => t.StartsWith(h, StringComparison.OrdinalIgnoreCase));
    }

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
