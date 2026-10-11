namespace IsleBar.Core.Ui;

/// <summary>
/// The break between a card's parts — "中转站 1个文件", "已复制 你好", "充电中 78%" (TODO ⑤ 全岛排版, user 10-11): the first part in the
/// main colour, the rest grey, <b>a fixed <see cref="Width"/> between them</b> — no " · " (a Western habit, sits unsteadily among Han
/// characters) and no space character (its width is the font's: 3.51 dip in 14 Pretendard).
/// Text carries the break as <see cref="Mark"/>; whoever draws it leaves <see cref="Width"/> there (WinUI: character spacing on the
/// part's last character). Translations write the break as " · " — <see cref="FromDots"/> turns it into the mark.
/// </summary>
public static class TextGap
{
    /// <summary>The break inside a text (an invisible control character no real text holds).</summary>
    public const char Mark = '\u001F';

    /// <summary>The gap at a break, in dip (user 10-11: 4; a space was 3.51).</summary>
    public const double Width = 4;

    /// <summary>How the translations write a break.</summary>
    public const string Dots = " · ";

    /// <summary>The mark as a string, for joining.</summary>
    public static readonly string Break = Mark.ToString();

    /// <summary>The parts joined with breaks; blank parts are left out.</summary>
    public static string Join(params string?[] parts) => string.Join(Mark, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    /// <summary>A translation's " · " (and a stray one at either end) as breaks.</summary>
    public static string FromDots(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return Join(text.Split(Dots));
    }

    /// <summary>The parts between the breaks.</summary>
    public static string[] Parts(string? text) => string.IsNullOrEmpty(text) ? [] : text.Split(Mark);

    /// <summary>The text with every break as one space — for places that show plain text (logs, accessibility).</summary>
    public static string Plain(string? text) => string.IsNullOrEmpty(text) ? string.Empty : text.Replace(Mark, ' ');

    /// <summary>The text without its breaks (to measure the letters; each break then adds <see cref="Width"/>).</summary>
    public static string Letters(string? text) => string.IsNullOrEmpty(text) ? string.Empty : text.Replace(Break, string.Empty, StringComparison.Ordinal);

    /// <summary>How many breaks the text holds.</summary>
    public static int Count(string? text) => string.IsNullOrEmpty(text) ? 0 : text.Count(c => c == Mark);

    /// <summary>
    /// The character spacing (thousandths of the font size, what WinUI's <c>CharacterSpacing</c> takes) that puts <see cref="Width"/>
    /// after one character: 286 at 14.
    /// </summary>
    public static int Spacing(double fontSize) => fontSize > 0 ? (int)Math.Round(Width / fontSize * 1000) : 0;
}
