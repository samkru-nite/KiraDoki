using IsleBar.Core.Localization;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class BarModesUsableTests
{
    [Fact]
    public void Excluded_modes_are_skipped_by_Tab()
    {
        var usable = BarModes.Usable([BarMode.Claude, BarMode.Files], filesOn: true, ["claude"]);
        Assert.Equal([BarMode.Files], usable);
        Assert.Equal(BarMode.Files, BarModes.Next(BarMode.Files, usable, filesOn: true));
    }

    [Fact]
    public void File_search_switch_is_also_considered()
        => Assert.Equal([BarMode.Claude], BarModes.Usable([BarMode.Files, BarMode.Claude], filesOn: false, []));

    [Fact]
    public void Turning_all_off_leaves_only_Claude()
        => Assert.Equal([BarMode.Claude], BarModes.Usable([BarMode.Claude, BarMode.Files], filesOn: false, ["claude"]));
}
