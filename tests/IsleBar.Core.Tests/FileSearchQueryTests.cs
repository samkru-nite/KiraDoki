using IsleBar.Core.Configuration;
using IsleBar.Core.Search;
using Xunit;

namespace IsleBar.Core.Tests;

public class FileSearchQueryTests
{
    [Theory]
    [InlineData(@"Windows\Prefetch", true)]
    [InlineData("C:/Windows/notepad", true)]
    [InlineData("apple .jpg", false)]
    public void A_query_naming_a_path_shows_everything(string query, bool targetsPath)
        => Assert.Equal(targetsPath, FileSearchQuery.TargetsPath(query));

    [Theory]
    [InlineData("name", "folder,a.txt,B.txt")]       // folders first, then A→Z ignoring case
    [InlineData("modified", "B.txt,folder,a.txt")]   // newest first
    [InlineData("size", "a.txt,B.txt,folder")]       // largest first, folders (no size) last
    public void Local_order_matches_everything(string sort, string expected)
    {
        var rows = new[]
        {
            (Name: "a.txt", When: new DateTime(2026, 10, 1), Size: (long?)300, Folder: false),
            (Name: "B.txt", When: new DateTime(2026, 10, 4), Size: (long?)20, Folder: false),
            (Name: "folder", When: new DateTime(2026, 10, 2), Size: (long?)null, Folder: true),
        };
        var ordered = FileSearchQuery.Order(rows, sort, r => r.Name, r => r.When, r => r.Size, r => r.Folder);
        Assert.Equal(expected, string.Join(",", ordered.Select(r => r.Name)));
    }

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
