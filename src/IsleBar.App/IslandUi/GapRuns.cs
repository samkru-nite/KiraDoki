using IsleBar.Core.Ui;
using Microsoft.UI.Xaml.Documents;

namespace IsleBar.App.IslandUi;

/// <summary>
/// Draws a text with <see cref="TextGap"/> breaks: each part its own runs (<paramref name="part"/> styles them — part 0 main colour,
/// the rest grey, as the caller wants), and at every break a fixed <see cref="TextGap.Width"/>. The gap is character spacing on the
/// part's last character (WinUI adds it after each character of a run) — not a space, whose width is the font's.
/// </summary>
internal static class GapRuns
{
    /// <summary>Adds <paramref name="text"/> to <paramref name="inlines"/>. <paramref name="part"/>(index, words) makes a styled run.</summary>
    public static void Add(InlineCollection inlines, string text, double fontSize, Func<int, string, Run> part)
    {
        var parts = TextGap.Parts(text);
        for (var i = 0; i < parts.Length; i++)
        {
            var words = parts[i];
            if (i == parts.Length - 1 || words.Length == 0)
            {
                if (words.Length > 0)
                {
                    inlines.Add(part(i, words));
                }

                continue;
            }

            // the last character (a whole text element: an emoji stays one) carries the gap
            var cut = LastElement(words);
            if (cut > 0)
            {
                inlines.Add(part(i, words[..cut]));
            }

            var last = part(i, words[cut..]);
            last.CharacterSpacing = TextGap.Spacing(fontSize);
            inlines.Add(last);
        }
    }

    /// <summary>
    /// Puts plain <paramref name="text"/> in <paramref name="block"/>: as its <c>Text</c> when it has no break, else as runs with the
    /// gaps (<see cref="Spread"/>).
    /// </summary>
    public static void Fill(Microsoft.UI.Xaml.Controls.TextBlock block, string text)
    {
        if (text.IndexOf(TextGap.Mark, StringComparison.Ordinal) < 0)
        {
            block.Text = text;
            return;
        }

        block.Inlines.Clear();
        block.Inlines.Add(new Run { Text = text });
        Spread(block.Inlines, block.FontSize);
    }

    /// <summary>
    /// Runs already laid out (a title split into Han and Latin runs, a state word and its time) whose text holds breaks: each break
    /// goes, and the character before it — in its run, or the run before — gets the gap. Styles set on a run are kept on its pieces.
    /// </summary>
    public static void Spread(InlineCollection inlines, double fontSize)
    {
        var spacing = TextGap.Spacing(fontSize);
        var i = 0;
        while (i < inlines.Count)
        {
            if (inlines[i] is not Run run || run.Text.IndexOf(TextGap.Mark, StringComparison.Ordinal) is var at && at < 0)
            {
                i++;
                continue;
            }

            var before = run.Text[..at];
            var after = run.Text[(at + 1)..];
            inlines.RemoveAt(i);
            var next = i;
            if (before.Length > 0)
            {
                var cut = LastElement(before);
                if (cut > 0)
                {
                    inlines.Insert(next++, Copy(run, before[..cut]));
                }

                var gap = Copy(run, before[cut..]);
                gap.CharacterSpacing = spacing;
                inlines.Insert(next++, gap);
            }
            else if (i > 0 && inlines[i - 1] is Run prev && prev.Text.Length > 0)
            {
                var cut = LastElement(prev.Text);
                if (cut > 0)
                {
                    var gap = Copy(prev, prev.Text[cut..]);
                    prev.Text = prev.Text[..cut];
                    gap.CharacterSpacing = spacing;
                    inlines.Insert(next++, gap);
                }
                else
                {
                    prev.CharacterSpacing = spacing;
                }
            }

            if (after.Length > 0)
            {
                inlines.Insert(next, Copy(run, after));   // looked at again: it may hold another break
            }

            i = next;
        }
    }

    /// <summary>A run with <paramref name="text"/> and the styles <paramref name="run"/> sets itself (unset ones stay inherited).</summary>
    private static Run Copy(Run run, string text)
    {
        var copy = new Run { Text = text };
        foreach (var property in new[] { TextElement.FontFamilyProperty, TextElement.FontWeightProperty, TextElement.ForegroundProperty, TextElement.FontSizeProperty })
        {
            var value = run.ReadLocalValue(property);
            if (value != Microsoft.UI.Xaml.DependencyProperty.UnsetValue)
            {
                copy.SetValue(property, value);
            }
        }

        return copy;
    }

    private static int LastElement(string words)
    {
        var starts = System.Globalization.StringInfo.ParseCombiningCharacters(words);
        return starts.Length == 0 ? 0 : starts[^1];
    }
}
