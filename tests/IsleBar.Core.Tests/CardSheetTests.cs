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
    public void The_transit_station_lists_its_files_marks_the_gone_ones_and_can_be_cleared()
    {
        var sheet = CardSheets.Build(Card(CardType.Status, new ActivityState
        {
            RawKind = ActivityState.KindNotice, Glyph = SystemWatch.NoticeGlyphs.Transit, Title = "中转站", Msg = "2 个文件",
            Files = [@"C:\a\报告.docx", @"D:\照片"], Gone = [@"d:\照片"],
        }), Now, Zh);

        Assert.Equal("中转站", sheet.Source);
        Assert.Null(sheet.State);
        Assert.Equal(SheetLayout.Files, sheet.Layout);
        Assert.Equal([new SheetFile(@"C:\a\报告.docx", "报告.docx", false), new SheetFile(@"D:\照片", "照片", true)], sheet.Files);
        Assert.Equal([SheetAction.Clear], sheet.Buttons.Select(b => b.Action));
    }

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
        Assert.Equal("2分钟前", sheet.Time);
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
        Assert.Equal("已运行3分钟", sheet.Time);
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
        Assert.Equal("59分钟前", CardSheets.Ago(Now.AddMinutes(-59), Now, Zh));
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
        Assert.Equal("25分钟", sheet.Time);
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
    public void A_browser_download_flows_and_says_how_much_came_how_fast_and_how_long()
    {
        var sheet = CardSheets.Build(Card(CardType.Status, new ActivityState
        {
            RawKind = ActivityState.KindTransfer, Title = "Download → PC", Name = "report.pdf", Stage = "download",
            Done = 13 * 1024 * 1024, Speed = 3 * 1024 * 1024, State = "run", AppName = "Microsoft Edge", UpdatedAt = Now.AddMinutes(-2),
        }), Now, Zh);

        Assert.Equal("Microsoft Edge", sheet.Source);   // the program doing it (user 10-11)
        Assert.Equal("下载中", sheet.State);
        Assert.Null(sheet.Time);   // no "2分钟前" on the right any more
        Assert.Equal("report.pdf", sheet.Title);
        Assert.Empty(sheet.Body);
        Assert.True(sheet.Progress!.Flowing);
        Assert.Equal("已下载 13.0 MB\u001F3.0 MB/s", sheet.Progress.Left);
        Assert.Equal("用时2分钟", sheet.Progress.Right);
        Assert.Equal([SheetAction.OpenFolder, SheetAction.Hide], sheet.Buttons.Select(b => b.Action));
    }

    [Fact]
    public void A_packer_with_a_percentage_says_its_file_time_left_and_speed()
    {
        var sheet = CardSheets.Build(Card(CardType.Status, new ActivityState
        {
            RawKind = ActivityState.KindTransfer, Title = "Files", Name = "照片.rar", Stage = "copy", Words = "压缩中", State = "run",
            Done = 40, Total = 100, Speed = 48.2 * 1024 * 1024, Eta = TimeSpan.FromSeconds(130), Note = "DSC02582.ARW", AppName = "WinRAR",
        }), Now, Zh);

        Assert.Equal("WinRAR", sheet.Source);
        Assert.Equal("压缩中", sheet.State);
        Assert.Equal(["DSC02582.ARW"], sheet.Body);
        Assert.False(sheet.Progress!.Flowing);
        Assert.Equal("剩余2分钟\u001F48.2 MB/s", sheet.Progress.Left);
        Assert.Equal("40%", sheet.Progress.Right);
    }

    [Fact]
    public void Time_left_waits_till_it_has_settled()
    {
        var eta = new SteadyEta();
        Assert.Null(eta.See(TimeSpan.FromMinutes(9), Now));
        Assert.Null(eta.See(TimeSpan.FromMinutes(2), Now.AddSeconds(2)));
        Assert.Equal(TimeSpan.FromMinutes(2), eta.See(TimeSpan.FromMinutes(2), Now.AddSeconds(3)));
        Assert.Null(eta.See(null, Now.AddSeconds(4)));   // gone a moment: settles anew
        Assert.Null(eta.See(TimeSpan.FromMinutes(1), Now.AddSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(50), SteadyEta.From(500, 10));
    }

    [Fact]
    public void Lengths_read_in_seconds_minutes_or_hours()
    {
        Assert.Equal("45秒", CardSheets.Span(TimeSpan.FromSeconds(45), Zh));
        Assert.Equal("2分钟", CardSheets.Span(TimeSpan.FromSeconds(150), Zh));
        Assert.Equal("1小时5分钟", CardSheets.Span(TimeSpan.FromMinutes(65), Zh));
    }

    [Fact]
    public void A_finished_transfer_says_its_size_and_time_and_shows_the_file()
    {
        var sheet = CardSheets.Build(Card(CardType.Temporary, new ActivityState
        {
            RawKind = ActivityState.KindTransfer, Title = "Files", Name = "照片.rar", Stage = "copy", Words = "已压缩", State = "done",
            Note = "2.40 GB", Took = TimeSpan.FromMinutes(4), Open = @"C:\a\照片.rar",
        }), Now, Zh);

        Assert.Equal(SheetTone.Ok, sheet.Tone);
        Assert.Equal(["2.40 GB\u001F用时4分钟"], sheet.Body);
        Assert.Null(sheet.Progress);
        Assert.Equal([(SheetAction.OpenFolder, "在文件夹中显示")], sheet.Buttons.Select(b => (b.Action, b.Label)));
    }

    [Fact]
    public void A_game_steam_installed_can_be_played()
    {
        var done = new ActivityState { RawKind = ActivityState.KindTransfer, Title = "Files", Name = "Portal 2", Stage = "copy", Words = "已安装", State = "done" };
        var sheet = CardSheets.Build(new Card("steam:620", CardType.Temporary, true, done), Now, Zh);
        Assert.Equal([(SheetAction.Play, "开始游戏")], sheet.Buttons.Select(b => (b.Action, b.Label)));
        Assert.Equal(620, CardSheets.SteamGame("steam:620"));
        Assert.Null(CardSheets.SteamGame("steam:busy"));   // Steam's fallback names no game
    }

    [Fact]
    public void A_stuck_printer_names_itself_and_its_problem_and_stands_where_it_stopped()
    {
        var notice = SystemWatch.SystemNotice.Make("缺纸", SystemWatch.NoticeGlyphs.Printer, Now, msg: "报告.pdf", urgent: true);
        notice.AppName = "HP LaserJet";
        notice.Note = "已打印3页，共12页";
        notice.Done = 3;
        notice.Total = 12;
        var sheet = CardSheets.Build(Card(CardType.Pending, notice), Now, Zh);

        Assert.Equal("HP LaserJet", sheet.Source);
        Assert.Equal(("缺纸", SheetTone.Waiting), (sheet.State, sheet.Tone));
        Assert.Equal("报告.pdf", sheet.Title);
        Assert.Equal(["已打印3页，共12页"], sheet.Body);
        Assert.Equal((0.25, "25%", SheetTone.Waiting), (sheet.Progress!.Fraction, sheet.Progress.Right, sheet.Progress.Tone));
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
        Assert.Null(sheet.Title);
        Assert.Empty(sheet.Body);
        var only = Assert.Single(sheet.Groups);
        Assert.Equal("张伟", only.Name);
        Assert.Equal(["周五的评审改到下午三点了"], only.Lines);
    }

    [Fact]
    public void Merged_notifications_are_grouped_by_sender_the_last_writer_first()
    {
        ActivityState Msg(string who, string what) => new() { RawKind = ActivityState.KindNotice, Stage = "expand", Name = "QQ", Title = who, Msg = what, T0 = Unix(Now) };
        var newest = Msg("秦鹏宇", "明天的实验报告交了没");
        newest.Earlier = [Msg("六神花露水", "晚上吃什么"), Msg("秦鹏宇", "在吗"), Msg("六神花露水", "[动画表情]"), Msg("六神花露水", "[动画表情]")];

        var groups = CardSheets.Build(Card(CardType.Temporary, newest), Now, Zh).Groups;

        Assert.Equal(["秦鹏宇", "六神花露水"], groups.Select(g => g.Name));
        Assert.Equal(["在吗", "明天的实验报告交了没"], groups[0].Lines);   // oldest first, as in the chat
        Assert.Equal(["晚上吃什么", "[动画表情]", "[动画表情]"], groups[1].Lines);   // the same sticker twice is two messages (10-11: 4 showed as 2)
    }

    [Fact]
    public void A_notification_with_nothing_to_open_has_no_open_button()
    {
        ActivityState Toast(string? open) => new()
        {
            RawKind = ActivityState.KindNotice, Stage = "expand", Name = "QQ", Title = "张伟", Msg = "在吗", Open = open, T0 = Unix(Now),
        };

        Assert.DoesNotContain(CardSheets.Build(Card(CardType.Temporary, Toast(null)), Now, Zh).Buttons, b => b.Action == SheetAction.Open);   // QQ (TODO 四, 10-11)
        Assert.Contains(CardSheets.Build(Card(CardType.Temporary, Toast("ms-actioncenter:")), Now, Zh).Buttons, b => b.Action == SheetAction.Open);
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
    public void One_program_s_several_downloads_are_a_list()
    {
        var sheet = CardSheets.Build(Card(CardType.Status, new ActivityState
        {
            RawKind = ActivityState.KindTransfer, Title = "Download → PC", Name = "report.pdf", Stage = "download", State = "run",
            Done = 1, MoreCount = 2,
            Downloads =
            [
                new DownloadLine("Edge", "report.pdf", 13 * 1024 * 1024, 2 * 1024 * 1024, false),
                new DownloadLine("Edge", null, 1024 * 1024, 1024 * 1024, false),
                new DownloadLine("Edge", "my_*draft*.zip", 5 * 1024 * 1024, null, true),
            ],
            Speed = 3 * 1024 * 1024, UpdatedAt = Now.AddMinutes(-2),
        }), Now, Zh);

        Assert.True(sheet.Markdown);
        Assert.Null(sheet.Title);
        var blocks = CardMarkdown.Parse(sheet.Body[0]);
        Assert.Equal(
            [(MdKind.Bullet, "report.pdf\u001F13.0 MB"), (MdKind.Bullet, "已下载 1.0 MB"), (MdKind.Bullet, "my_*draft*.zip\u001F5.0 MB\u001F已暂停")],
            blocks.Select(b => (b.Kind, b.Text)));
        Assert.Equal("3个\u001F3.0 MB/s", sheet.Progress!.Left);   // one speed for them all (user 10-11)
        Assert.Equal("用时2分钟", sheet.Progress.Right);
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
