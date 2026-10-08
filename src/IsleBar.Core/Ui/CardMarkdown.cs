using System.Text;
using System.Text.RegularExpressions;

namespace IsleBar.Core.Ui;

public enum MdKind
{
    Paragraph,
    Heading,
    Bullet,
    Number,
    Code,
    Table,
}

/// <summary>
/// One block of an answer as the lifted card draws it. <see cref="Text"/> is plain (marks taken out); a list item has its
/// <see cref="Marker"/> ("•" / "1.") and nesting <see cref="Level"/>; a table its <see cref="Rows"/> (the first is the header).
/// </summary>
public sealed record MdBlock(MdKind Kind, string Text, int Level = 0, string? Marker = null, IReadOnlyList<IReadOnlyList<string>>? Rows = null);

/// <summary>
/// An agent's answer (Markdown) laid out compactly on the lifted card (TODO 7.9 F2b, user 10-08, preview
/// https://claude.ai/artifact/XwYwmoxSbDXue1NH2kpXNL): no marks shown; headings and lines that are wholly bold become small grey
/// section names; bold inside a sentence becomes plain (the body is never bold); lists keep "•" / "1."; code blocks stay as they
/// are (monospace, scroll sideways); tables stay tables; links keep their words. Only what agents write is handled — not all of Markdown.
/// </summary>
public static class CardMarkdown
{
    private const int MaxLevel = 2;

    private static readonly Regex HeadingLine = new(@"^\s{0,3}#{1,6}\s+(.*?)\s*#*\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex BoldLine = new(@"^\s*\*\*([^*]+)\*\*\s*[:：]?\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex BulletLine = new(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex NumberLine = new(@"^(\s*)(\d{1,3})[.)]\s+(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Rule = new(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex TableSeparator = new(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Fence = new(@"^\s*(```|~~~)", RegexOptions.CultureInvariant);

    private static readonly Regex Link = new(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex Strong = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", RegexOptions.CultureInvariant);
    private static readonly Regex Emphasis = new(@"(?<![\w*])\*(?=\S)([^*\n]+?)(?<=\S)\*(?![\w*])", RegexOptions.CultureInvariant);
    private static readonly Regex Strike = new(@"~~(.+?)~~", RegexOptions.CultureInvariant);
    private static readonly Regex InlineCode = new(@"(`+)(.+?)\1", RegexOptions.CultureInvariant);
    private static readonly Regex Escaped = new(@"\\([\\`*_{}\[\]()#+\-.!|>~])", RegexOptions.CultureInvariant);
    private static readonly Regex Break = new(@"<br\s*/?>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<MdBlock> Parse(string? markdown)
    {
        var blocks = new List<MdBlock>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return blocks;
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var paragraph = new StringBuilder();
        MdBlock? item = null;   // the list item being read (a following indented line continues it)

        void EndParagraph()
        {
            if (paragraph.Length > 0)
            {
                blocks.Add(new MdBlock(MdKind.Paragraph, Inline(paragraph.ToString())));
                paragraph.Clear();
            }
        }

        void EndItem()
        {
            if (item is not null)
            {
                blocks.Add(item with { Text = Inline(item.Text) });
                item = null;
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                EndParagraph();
                EndItem();
                continue;
            }

            if (Fence.Match(line) is { Success: true } fence)
            {
                EndParagraph();
                EndItem();
                var indent = line.Length - line.TrimStart().Length;
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith(fence.Groups[1].Value, StringComparison.Ordinal); i++)
                {
                    var l = lines[i];
                    var drop = Math.Min(indent, l.Length - l.TrimStart().Length);   // a fence inside a list item is indented with it
                    code.Add(l[drop..].TrimEnd());
                }

                blocks.Add(new MdBlock(MdKind.Code, string.Join('\n', code)));
                continue;
            }

            if (line.TrimStart().StartsWith('|') && i + 1 < lines.Length && TableSeparator.IsMatch(lines[i + 1]))
            {
                EndParagraph();
                EndItem();
                var rows = new List<IReadOnlyList<string>> { Cells(line) };
                for (i += 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                {
                    rows.Add(Cells(lines[i]));
                }

                i--;
                var width = rows.Max(r => r.Count);
                blocks.Add(new MdBlock(MdKind.Table, string.Empty,
                    Rows: rows.Select(r => (IReadOnlyList<string>)[.. r, .. Enumerable.Repeat(string.Empty, width - r.Count)]).ToList()));
                continue;
            }

            if (Rule.IsMatch(line))
            {
                EndParagraph();
                EndItem();
                continue;
            }

            // "## 标题" or a line that is wholly bold ("**① 结果**") — a section name
            var heading = HeadingLine.Match(line);
            var boldLine = heading.Success ? Match.Empty : BoldLine.Match(line);
            if (heading.Success || boldLine.Success)
            {
                EndParagraph();
                EndItem();
                blocks.Add(new MdBlock(MdKind.Heading, Inline((heading.Success ? heading.Groups[1].Value : boldLine.Groups[1].Value).Trim())));
                continue;
            }

            if (BulletLine.Match(line) is { Success: true } bullet)
            {
                EndParagraph();
                EndItem();
                item = new MdBlock(MdKind.Bullet, bullet.Groups[2].Value, Level(bullet.Groups[1].Value), "•");
                continue;
            }

            if (NumberLine.Match(line) is { Success: true } number)
            {
                EndParagraph();
                EndItem();
                item = new MdBlock(MdKind.Number, number.Groups[3].Value, Level(number.Groups[1].Value), number.Groups[2].Value + ".");
                continue;
            }

            var text = line.Trim();
            if (text.StartsWith('>'))
            {
                text = text.TrimStart('>').Trim();
            }

            if (item is not null)
            {
                item = item with { Text = item.Text + "\n" + text };   // a list item's next line
            }
            else
            {
                paragraph.Append(paragraph.Length > 0 ? "\n" : string.Empty).Append(text);
            }
        }

        EndParagraph();
        EndItem();
        return blocks;
    }

    /// <summary>Marks taken out of a line: bold, italics, strikethrough, inline code, links (their words stay), escapes.</summary>
    public static string Inline(string text)
    {
        // inline code first, so the marks inside it stay as written ("`a*b*c`" keeps its stars)
        var codes = new List<string>();
        var s = InlineCode.Replace(text, m =>
        {
            codes.Add(m.Groups[2].Value.Trim());
            return $"\u0001{codes.Count - 1}\u0002";
        });
        s = Break.Replace(s, "\n");
        s = Link.Replace(s, "$1");
        s = Strong.Replace(s, "$2");
        s = Emphasis.Replace(s, "$1");
        s = Strike.Replace(s, "$1");
        s = Escaped.Replace(s, "$1");
        s = Regex.Replace(s, "\u0001(\\d+)\u0002", m => codes[int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]);
        return s.Trim();
    }

    /// <summary>Nesting from the indent (2–5 spaces = one level, more = two; a tab counts 4).</summary>
    private static int Level(string indent)
    {
        var width = indent.Sum(c => c == '	' ? 4 : 1);
        return width < 2 ? 0 : width < 6 ? 1 : MaxLevel;
    }

    private static List<string> Cells(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('|'))
        {
            t = t[1..];
        }

        if (t.EndsWith('|') && !t.EndsWith("\\|", StringComparison.Ordinal))
        {
            t = t[..^1];
        }

        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var k = 0; k < t.Length; k++)
        {
            if (t[k] == '\\' && k + 1 < t.Length && t[k + 1] == '|')
            {
                cell.Append('|');
                k++;
            }
            else if (t[k] == '|')
            {
                cells.Add(Inline(cell.ToString()));
                cell.Clear();
            }
            else
            {
                cell.Append(t[k]);
            }
        }

        cells.Add(Inline(cell.ToString()));
        return cells;
    }
}
