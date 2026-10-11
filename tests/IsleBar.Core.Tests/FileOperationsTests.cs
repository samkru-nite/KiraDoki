using IsleBar.Core.Localization;
using IsleBar.Core.SystemWatch;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>
/// Explorer's progress window, as logged on the user's Windows 11 (Chinese) on 10-10. Only what the window says outright is used:
/// what it does, the file, whether a question came up; "已复制" only when it surely went through, else no card.
/// </summary>
public class FileOperationsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 23, 0, 0, TimeSpan.Zero);
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly LanguageStrings Zh = LanguageCatalog.For("zh");

    private static FileOpLook Copying(int count, string name, int left, string leftSize, string speed = "1.55 GB", string title = "已完成 12%", string verb = "复制")
        => FileOpReader.Read(title,
        [
            "简略信息", $"正在将 {count} 个项目从 桌面 {verb}到 新建文件夹 (2) - 已完成 12%", $"正在将 {count} 个项目从", "桌面", $"{verb}到",
            "新建文件夹 (2)", "暂停操作", "取消磁贴", "已完成 12%", $"操作进度: 2%；速度: {speed}/秒", "名称:", name, "剩余时间:", "正在计算...",
            "剩余项目:", $"{left} ({leftSize})", "系统", "系统", "最小化", "最大化", "关闭",
        ]);

    private static FileOpLook Clash(int count, int same) => FileOpReader.Read("替换或跳过文件",
    [
        "简略信息", $"正在将 {count} 个项目从 0430 复制到 text1 - 已中断 - 已完成 0%", $"正在将 {count} 个项目从", "0430", "复制到", "text1",
        $"目标包含 {same} 个同名文件", "替换目标中的文件(R)", "跳过这些文件(S)", "让我决定每个文件(L)", "系统", "系统", "最小化", "最大化", "关闭",
    ]);

    [Fact]
    public void Reads_the_sentence_the_file_and_whats_left()
    {
        var look = Copying(273, "DSC02622.ARW", 222, "12.4 GB");
        Assert.Equal(FileVerb.Copy, look.Verb);
        Assert.Equal(273, look.Count);
        Assert.Equal("DSC02622.ARW", look.Name);
        Assert.Equal(222, look.Left);
        Assert.Equal((long)(12.4 * Gb), look.LeftBytes);
        Assert.Equal(1.55 * Gb, look.Speed!.Value, 0);
        Assert.Equal(12, look.Percent);
        Assert.False(look.Asking);
    }

    [Fact]
    public void Tells_copy_from_move()
    {
        Assert.Equal(FileVerb.Move, Copying(273, "a", 1, "1 KB", verb: "移动").Verb);
        Assert.Equal(FileVerb.Copy, Copying(273, "a", 1, "1 KB").Verb);
    }

    [Fact]
    public void An_empty_name_is_no_name()
    {
        // 23:55 "名称: ¦ 剩余时间:" — it said "已移动 剩余时间:"
        var look = FileOpReader.Read("已完成 0%", ["正在将 23 个项目从 A 移动到 A - 已完成 0%", "暂停操作", "名称:", "剩余时间:", "正在计算...", "剩余项目:", "23 (1.37 GB)"]);
        Assert.Null(look.Name);
        Assert.Equal(23, look.Left);
    }

    [Theory]
    [InlineData("替换或跳过文件", "目标包含 272 个同名文件")]
    [InlineData("替换或跳过文件", "共 272 个文件冲突")]
    [InlineData("文件正在使用", "操作无法完成，因为文件已在 Word 中打开。")]
    [InlineData("1 个已中断的操作", "源文件名和目标文件名相同。")]
    public void Every_question_seen_is_asking(string title, string says)
        => Assert.True(FileOpReader.Read(title, ["简略信息", "正在将 1 个项目从 桌面 移动到 桌面 - 已中断 - 已完成 0%", says]).Asking);

    [Fact]
    public void Paused_is_not_asking_and_an_empty_read_is_neither()
    {
        Assert.False(FileOpReader.Read("已暂停 - 已完成 53%", ["正在将 273 个项目从 A 复制到 B", "继续操作", "取消磁贴"]).Asking);
        Assert.False(FileOpReader.Read("已完成 0%", []).Asking);
    }

    [Fact]
    public void A_plain_copy_is_done()
    {
        var tally = new FileOpTally();
        tally.See(Copying(273, "DSC02622.ARW", 222, "12.4 GB"), T0);
        tally.See(Copying(273, "DSC02690.ARW", 0, "0 字节"), T0.AddSeconds(9));
        Assert.True(tally.Finished(T0.AddSeconds(10)));
        var text = FileOpText.Done(FileOpText.JobOf(tally.Verb, false), tally.Count, tally.Name, Zh);
        Assert.Equal(("已复制", "DSC02690.ARW", 272), (text.Words, text.Detail, text.More));
    }

    [Fact]
    public void Closing_between_looks_with_a_little_left_is_still_done()
    {
        // test A (23:30): last look "4 left, 201 MB at 1.23 GB/s", then the window closed — it had finished
        var tally = new FileOpTally();
        tally.See(Copying(272, "DSC02580.ARW", 4, "201 MB", "1.23 GB"), T0);
        Assert.True(tally.Finished(T0.AddSeconds(1)));
    }

    [Fact]
    public void Closing_with_far_too_much_left_says_nothing()
    {
        // 23:25: last look "181 left, 10.1 GB", paused; the window closed within a second (it was cancelled)
        var tally = new FileOpTally();
        tally.See(Copying(273, "DSC02674.ARW", 181, "10.1 GB", "872 MB"), T0);
        Assert.False(tally.Finished(T0.AddSeconds(1)));
    }

    [Fact]
    public void Cancelling_says_nothing()
    {
        var tally = new FileOpTally();
        tally.See(Copying(273, "DSC02812.ARW", 0, "0 字节"), T0);
        tally.See(FileOpReader.Read("已暂停 - 已完成 53%", ["正在将 273 个项目从 A 复制到 B - 正在取消 - 已完成 53%", "继续操作"]), T0.AddSeconds(1));
        Assert.False(tally.Finished(T0.AddSeconds(2)));
    }

    [Fact]
    public void After_a_question_it_says_nothing_however_it_ends()
    {
        // replace, skip, close the question: the window looks the same afterwards — the answer is never guessed
        var tally = new FileOpTally();
        tally.See(Copying(272, "DSC02580.ARW", 272, "14.2 GB"), T0);
        tally.See(Clash(272, 272), T0.AddSeconds(1));
        Assert.True(tally.Asking);
        tally.See(Copying(272, "DSC02579.xmp", 0, "0 字节"), T0.AddSeconds(12));
        Assert.False(tally.Asking);
        Assert.False(tally.Finished(T0.AddSeconds(13)));
    }

    [Fact]
    public void Folded_to_the_short_form_it_goes_by_the_percentage()
    {
        var tally = new FileOpTally();
        tally.See(FileOpReader.Read("已完成 100%", ["正在将 3 个项目从 A 复制到 B - 已完成 100%", "暂停操作", "取消磁贴"]), T0);
        Assert.True(tally.Finished(T0.AddSeconds(1)));
    }

    [Fact]
    public void While_running_it_says_the_job_and_the_file()
    {
        var text = FileOpText.Running(FileJob.Copy, 273, "DSC02622.ARW", Zh);
        Assert.Equal(("复制中", "DSC02622.ARW", 272), (text.Words, text.Detail, text.More));
        var moving = FileOpText.Running(FileJob.Move, 273, null, Zh);
        Assert.Equal(("移动中", "273个", 0), (moving.Words, moving.Detail, moving.More));
    }

    [Fact]
    public void A_window_that_says_neither_copy_nor_move_is_just_running()
    {
        // deleting uses the same window (user 10-11): never "复制中"
        Assert.Equal(FileJob.Unknown, FileOpText.JobOf(FileVerb.Unknown, unzip: false));
        Assert.Equal("进行中", FileOpText.Running(FileJob.Unknown, 3000, null, Zh).Words);
    }
}
