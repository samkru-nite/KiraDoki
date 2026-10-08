using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public class CardMarkdownTests
{
    [Fact]
    public void Marks_come_out_and_bold_inside_a_sentence_becomes_plain()
    {
        Assert.Equal("已提交（4eaa603），见 TODO 和 CardSheet.cs。",
            CardMarkdown.Inline("已提交（`4eaa603`），见 **TODO** 和 [CardSheet.cs](src/CardSheet.cs)。"));
        Assert.Equal("snake_case_name stays, a*b in code stays", CardMarkdown.Inline("snake_case_name stays, `a*b` in code stays"));
    }

    [Fact]
    public void A_wholly_bold_line_and_a_hash_heading_are_section_names()
    {
        var blocks = CardMarkdown.Parse("**① AI 卡片的内容：能读到**\n正文一句。\n\n## 下一步\n- 一\n- 二 **重点**");

        Assert.Equal(
            [(MdKind.Heading, "① AI 卡片的内容：能读到"), (MdKind.Paragraph, "正文一句。"), (MdKind.Heading, "下一步"), (MdKind.Bullet, "一"), (MdKind.Bullet, "二 重点")],
            blocks.Select(b => (b.Kind, b.Text)));
    }

    [Fact]
    public void Numbered_and_nested_items_keep_their_marker_and_level()
    {
        var blocks = CardMarkdown.Parse("1. 第一\n   - 里面\n2. 第二\n   接着第二");

        Assert.Equal([("1.", 0), ("•", 1), ("2.", 0)], blocks.Select(b => (b.Marker, b.Level)));
        Assert.Equal("第二\n接着第二", blocks[2].Text);
    }

    [Fact]
    public void Code_keeps_its_indents_and_marks()
    {
        var blocks = CardMarkdown.Parse("看这里：\n```python\ndef f(x):\n    return x**2\n```\n完");

        Assert.Equal(MdKind.Code, blocks[1].Kind);
        Assert.Equal("def f(x):\n    return x**2", blocks[1].Text);
        Assert.Equal("完", blocks[2].Text);
    }

    [Fact]
    public void A_table_is_rows_of_cells_with_the_header_first()
    {
        var blocks = CardMarkdown.Parse("| Markdown | 卡片上 |\n|---|:---:|\n| 段落 | 空 6 |\n| `代码` | 等宽 \\| 灰 |\n\n后面");

        var table = Assert.Single(blocks, b => b.Kind == MdKind.Table);
        Assert.Equal([["Markdown", "卡片上"], ["段落", "空 6"], ["代码", "等宽 | 灰"]], table.Rows!.Select(r => r.ToArray()).ToArray());
        Assert.Equal(MdKind.Paragraph, blocks[^1].Kind);
    }
}
