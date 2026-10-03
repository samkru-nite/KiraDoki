using IsleBar.Core.Configuration;
using IsleBar.Core.Search;
using Xunit;

namespace IsleBar.Core.Tests;

public class FileSearchQueryTests
{
    private static readonly IReadOnlyList<string> System = FileSearchQuery.SystemFolders(@"C:\Windows");

    [Fact]
    public void Query_excludes_the_windows_folder()
        => Assert.Equal("<report> !path:\"C:\\Windows\\\"", FileSearchQuery.Build(" report ", System));

    [Theory]
    [InlineData(@"Windows\Prefetch")]
    [InlineData("C:/Windows/notepad")]
    public void A_query_naming_a_path_is_left_alone(string query)
        => Assert.Equal(query, FileSearchQuery.Build(query, System));

    [Theory]
    [InlineData(@"C:\Windows\Prefetch\DEEPSEEK HARNESS.EXE-250A3CF3.pf", true)]
    [InlineData(@"c:\windows\System32\notepad.exe", true)]
    [InlineData(@"C:\WindowsApps\thing.txt", false)]   // only the Windows folder itself, not names starting with it
    [InlineData(@"C:\Users\Kasumi\AppData\Local\Programs\DeepSeek Harness\DeepSeek Harness.exe", false)]   // apps install under AppData
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\WeChat.lnk", false)]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\$R1.txt", false)]   // user 10-04: keep the recycle bin
    public void Only_the_windows_folder_is_hidden(string path, bool hidden)
        => Assert.Equal(hidden, FileSearchQuery.IsSystem(path, System));

    [Theory]
    [InlineData("name", 1u)]
    [InlineData("modified", 14u)]
    [InlineData("size", 6u)]
    [InlineData(null, 1u)]
    [InlineData("whatever", 1u)]
    public void Sort_codes_match_everything(string? sort, uint code)
        => Assert.Equal(code, FileSearchQuery.EverythingSort(sort));

    [Fact]
    public void Count_and_sort_are_saved_and_kept_in_range()
    {
        var fresh = new IsleBarSettings();
        Assert.Equal(8, fresh.FileResults);
        Assert.Equal("name", fresh.FileSort);

        var raw = new System.Text.Json.Nodes.JsonObject();
        SettingsCodec.Apply(new IsleBarSettings { FileResults = 15, FileSort = "size" }, raw);
        var back = SettingsCodec.FromJson(raw);
        Assert.Equal(15, back.FileResults);
        Assert.Equal("size", back.FileSort);

        raw["file_results"] = 9999;
        raw["file_sort"] = "nonsense";
        back = SettingsCodec.FromJson(raw);
        Assert.Equal(FileSearchQuery.MaxCount, back.FileResults);
        Assert.Equal("name", back.FileSort);
    }
}
