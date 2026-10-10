using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class ListMotionTests
{
    private static readonly MotionBox Pill = new(200, 600, 220, 32, 16);
    private static readonly MotionBox Dots = new(470, 604, 24, 24, 12);

    [Fact]
    public void Slots_stack_up_from_above_the_taskbar_eight_apart()
    {
        Assert.Equal(592 - 8 - 32, ListMotion.Slot(0, Pill, 592).Y);
        Assert.Equal(592 - 8 - 32 - 40, ListMotion.Slot(1, Pill, 592).Y);
        Assert.Equal(Pill.X, ListMotion.Slot(3, Pill, 592).X);
        Assert.Equal(Pill.Width, ListMotion.Slot(3, Pill, 592).Width);
    }

    [Fact]
    public void A_leaving_pill_shrinks_about_its_centre_and_fades_before_the_end()
    {
        var slot = ListMotion.Slot(0, Pill, 592);
        var (start, startScale, startShown) = ListMotion.Leave(slot, 0);
        Assert.Equal(slot, start);
        Assert.Equal(1, startScale);
        Assert.Equal(1, startShown);

        var (end, endScale, endShown) = ListMotion.Leave(slot, 1);
        Assert.Equal(ListMotion.LeaveScale, endScale, 6);
        Assert.Equal(slot.X + (slot.Width / 2), end.X + (end.Width / 2), 6);
        Assert.Equal(slot.Y + (slot.Height / 2), end.Y + (end.Height / 2), 6);
        Assert.Equal(0, endShown);
        Assert.Equal(0, ListMotion.Leave(slot, ListMotion.LeaveFade).Opacity, 6);
    }

    [Fact]
    public void Only_what_fits_under_the_screen_top_is_listed()
    {
        Assert.Equal(5, ListMotion.Fits(5, 32, 592, 0));
        // 592 − 12 + 8 = 588 → 14 pills of 40
        Assert.Equal(14, ListMotion.Fits(30, 32, 592, 0));
        Assert.Equal(1, ListMotion.Fits(3, 32, 30, 0));
    }

    [Fact]
    public void Pills_set_off_one_stagger_apart_nearest_first()
    {
        var length = ListMotion.Length(false);
        Assert.Equal(0, ListMotion.Out(1, TimeSpan.FromMilliseconds(20), false, 20), 9);
        Assert.True(ListMotion.Out(0, TimeSpan.FromMilliseconds(20), false, 20) > 0);
        Assert.Equal(1, ListMotion.Out(2, length + TimeSpan.FromMilliseconds(40), false, 20), 6);
        Assert.Equal(length + TimeSpan.FromMilliseconds(80), ListMotion.Total(3, false, 40));
        Assert.Equal(ListMotion.Length(true), ListMotion.Total(1, true, 40));
    }

    [Fact]
    public void Way_out_starts_at_the_dots_circle_ends_in_the_slot_and_bows_up()
    {
        var slot = ListMotion.Slot(2, Pill, 592);
        Assert.Equal(Dots, ListMotion.Box(Dots, slot, 0).Box);
        Assert.Equal(slot, ListMotion.Box(Dots, slot, 1).Box);
        var mid = ListMotion.Box(Dots, slot, 0.5).Box;
        var straight = (Dots.Y + slot.Y) / 2;
        Assert.Equal(straight - ListMotion.Arc, mid.Y, 6);
    }

    [Fact]
    public void Content_shows_from_half_way()
    {
        Assert.Equal((0.0, 0.0), ListMotion.Shows(0));
        Assert.Equal(1, ListMotion.Shows(ListMotion.Appear).Pill);
        Assert.Equal(0, ListMotion.Shows(0.5).Content);
        Assert.Equal(1, ListMotion.Shows(1).Content);
        Assert.Equal(1, ListMotion.Back(0), 6);
        Assert.Equal(0, ListMotion.Back(1), 6);
    }
}
