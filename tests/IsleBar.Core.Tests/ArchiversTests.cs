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

    private static readonly string[] SevenPacking =
    [
        "后台(&B)", "暂停(&P)", "取消", "已用时间：", "剩余时间：", "文件:", "发生错误：", "总大小：", "速度：", "已处理：", "压缩后大小:", "压缩率：",
        "00:00:12", "00:00:17", "6", " / 6", "470 MB", "15 MB/s", "192 MB", "113 MB", "59%", "正在添加", "\nDSC06127.ARW",
    ];

    [Fact]
    public void Seven_zip_says_the_percentage_and_the_job_in_its_title()
    {
        var look = ArchiverReader.Read(@"40% 正在压缩 C:\Users\Kasumi\De ... 阳-扫街\0207-南阳-扫街.7z", SevenPacking, null);

        Assert.Equal(ArchiveJob.Zip, look!.Job);
        Assert.Equal("0207-南阳-扫街.7z", look.Archive);   // the path is cut in the middle; the name is whole
        Assert.Equal(40, look.Percent);
        Assert.False(look.Paused);
        Assert.Equal("DSC06127.ARW", look.Current);
        Assert.Equal(new TimeSpan(0, 0, 17), look.Left);   // elapsed first, then left
        Assert.Equal(15d * 1024 * 1024, look.Speed);

        var unzip = ArchiverReader.Read(@"62% 正在解压 C:\Users\Kasumi\De ... -扫街\0207-南阳-扫街.rar", [], null);
        Assert.Equal(ArchiveJob.Unzip, unzip!.Job);
        Assert.True(ArchiverReader.Read(@"已暂停 30% 正在压缩 C:\Users\Kasumi\De ... 阳-扫街\0207-南阳-扫街.7z", [], look)!.Paused);
        Assert.Null(ArchiverReader.Read(" 正在压缩", [], null));   // a moment at the start: no job yet
    }

    [Fact]
    public void Bandizip_is_read_before_7_zip_whose_title_it_looks_like()
    {
        string[] texts = ["33%", "00:00:00 / 00:00:07", "100%", "00:00:00 / 00:00:00", "DSC06095.ARW", "停止", "暂停", "关闭", "IDC_STATIC_MESSAGE"];

        var zip = ArchiverReader.Read("33% 正在压缩 0207-南阳-扫街.zip - Bandizip (Standard)", texts, null);
        var unzip = ArchiverReader.Read("14% 已解压 0207-南阳-扫街.zip - Bandizip (Standard)", [], null);   // "已解压" while still at it

        Assert.Equal(ArchiveJob.Zip, zip!.Job);
        Assert.Equal("0207-南阳-扫街.zip", zip.Archive);   // not "… - Bandizip (Standard)"
        Assert.Equal(33, zip.Percent);
        Assert.Equal("DSC06095.ARW", zip.Current);
        Assert.Equal(TimeSpan.FromSeconds(7), zip.Left);
        Assert.Equal(ArchiveJob.Unzip, unzip!.Job);
        Assert.Equal(14, unzip.Percent);
        Assert.Null(ArchiverReader.Read("已解压", [], null));   // a moment at the start
        Assert.True(ArchiverReader.Asks("Bandizip"));   // "Stop?" 是 / 否
        Assert.True(ArchiverReader.Asks("重复的文件"));
    }

    [Fact]
    public void Any_other_window_asks_but_the_options_one()
    {
        Assert.True(ArchiverReader.Asks("正在压缩"));   // 7-Zip's "确定要取消吗？"
        Assert.False(ArchiverReader.Asks("添加到压缩包"));
        Assert.False(ArchiverReader.Asks(@"解压 : C:\Users\Kasumi\Desktop\0207-南阳-扫街\0207-南阳-扫街.7z"));
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
