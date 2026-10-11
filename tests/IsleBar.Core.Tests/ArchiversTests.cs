using IsleBar.Core.SystemWatch;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>WinRAR's progress window, from the words logged on the user's PC (10-11, WinRAR in Chinese).</summary>
public class ArchiversTests
{
    private static readonly string[] Packing =
    [
        @"C:\Users\Kasumi\Desktop\0207-南阳-扫街.rar", "正在添加", "DSC06095.ARW", "64%", "已用时间", "00:00:03", "剩余时间", "00:02:24",
        "压缩率", "56%", "进度", "2%", "后台(&B)", "暂停(&P)", "取消", "模式(&M)...", "帮助",
    ];

    [Fact]
    public void Packing_reads_the_archive_and_the_overall_percentage_not_the_files()
    {
        var look = ArchiverReader.Read("正在创建压缩文件 0207-南阳-扫街.rar", Packing, null);

        Assert.NotNull(look);
        Assert.Equal(ArchiveJob.Zip, look.Job);
        Assert.Equal(@"C:\Users\Kasumi\Desktop\0207-南阳-扫街.rar", look.Archive);
        Assert.Equal(2, look.Percent);   // "进度 2%", not the file's 64% nor the ratio's 56%
        Assert.False(look.Paused);
        Assert.Equal("DSC06095.ARW", look.Current);   // read for the lifted card, not shown yet (user 10-11)
        Assert.Equal(new TimeSpan(0, 2, 24), look.Left);
    }

    [Fact]
    public void Unpacking_is_told_by_its_title()
    {
        string[] texts = [@"C:\Users\Kasumi\Desktop\0207-南阳-扫街.rar", "48%", "已用时间", "剩余时间", "压缩率", "进度", "57%"];

        var look = ArchiverReader.Read("正在从 0207-南阳-扫街.rar 中解压", texts, null);

        Assert.Equal(ArchiveJob.Unzip, look!.Job);
        Assert.Equal(57, look.Percent);
        Assert.Null(look.Current);   // that time the window named no file and left the times empty
        Assert.Null(look.Left);
    }

    [Fact]
    public void A_paused_window_keeps_the_job_it_was_doing()
    {
        var running = ArchiverReader.Read("正在创建压缩文件 0207-南阳-扫街.rar", Packing, null);

        var paused = ArchiverReader.Read("暂停，点击\"继续\"恢复", Packing, running);

        Assert.True(paused!.Paused);
        Assert.Equal(running!.Archive, paused.Archive);
        Assert.Null(ArchiverReader.Read("暂停，点击\"继续\"恢复", Packing, null));
        Assert.Null(ArchiverReader.Read("压缩文件名和参数", [], null));   // the options dialog before it starts
    }

    [Fact]
    public void Any_other_window_asks_but_the_options_one()
    {
        Assert.True(ArchiverReader.Asks("确认文件替换"));   // 10-11: up at the very moment the job's window came, missed at first
        Assert.False(ArchiverReader.Asks("压缩文件名和参数"));
        Assert.False(ArchiverReader.Asks("解压路径和选项"));
    }

    [Fact]
    public void Only_a_job_that_surely_went_through_is_done()
    {
        ArchiveLook At(int p, bool paused = false) => new(ArchiveJob.Zip, "a.rar", p, paused);

        var through = new ArchiveTally();
        through.See(At(10), false);
        through.See(At(86), false);   // unpacking closed at 86% (10-11): too quick to see the end
        Assert.True(through.Finished);

        var cancelled = new ArchiveTally();   // Cancel pauses first, then the window goes (10-11, twice)
        cancelled.See(At(10), false);
        cancelled.See(At(10, paused: true), false);
        Assert.False(cancelled.Finished);

        var asked = new ArchiveTally();   // "确认文件替换": what was answered isn't said
        asked.See(At(0), true);
        asked.See(At(100), false);
        Assert.False(asked.Finished);

        var resumed = new ArchiveTally();
        resumed.See(At(40, paused: true), false);
        resumed.See(At(60), false);
        Assert.False(resumed.Finished);   // paused on the way, last seen at 60: maybe cancelled after all
        resumed.See(At(99), false);
        Assert.True(resumed.Finished);
    }
}
