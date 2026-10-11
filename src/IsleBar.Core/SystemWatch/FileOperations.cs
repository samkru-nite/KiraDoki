using System.Globalization;
using System.Text.RegularExpressions;

namespace IsleBar.Core.SystemWatch;

/// <summary>What Explorer's operation does (from its progress window's sentence "正在将 273 个项目从 A 复制到 B").</summary>
public enum FileVerb
{
    Unknown,
    Copy,
    Move,
}

/// <summary>
/// One look at Explorer's progress window (class <c>OperationStatusWindow</c>): its title and every text UI Automation reads from it.
/// Only what the window says outright is taken (user 10-10, after two rounds of guessing outcomes wrong): what it does, how many,
/// the file, what's left — and whether it is asking something (a dialog in place of the progress: same names, a file in use, source
/// and target the same, …). What was answered there is never guessed. Logged case by case on Windows 11 (Chinese, 10-10); the English
/// forms are Windows' own wording, not yet seen in a log.
/// </summary>
/// <param name="Count">Items in the whole operation ("273 个项目").</param>
/// <param name="Left">"剩余项目: 222 (12.4 GB)" → 222 and the bytes. Gone while the window is folded to its short form.</param>
/// <param name="Speed">"速度: 1.55 GB/秒", bytes a second.</param>
/// <param name="Asking">A question instead of the progress (no pause / resume button in it).</param>
public sealed record FileOpLook(
    FileVerb Verb,
    int? Count,
    string? Name,
    int? Left,
    long? LeftBytes,
    double? Speed,
    long? Percent,
    bool Cancelling,
    bool Asking);

/// <summary>Reads <see cref="FileOpLook"/> from the window's words (pure, tested).</summary>
public static partial class FileOpReader
{
    public static FileOpLook Read(string title, IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        title ??= string.Empty;
        var all = string.Join("\n", texts);

        var verb = FileVerb.Unknown;
        int? count = null;
        if (SentenceZh().Match(all) is { Success: true } zh)
        {
            count = Number(zh.Groups[1].Value);
            verb = zh.Groups[2].Value == "移动" ? FileVerb.Move : FileVerb.Copy;
        }
        else if (SentenceEn().Match(all) is { Success: true } en)
        {
            verb = en.Groups[1].Value.StartsWith("Mov", StringComparison.OrdinalIgnoreCase) ? FileVerb.Move : FileVerb.Copy;
            count = Number(en.Groups[2].Value);
        }

        // the file being handled: the text after the first "label:" ("名称:" / "Name:") — unless that is empty and the next label
        // follows at once ("名称: ¦ 剩余时间:", user 10-10: "已移动 剩余时间:")
        string? name = null;
        var list = texts.ToList();
        var label = list.FindIndex(IsLabel);
        if (label >= 0 && label + 1 < texts.Count && !IsLabel(texts[label + 1]))
        {
            name = texts[label + 1];
        }

        if (NamedZh().Match(all) is { Success: true } named)
        {
            name = named.Groups[1].Value;
        }

        int? left = null;
        long? leftBytes = null;
        var leftAt = list.FindIndex(t => t.StartsWith("剩余项目", StringComparison.Ordinal)
                                         || t.StartsWith("Items remaining", StringComparison.OrdinalIgnoreCase));
        if (leftAt >= 0 && leftAt + 1 < texts.Count && LeftPattern().Match(texts[leftAt + 1]) is { Success: true } l)
        {
            left = Number(l.Groups[1].Value);
            leftBytes = Bytes(l.Groups[2].Value, l.Groups[3].Value);
        }

        double? speed = SpeedPattern().Match(all) is { Success: true } s ? Bytes(s.Groups[1].Value, s.Groups[2].Value) : null;
        long? percent = PercentPattern().Match(title) is { Success: true } p ? Math.Clamp(long.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture), 0, 100) : null;

        // the progress view always has its pause / resume button; every question seen (replace or skip, keep which files, file in use,
        // source and target the same) has none. An empty read (the window closing) is neither.
        var asking = texts.Count > 0 && !texts.Any(t => t is "暂停操作" or "继续操作"
            || t.Equals("Pause", StringComparison.OrdinalIgnoreCase) || t.Equals("Resume", StringComparison.OrdinalIgnoreCase));

        return new FileOpLook(
            verb,
            count,
            name,
            left,
            leftBytes,
            speed,
            percent,
            all.Contains("正在取消", StringComparison.Ordinal) || all.Contains("Cancelling", StringComparison.OrdinalIgnoreCase),
            asking);
    }

    private static bool IsLabel(string text) => text.EndsWith(':') || text.EndsWith('：');

    private static int Number(string digits) => int.Parse(digits.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);

    /// <summary>"12.4" "GB" → bytes. "0 字节" / "53 bytes" → as they are.</summary>
    internal static long Bytes(string amount, string unit)
    {
        var value = double.Parse(amount.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
        var times = unit.ToUpperInvariant() switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024,
            "GB" => 1024d * 1024 * 1024,
            "TB" => 1024d * 1024 * 1024 * 1024,
            _ => 1d,
        };
        return (long)(value * times);
    }

    [GeneratedRegex(@"正在将\s*([\d,]+)\s*个项目从.*?(复制|移动)到")]
    private static partial Regex SentenceZh();

    [GeneratedRegex(@"(Copying|Moving)\s+([\d,]+)\s+items?", RegexOptions.IgnoreCase)]
    private static partial Regex SentenceEn();

    [GeneratedRegex(@"目标已包含一个名为[“""](.+?)[”""]的文件")]
    private static partial Regex NamedZh();

    [GeneratedRegex(@"^([\d,]+)\s*\(([\d.,]+)\s*(字节|bytes|KB|MB|GB|TB)\)", RegexOptions.IgnoreCase)]
    private static partial Regex LeftPattern();

    [GeneratedRegex(@"(?:速度|Speed):\s*([\d.,]+)\s*(KB|MB|GB|TB|字节|bytes)/", RegexOptions.IgnoreCase)]
    private static partial Regex SpeedPattern();

    [GeneratedRegex(@"(\d{1,3})\s*%")]
    private static partial Regex PercentPattern();
}

/// <summary>
/// Follows one operation from its window's looks (4 a second) and tells, when the window closes, whether it surely went through
/// (user 10-10: only say what is sure, else nothing): no question ever came up, it wasn't cancelled, and the last look had nothing
/// left — or what was left could have gone before it closed (a copy finishing between looks closes on "4 left, 201 MB at
/// 1.23 GB/s"). Anything else — a question answered, a cancel, a close too early — ends with no card at all.
/// </summary>
public sealed class FileOpTally
{
    private static readonly TimeSpan FinishSlack = TimeSpan.FromSeconds(1.5);
    private const double SaneSpeed = 10d * 1024 * 1024 * 1024;   // above 10 GB/s it's Explorer's nonsense (52.9 GB/s while skipping)

    private DateTimeOffset _lastSeen;
    private int? _left;
    private long? _leftBytes;
    private double? _speed;
    private long? _percent;

    public FileVerb Verb { get; private set; }

    public int? Count { get; private set; }

    public string? Name { get; private set; }

    /// <summary>It is asking something now (the card waits for you).</summary>
    public bool Asking { get; private set; }

    /// <summary>A question came up at some point: the outcome is the person's answer, which isn't shown.</summary>
    public bool Asked { get; private set; }

    public bool Cancelled { get; private set; }

    public void See(FileOpLook look, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(look);
        _lastSeen = now;
        if (look.Verb != FileVerb.Unknown)
        {
            Verb = look.Verb;
        }

        Count = look.Count ?? Count;
        Name = look.Name ?? Name;
        Cancelled |= look.Cancelling;
        Asking = look.Asking;
        Asked |= look.Asking;
        if (look.Asking)
        {
            return;   // a question shows no progress
        }

        _percent = look.Percent ?? _percent;
        if (look.Speed is { } speed and > 0 and < SaneSpeed)
        {
            _speed = speed;
        }

        if (look.Left is { } left)
        {
            _left = left;
            _leftBytes = look.LeftBytes;
        }
    }

    /// <summary>The window closed: true only when it surely went through.</summary>
    public bool Finished(DateTimeOffset now)
    {
        if (Asked || Cancelled)
        {
            return false;
        }

        if (_left is not { } left)
        {
            return _percent >= 99;   // folded to the short form: only the title's percentage
        }

        return left == 0 || (_speed is { } speed && _leftBytes is { } bytes
                             && TimeSpan.FromSeconds(bytes / speed) <= now - _lastSeen + FinishSlack);
    }
}

/// <summary>What Explorer is doing, as the pill says it.</summary>
public enum FileJob
{
    Copy,
    Move,
    Unzip,
    Zip,

    /// <summary>The window said neither copy nor move — deleting uses the same window (user 10-11: "进行中", and no "done").</summary>
    Unknown,
}

/// <summary>
/// The file transfer card's words (TODO ④, user 10-10): the state first in the main colour, then the file grey —
/// "复制中 DSC02580.ARW +271", "已复制 DSC02580.ARW +271".
/// </summary>
/// <param name="Words">The state, main colour.</param>
/// <param name="Detail">The file's name (or "273 个" before one is known), grey.</param>
/// <param name="More">"+N" after a name (0 = none).</param>
public sealed record FileOpText(string Words, string? Detail, int More)
{
    public static FileJob JobOf(FileVerb verb, bool unzip) => unzip ? FileJob.Unzip : verb switch
    {
        FileVerb.Move => FileJob.Move,
        FileVerb.Copy => FileJob.Copy,
        _ => FileJob.Unknown,
    };

    /// <summary>While it runs: "复制中 DSC02580.ARW +271".</summary>
    public static FileOpText Running(FileJob job, int? count, string? name, Localization.LanguageStrings text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = job switch
        {
            FileJob.Move => text.FileMove,
            FileJob.Unzip => text.FileUnzip,
            FileJob.Zip => text.FileZip,
            FileJob.Unknown => text.CardRunning,
            _ => text.FileCopy,
        };
        return Named(words, count, name, text);
    }

    /// <summary>Surely through: "已复制 DSC02580.ARW +271".</summary>
    public static FileOpText Done(FileJob job, int? count, string? name, Localization.LanguageStrings text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = job switch
        {
            FileJob.Move => text.FileMoved,
            FileJob.Unzip => text.FileUnzipped,
            FileJob.Zip => text.FileZipped,
            _ => text.FileCopied,
        };
        return Named(words, count, name, text);
    }

    private static FileOpText Named(string words, int? count, string? name, Localization.LanguageStrings text)
        => name is not null ? new(words, name, Math.Max(0, (count ?? 1) - 1))
            : new(words, count is { } c ? string.Format(CultureInfo.InvariantCulture, text.FileItems, c) : null, 0);
}
