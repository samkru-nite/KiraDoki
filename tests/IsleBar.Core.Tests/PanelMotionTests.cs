using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class PanelMotionTests
{
    [Fact]
    public void It_is_centred_on_the_pills_with_its_bottom_8_above_the_taskbar()
    {
        // pills from 1000 to 1361 (four icons: the same width as the panel), at 100%
        var p = PanelMotion.Place(new ScreenRect(1000, 1040, 1361, 1072), 0, 2560, 1032, 1, 200);
        Assert.Equal(1000, p.Left);
        Assert.Equal(361, p.Width);
        Assert.Equal(1024, p.Bottom);
        Assert.Equal(200, p.Height);
    }

    [Fact]
    public void Near_the_screen_edge_it_keeps_12_from_it()
    {
        // the bar at the far left (12 in), two icons: the group is 313 wide, centring would start at -12
        var p = PanelMotion.Place(new ScreenRect(12, 1040, 325, 1072), 0, 1920, 1032, 1, 200);
        Assert.Equal(12, p.Left);
        var right = PanelMotion.Place(new ScreenRect(1800, 1040, 1908, 1072), 0, 1920, 1032, 1, 200);
        Assert.Equal(1920 - 12, right.Right);
    }

    [Fact]
    public void Sizes_scale_with_the_display()
    {
        var p = PanelMotion.Place(new ScreenRect(24, 1841, 650, 1905), 0, 3072, 1824, 2, 248);
        Assert.Equal(722, p.Width);
        Assert.Equal(496, p.Height);
        Assert.Equal(1824 - 16, p.Bottom);
    }

    [Theory]
    [InlineData(1, 36)]     // one line: the mode button's 28 + 4 around
    [InlineData(2, 53)]     // 2 × 18.5 + 8 + 8
    [InlineData(4, 90)]
    [InlineData(9, 90)]     // more lines scroll inside
    public void The_input_box_grows_to_four_lines(int lines, double height)
        => Assert.Equal(height, PanelMotion.FieldHeight(lines, 18.5));

    [Fact]
    public void The_shape_goes_from_the_circle_to_the_panel()
    {
        var circle = new MotionBox(300, 500, 24, 24, 12);
        var panel = new MotionBox(0, 0, 361, 248, 8);
        Assert.Equal(circle, PanelMotion.Between(circle, panel, 0));
        Assert.Equal(panel, PanelMotion.Between(circle, panel, 1));
        var mid = PanelMotion.Between(circle, panel, 0.5);
        Assert.InRange(mid.Radius, 8, mid.Height / 2);
    }
}
