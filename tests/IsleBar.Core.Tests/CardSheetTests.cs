using IsleBar.Core.Island;
using IsleBar.Core.Localization;
using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class CardSheetTests
{
    private static readonly LanguageStrings Zh = LanguageCatalog.For("zh");
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 6, 32, 0, TimeSpan.Zero);

    private static Card Card(CardType type, ActivityState state) => new("k", type, type != CardType.Status, state);

    private static double Unix(DateTimeOffset at) => at.ToUnixTimeSeconds();

    [Fact]
    public void An_agent_done_card_names_the_maker_the_state_and_the_chat()
    {
        var sheet = CardSheets.Build(Card(CardType.Inform, new ActivityState
        {
            RawKind = ActivityState.KindAgentDone, Name = "修复滚动条", UpdatedAt = Now.AddMinutes(-2),
        }), Now, Zh);

        Assert.Equal("Claude Code", sheet.Source);
        Assert.Equal("完成", sheet.State);
        Assert.Equal(SheetTone.Ok, sheet.Tone);
        Assert.Equal("2 分钟前", sheet.Time);
        Assert.Equal("修复滚动条", sheet.Title);
        Assert.Equal([SheetAction.OpenChat, SheetAction.Close], sheet.Buttons.Select(b => b.Action));
        Assert.True(sheet.Adaptive);
    }

    [Fact]
    public void An_agent_at_work_grows_if_it_must_with_no_close_and_counts_its_minutes()
    {
        var sheet = CardSheets.Build(Card(CardType.Status, new ActivityState
        {
            RawKind = ActivityState.KindAgentWorking, Agent = "codex", Name = "重写下载卡片", T0 = Unix(Now.AddMinutes(-3)),
        }), Now, Zh);

        Assert.Equal("Codex", sheet.Source);
        Assert.Equal(SheetTone.Plain, sheet.Tone);   // nobody is waiting on it
        Assert.Equal("已运行 3 分钟", sheet.Time);
        Assert.Equal([SheetAction.OpenChat], sheet.Buttons.Select(b => b.Action));
        Assert.True(sheet.Adaptive);   // user 10-09: what it says shouldn't be cut
    }

    [Fact]
    public void An_agent_at_work_shows_what_it_said_this_turn_with_its_step_under_it()
    {
        var working = Card(CardType.Status, new ActivityState { RawKind = ActivityState.KindAgentWorking, Name = "继续 F2b", T0 = Unix(Now) });

        var talking = CardSheets.Build(working, Now, Zh, new AgentTail("上一轮的回答", "Bash · 构建", "找到了对应关系，接下来确认。"));
        Assert.True(talking.Markdown);
        Assert.Equal(["找到了对应关系，接下来确认。"], talking.Body);
        Assert.Equal("Bash · 构建", talking.Footnote);

        var silent = CardSheets.Build(working, Now, Zh, new AgentTail("上一轮的回答", "Bash · 构建"));
        Assert.False(silent.Markdown);   // nothing said yet this turn: the step alone, never the turn before's answer
        Assert.Equal(["Bash · 构建"], silent.Body);
        Assert.Null(silent.Footnote);
    }

    [Fact]
    public void Time_reads_just_now_then_minutes_then_the_clock()
    {
        Assert.Equal("刚刚", CardSheets.Ago(Now.AddSeconds(-20), Now, Zh));
        Assert.Equal("59 分钟前", CardSheets.Ago(Now.AddMinutes(-59), Now, Zh));
        Assert.Equal(Now.AddMinutes(-61).ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), CardSheets.Ago(Now.AddMinutes(-61), Now, Zh));
    }

    [Fact]
    public void A_running_timer_shows_the_time_left_and_its_start_and_end_over_the_bar()
    {
        var start = Now.AddMinutes(-6).AddSeconds(-18);
        var sheet = CardSheets.Build(Card(CardType.Status, TimerParser.ToActivity(TimeSpan.FromMinutes(25), start)), Now, Zh);

        Assert.Equal(SheetLayout.Number, sheet.Layout);
        Assert.Equal("18:42", sheet.Number);
        Assert.Equal("剩余", sheet.NumberCaption);
        Assert.Equal("进行中", sheet.State);
        Assert.Equal("25 分钟", sheet.Time);
        Assert.StartsWith(start.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), sheet.Progress!.Left, StringComparison.Ordinal);
        Assert.EndsWith("结束", sheet.Progress.Right, StringComparison.Ordinal);
        Assert.Equal([SheetAction.TimerPause, SheetAction.TimerCancel], sheet.Buttons.Select(b => b.Action));
    }

    [Fact]
    public void A_finished_timer_keeps_the_same_layout_full_and_green()
    {
        var sheet = CardSheets.Build(Card(CardType.Inform, TimerParser.ToActivity(TimeSpan.FromMinutes(25), Now.AddMinutes(-26))), Now, Zh);

        Assert.Equal("25:00", sheet.Number);   // the whole length, not 0:00
        Assert.Null(sheet.NumberCaption);
        Assert.Equal("已结束", sheet.State);
        Assert.Equal(SheetTone.Ok, sheet.Tone);
        Assert.Equal(1, sheet.Progress!.Fraction);
        Assert.Equal(SheetTone.Ok, sheet.Progress.Tone);
        Assert.Equal([SheetAction.TimerAgain, SheetAction.Close], sheet.Buttons.Select(b => b.Action));
    }

    [Fact]
    public void A_browser_download_has_no_bar_and_says_how_much_came()
    {
        var sheet = CardSheets.Build(Card(CardType.Status, new ActivityState
        {
            RawKind = ActivityState.KindTransfer, Title = "Download → PC", Name = "report.pdf", Stage = "download",
            Done = 13 * 1024 * 1024, Speed = 3 * 1024 * 1024, State = "run",
        }), Now, Zh);

        Assert.Null(sheet.Progress);
        Assert.Equal("下载", sheet.Source);
        Assert.Equal(["已下载 13.0 MB · 3.0 MB/s"], sheet.Body);
        Assert.Equal([SheetAction.OpenFolder, SheetAction.Hide], sheet.Buttons.Select(b => b.Action));
    }

    [Fact]
    public void Music_with_a_known_length_gets_a_bar_and_the_smaller_cover()
    {
        var sheet = CardSheets.Build(Card(CardType.Status, new ActivityState
        {
            RawKind = ActivityState.KindMusic, Name = "夜空中最亮的星", Msg = "逃跑计划", Stage = "playing", Total = 252_000, Done = 108_000,
        }), Now, Zh);

        Assert.Equal(SheetLayout.Media, sheet.Layout);
        Assert.Equal("1:48", sheet.Progress!.Left);
        Assert.Equal("4:12", sheet.Progress.Right);
        Assert.Equal(56, CardSheets.CoverSize(sheet.Progress is not null));
        Assert.Equal(58, CardSheets.MiddleHeight(sheet, 200));   // 200 − 32 padding − 16 source − 12 − 38 progress − 44 buttons
    }

    [Fact]
    public void A_temporary_card_has_no_buttons_so_its_middle_runs_to_the_bottom()
    {
        var sheet = CardSheets.Build(Card(CardType.Temporary, new ActivityState
        {
            RawKind = ActivityState.KindNotice, Title = "已复制", Msg = "周五评审改到下午三点", T0 = Unix(Now),
        }), Now, Zh);

        Assert.Empty(sheet.Buttons);
        Assert.Equal(200 - 16 - 16 - 12 - 16, CardSheets.MiddleHeight(sheet, 200));
        Assert.Equal(96, CardSheets.MiddleHeight(sheet with { Buttons = [new(SheetAction.Close, "关闭")] }, 200));
    }

    [Fact]
    public void A_system_notification_takes_its_app_as_the_source()
    {
        var sheet = CardSheets.Build(Card(CardType.Temporary, new ActivityState
        {
            RawKind = ActivityState.KindNotice, Stage = "expand", Name = "邮件", Title = "张伟", Msg = "周五的评审改到下午三点了", T0 = Unix(Now),
        }), Now, Zh);

        Assert.Equal("邮件", sheet.Source);
        Assert.Null(sheet.State);
        Assert.Equal("张伟", sheet.Title);
        Assert.Equal(["周五的评审改到下午三点了"], sheet.Body);
    }

    [Fact]
    public void The_mic_card_lists_every_program_using_it()
    {
        ActivityState Mic(params string[] apps) => new()
        {
            RawKind = ActivityState.KindNotice, Stage = "privacy", Glyph = IsleBar.Core.SystemWatch.NoticeGlyphs.Microphone,
            Name = apps[0], Apps = apps, UpdatedAt = Now,
        };

        var one = CardSheets.Build(Card(CardType.Status, Mic("微信")), Now, Zh);
        Assert.Equal("微信", one.Title);
        Assert.False(one.Markdown);

        var two = CardSheets.Build(Card(CardType.Status, Mic("腾讯会议", "微信")), Now, Zh);
        Assert.Null(two.Title);
        Assert.True(two.Markdown);
        Assert.Equal(["- 腾讯会议\n- 微信"], two.Body);
    }

    [Fact]
    public void A_download_source_comes_from_its_temp_file()
    {
        Assert.Equal("Firefox", DownloadSources.Of(@"C:\d\a.zip.part", []));
        Assert.Equal("百度网盘", DownloadSources.Of(@"C:\d\a.zip.baiduyun.p.downloading", ["Edge"]));
        Assert.Equal("Edge", DownloadSources.Of(@"C:\d\a.zip.crdownload", ["Edge"]));
        Assert.Null(DownloadSources.Of(@"C:\d\a.zip.crdownload", ["Edge", "Chrome"]));   // two Chromium browsers: can't tell
    }

    [Fact]
    public void Several_downloads_are_listed_by_source()
    {
        var sheet = CardSheets.Build(Card(CardType.Status, new ActivityState
        {
            RawKind = ActivityState.KindTransfer, Title = "Download → PC", Name = "report.pdf", Stage = "download", State = "run",
            Done = 1, MoreCount = 2,
            Downloads =
            [
                new DownloadLine("Edge", "report.pdf", 13 * 1024 * 1024, 2 * 1024 * 1024, false),
                new DownloadLine("Edge", null, 1024 * 1024, 1024 * 1024, false),
                new DownloadLine("百度网盘", "my_*draft*.zip", 5 * 1024 * 1024, null, true),
            ],
        }), Now, Zh);

        Assert.True(sheet.Markdown);
        Assert.Null(sheet.Title);
        var blocks = CardMarkdown.Parse(sheet.Body[0]);
        Assert.Equal(
            [(MdKind.Heading, "Edge · 3.0 MB/s"), (MdKind.Bullet, "report.pdf · 已下载 13.0 MB"), (MdKind.Bullet, "已下载 1.0 MB"),
             (MdKind.Heading, "百度网盘 · 已暂停"), (MdKind.Bullet, "my_*draft*.zip · 已下载 5.0 MB")],
            blocks.Select(b => (b.Kind, b.Text)));
    }

    [Fact]
    public void Music_names_its_player()
    {
        var sheet = CardSheets.Build(Card(CardType.Status, new ActivityState
        {
            RawKind = ActivityState.KindMusic, Name = "晴天", Stage = "playing", AppName = "网易云音乐",
        }), Now, Zh);

        Assert.Equal("网易云音乐", sheet.Source);
    }
}
