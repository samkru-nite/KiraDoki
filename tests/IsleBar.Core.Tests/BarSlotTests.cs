using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class BarSlotTests
{
    // the user's taskbar on 10-05 (3072 × 1920 at 200%): icons centred from Start (1091) to the last app (1981), tray from 2560
    private static readonly TaskbarLayout Centred = new(0, 1824, 3072, 1920, IconsLeft: 1091, IconsRight: 1981, TrayLeft: 2560, WidgetsRight: null);
    private static readonly TaskbarLayout IconsOnLeft = Centred with { IconsLeft = 0, IconsRight = 900 };

    private const int W = 440;
    private const int H = 64;
    private const int Gap = 24;

    [Fact]
    public void Left_sits_at_the_left_end_centred_in_the_taskbar()
    {
        var spot = BarSlot.Place(BarPosition.Left, Centred, W, H, Gap, 0);
        Assert.Equal(new BarSpot(true, 24, 1840), spot);
    }

    [Fact]
    public void Centred_below_the_light_top_edge()
    {
        // 96 px taskbar with a 2 px light line on top: 94 px below it, the 64 px bar 15 px from each end (measured 10-05)
        var spot = BarSlot.Place(BarPosition.Left, Centred, W, H, Gap, 0, topEdge: 2);
        Assert.Equal(1824 + 2 + 15, spot.Y);
    }

    [Fact]
    public void Left_steps_past_the_widgets_button()
        => Assert.Equal(324, BarSlot.Place(BarPosition.Left, Centred with { WidgetsRight = 300 }, W, H, Gap, 0).X);

    [Fact]
    public void Left_doesnt_fit_when_the_icons_start_at_the_left()
        => Assert.False(BarSlot.Place(BarPosition.Left, IconsOnLeft, W, H, Gap, 0).Fits);

    [Fact]
    public void Left_doesnt_fit_when_it_would_reach_the_start_button()
        => Assert.False(BarSlot.Place(BarPosition.Left, Centred with { IconsLeft = 400 }, W, H, Gap, 0).Fits);

    [Fact]
    public void Center_is_the_middle_of_the_screen_when_the_icons_are_on_the_left()
        => Assert.Equal(new BarSpot(true, 1316, 1840), BarSlot.Place(BarPosition.Center, IconsOnLeft, W, H, Gap, 0));

    [Fact]
    public void Center_steps_right_of_the_icons_when_they_reach_the_middle()
        => Assert.Equal(new BarSpot(true, 2005, 1840), BarSlot.Place(BarPosition.Center, Centred, W, H, Gap, 0));

    [Fact]
    public void Center_doesnt_fit_when_stepping_aside_would_reach_the_tray()
        => Assert.False(BarSlot.Place(BarPosition.Center, Centred with { IconsRight = 2200 }, W, H, Gap, 0).Fits);

    [Fact]
    public void Manual_is_the_offset_from_the_left_end_kept_on_the_taskbar()
    {
        Assert.Equal(new BarSpot(true, 500, 1840), BarSlot.Place(BarPosition.Manual, Centred, W, H, Gap, 500));
        Assert.Equal(3072 - W, BarSlot.Place(BarPosition.Manual, Centred, W, H, Gap, 9999).X);
        Assert.Equal(0, BarSlot.Place(BarPosition.Manual, Centred, W, H, Gap, -50).X);
    }

    [Fact]
    public void A_second_monitor_left_of_the_main_one_has_negative_coordinates()
        => Assert.Equal(-1920 + Gap, BarSlot.Place(BarPosition.Left, Centred with { Left = -1920, Right = 0, IconsLeft = null, IconsRight = null }, W, H, Gap, 0).X);

    [Theory]
    [InlineData("left", BarPosition.Left)]
    [InlineData("center", BarPosition.Center)]
    [InlineData("manual", BarPosition.Manual)]
    [InlineData(null, BarPosition.Left)]
    [InlineData("nonsense", BarPosition.Left)]
    public void Settings_value_round_trips(string? value, BarPosition expected)
    {
        Assert.Equal(expected, BarSlot.Parse(value));
        Assert.Equal(expected, BarSlot.Parse(BarSlot.Key(expected)));
    }
}
