using IsleBar.Core.SystemWatch;
using Xunit;

namespace IsleBar.Core.Tests;

public class TransitOutTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 11, 0, 30, 0, TimeSpan.Zero);
    private const string A = @"C:\Users\K\Desktop\新建文件夹\DSC02580.ARW";
    private const string B = @"C:\Users\K\Desktop\新建文件夹\DSC02581.ARW";

    private static (IReadOnlyList<string> Moved, IReadOnlyList<string> Copied) Check(
        string? target, IEnumerable<string> present, Dictionary<string, DateTimeOffset> made)
    {
        var there = present.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return TransitOut.Check([A, B], target, Since, there.Contains, p => made.TryGetValue(p, out var t) ? t : null);
    }

    [Fact]
    public void Gone_from_where_they_were_is_moved()
    {
        var (moved, copied) = Check(@"C:\Users\K\Desktop", [], []);
        Assert.Equal([A, B], moved);
        Assert.Empty(copied);
    }

    [Fact]
    public void Still_there_and_made_just_now_in_the_folder_is_copied()
    {
        var (moved, copied) = Check(@"E:\", [A, B], new() { [@"E:\DSC02580.ARW"] = Since.AddSeconds(1), [@"E:\DSC02581.ARW"] = Since.AddSeconds(3) });
        Assert.Empty(moved);
        Assert.Equal([A, B], copied);
    }

    [Fact]
    public void An_old_file_of_the_same_name_is_not_a_copy()
    {
        // 10-11 00:31: the desktop already had these names from earlier tests
        var (_, copied) = Check(@"C:\Users\K\Desktop", [A, B], new() { [@"C:\Users\K\Desktop\DSC02580.ARW"] = Since.AddDays(-1) });
        Assert.Empty(copied);
    }

    [Fact]
    public void Not_there_yet_or_not_a_folder_is_neither()
    {
        Assert.Equal((0, 0), Check(@"E:\", [A, B], []) is var r ? (r.Moved.Count, r.Copied.Count) : default);
        Assert.Equal((0, 0), Check(null, [A, B], []) is var s ? (s.Moved.Count, s.Copied.Count) : default);
    }

    [Fact]
    public void Let_go_in_their_own_folder_is_no_copy()
        => Assert.Empty(Check(@"C:\Users\K\Desktop\新建文件夹", [A, B], new() { [A] = Since.AddSeconds(1) }).Copied);

    [Fact]
    public void Names_are_the_first_and_how_many_more()
    {
        Assert.Equal("DSC02580.ARW +1", TransitOut.Names([A, B]));
        Assert.Equal("DSC02580.ARW", TransitOut.Names([A]));
    }
}
