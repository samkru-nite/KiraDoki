using IsleBar.Core.Island;
using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class LiftMotionTests
{
    // 200% screen 2560 wide, taskbar top at 1440 - 96; the big pill 220×32 dip = 440×64 px
    private const double Scale = 2;
    private const int TaskbarTop = 1344;

    [Fact]
    public void The_card_is_centred_on_the_big_pill_and_sits_8_above_the_taskbar()
    {
        var pill = new ScreenRect(1000, 1359, 1440, 1423);
        var lift = LiftMotion.Place(pill, 0, 2560, TaskbarTop, Scale, height: LiftMotion.Height);

        Assert.Equal(600, lift.Card.Width);
        Assert.Equal(400, lift.Card.Height);
        Assert.Equal(pill.Left + (pill.Width / 2), lift.Card.Left + (lift.Card.Width / 2));
        Assert.Equal(TaskbarTop - 16, lift.Card.Bottom);
    }

    [Fact]
    public void An_adaptive_card_takes_the_lowest_height_that_holds_its_middle()
    {
        // no buttons, no progress: the middle is the height − 2 × 16 − 16 − 12 = 140 / 240 / 340
        var sheet = new CardSheet("x", null, SheetTone.Plain, null, SheetLayout.Text, "t", [], null, null, null, null, [], Adaptive: true);
        Assert.Equal(200, LiftMotion.Fit(sheet, 140));
        Assert.Equal(300, LiftMotion.Fit(sheet, 141));
        Assert.Equal(300, LiftMotion.Fit(sheet, 240));
        Assert.Equal(400, LiftMotion.Fit(sheet, 241));
        Assert.Equal(400, LiftMotion.Fit(sheet, 999));   // scrolls
        Assert.Equal(200, LiftMotion.Fit(sheet with { Adaptive = false }, 999));
    }

    [Fact]
    public void The_window_reaches_past_the_small_pill_so_the_card_can_go_down_into_a_task_cell()
    {
        // big pill 1000–1440, small pill to its right up to 1800: the window spans it plus a cell and the overshoot (24 + 16 dip)
        var pill = new ScreenRect(1000, 1359, 1440, 1423);
        var bar = new ScreenRect(1000, 1345, 1800, 1437);
        var lift = LiftMotion.Place(pill, 0, 2560, TaskbarTop, Scale, height: LiftMotion.Height, bar);

        Assert.Equal(1800 + 80, lift.Window.Right);
        Assert.Equal(lift.Card.Left - 48, lift.Window.Left);   // on the left the card (300 on a 220 pill) and its room reach further
        Assert.Equal(1437, lift.Window.Bottom);
        Assert.Equal(LiftMotion.Place(pill, 0, 2560, TaskbarTop, Scale, height: LiftMotion.Height).Card, lift.Card);   // the card itself doesn't move
    }

    [Fact]
    public void A_long_card_is_3_by_4()
    {
        var lift = LiftMotion.Place(new ScreenRect(1000, 1359, 1440, 1423), 0, 2560, TaskbarTop, Scale, height: LiftMotion.TallHeight);
        Assert.Equal(lift.Card.Width * 4 / 3, lift.Card.Height);
    }

    [Fact]
    public void Near_the_screen_edge_it_is_pushed_back_to_12()
    {
        // the big pill at x = 12 (small pill on its right): the card would hang 40 off the left → it lines up at 12
        var left = LiftMotion.Place(new ScreenRect(24, 1359, 464, 1423), 0, 2560, TaskbarTop, Scale, height: LiftMotion.Height);
        Assert.Equal(24, left.Card.Left);

        var right = LiftMotion.Place(new ScreenRect(2400, 1359, 2840, 1423), 0, 2560, TaskbarTop, Scale, height: LiftMotion.Height);
        Assert.Equal(2560 - 24, right.Card.Right);
    }

    [Fact]
    public void The_window_holds_both_the_card_and_the_big_pill()
    {
        var pill = new ScreenRect(24, 1359, 464, 1423);
        var lift = LiftMotion.Place(pill, 0, 2560, TaskbarTop, Scale, height: LiftMotion.Height);

        Assert.True(lift.Window.Left <= lift.Card.Left - 48 && lift.Window.Left <= pill.Left);   // room for the bounce
        Assert.True(lift.Window.Right >= lift.Card.Right + 48 && lift.Window.Right >= pill.Right);
        Assert.Equal(lift.Card.Top - (int)(LiftMotion.Room * Scale), lift.Window.Top);
        Assert.Equal(pill.Bottom, lift.Window.Bottom);
    }

    [Fact]
    public void Closing_is_opening_backwards_and_the_sheet_stands_up()
    {
        Assert.Equal(0, LiftMotion.Opening(0, bounce: false), 3);
        Assert.Equal(1, LiftMotion.Opening(1, bounce: true), 3);
        var peak = Enumerable.Range(1, 99).Max(i => LiftMotion.Opening(i / 100.0, bounce: true));
        Assert.True(peak <= 1);   // the card never overshoots (10-10)
        Assert.Equal(1, LiftMotion.Closing(0), 3);
        Assert.Equal(0, LiftMotion.Closing(1), 3);
        Assert.True(Enumerable.Range(0, 101).All(i => LiftMotion.Closing(i / 100.0) >= 0));   // never sinks past the pill
        Assert.Equal(LiftMotion.Opening(0.3, bounce: false), LiftMotion.Closing(0.7), 6);
        Assert.Equal(LiftMotion.Tilt, LiftMotion.TiltAt(0));
        Assert.Equal(0, LiftMotion.TiltAt(1));
        Assert.Equal(0, LiftMotion.TiltAt(1.03));    // never leans back past upright
        Assert.Equal(LiftMotion.Tilt, LiftMotion.TiltAt(-0.03));
        Assert.Equal(0, LiftMotion.Window(0.4, 0.5, 1));
        Assert.Equal(0.5, LiftMotion.Window(0.75, 0.5, 1), 3);
    }
}
