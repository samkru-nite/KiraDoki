using IsleBar.Core.Search;
using Xunit;

namespace IsleBar.Core.Tests;

public class FileListTextTests
{
    [Theory]
    [InlineData(0, "0 KB")]
    [InlineData(10, "1 KB")]           // rounded up, like Explorer
    [InlineData(3 * 1024, "3 KB")]
    [InlineData(1024 * 1024 - 1, "1024 KB")]
    [InlineData(5 * 1024 * 1024 + 300 * 1024, "5.3 MB")]
    [InlineData(244_482_048L, "233.2 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.00 GB")]
    public void Sizes(long bytes, string text)
        => Assert.Equal(text, FileListText.FormatSize(bytes));

    private const string Long = @"C:\Users\Kasumi\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\sentry";

    [Fact]
    public void A_path_that_fits_is_unchanged()
        => Assert.Equal(@"C:\Users\Kasumi", FileListText.MiddleEllipsis(@"C:\Users\Kasumi", _ => true));

    [Fact]
    public void Folders_are_dropped_from_the_middle_keeping_the_end()
        => Assert.Equal(@"C:\Users\…\Roaming\Claude\sentry", FileListText.MiddleEllipsis(Long, t => t.Length <= 33));

    [Fact]
    public void Keeps_as_many_last_folders_as_fit()
    {
        var shown = FileListText.MiddleEllipsis(Long, t => t.Length <= 60);
        Assert.StartsWith(@"C:\Users\…\", shown);
        Assert.EndsWith(@"\Claude\sentry", shown);
        Assert.True(shown.Length <= 60);
    }

    [Fact]
    public void A_name_that_fits_is_unchanged()
        => Assert.Equal("notes.txt", FileListText.MiddleEllipsisName("notes.txt", _ => true));

    [Fact]
    public void Long_names_keep_their_start_and_extension()
    {
        var shown = FileListText.MiddleEllipsisName("MM_RENDER_20261004.xlog", t => t.Length <= 18);
        Assert.Equal("MM_RENDE…1004.xlog", shown);   // as much of the start as fits, then the end with the extension
        Assert.True(shown.Length <= 18);
    }

    [Fact]
    public void Names_without_extension_keep_a_little_of_the_end()
        => Assert.Equal("Network Pers…tate", FileListText.MiddleEllipsisName("Network Persistent State", t => t.Length <= 17));

    [Fact]
    public void A_long_dotted_folder_name_keeps_its_last_part()
        => Assert.EndsWith("…eddb.leveldb", FileListText.MiddleEllipsisName("https_chat.deepseek.com_0.indexeddb.leveldb", t => t.Length <= 25));

    [Fact]
    public void Very_narrow_keeps_the_tail()
        => Assert.Equal("…sentry", FileListText.MiddleEllipsis(Long, t => t.Length <= 7));
}
