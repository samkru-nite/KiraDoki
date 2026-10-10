using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public class DropZoneTests
{
    private const int Gap = 16;

    [Fact]
    public void On_the_left_it_runs_from_the_taskbars_end_to_the_icons()
    {
        var bar = new TaskbarLayout(0, 1824, 3840, 1920, IconsLeft: 1500, IconsRight: 2300, TrayLeft: 3300, WidgetsRight: null);
        Assert.Equal((0, 1500 - Gap), DropZone.Span(bar, 48, 600, Gap));
    }

    [Fact]
    public void Between_the_icons_and_the_tray_it_fills_the_gap()
    {
        var bar = new TaskbarLayout(0, 1824, 3840, 1920, IconsLeft: 100, IconsRight: 900, TrayLeft: 3300, WidgetsRight: null);
        Assert.Equal((900 + Gap, 3300 - Gap), DropZone.Span(bar, 1700, 2200, Gap));
    }

    [Fact]
    public void The_widgets_button_bounds_it_on_the_left()
    {
        var bar = new TaskbarLayout(0, 1824, 3840, 1920, IconsLeft: 1500, IconsRight: 2300, TrayLeft: 3300, WidgetsRight: 300);
        Assert.Equal((300 + Gap, 1500 - Gap), DropZone.Span(bar, 316, 900, Gap));
    }

    [Fact]
    public void Never_narrower_than_the_pills()
    {
        var bar = new TaskbarLayout(0, 1824, 3840, 1920, IconsLeft: 1500, IconsRight: 2300, TrayLeft: 3300, WidgetsRight: null);
        Assert.Equal((0, 1495), DropZone.Span(bar, 48, 1495, Gap));   // a hand-set spot right up to the icons
    }

    [Fact]
    public void Without_the_taskbars_layout_it_is_the_pills()
        => Assert.Equal((100, 700), DropZone.Span(null, 100, 700, Gap));
}
