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
        var lift = LiftMotion.Place(pill, 0, 2560, TaskbarTop, Scale, tall: false);

        Assert.Equal(600, lift.Card.Width);
        Assert.Equal(400, lift.Card.Height);
        Assert.Equal(pill.Left + (pill.Width / 2), lift.Card.Left + (lift.Card.Width / 2));
        Assert.Equal(TaskbarTop - 16, lift.Card.Bottom);
    }

    [Fact]
    public void A_long_card_is_square()
    {
        var lift = LiftMotion.Place(new ScreenRect(1000, 1359, 1440, 1423), 0, 2560, TaskbarTop, Scale, tall: true);
        Assert.Equal(lift.Card.Width, lift.Card.Height);
    }

    [Fact]
    public void Near_the_screen_edge_it_is_pushed_back_to_12()
    {
        // the big pill at x = 12 (small pill on its right): the card would hang 40 off the left → it lines up at 12
        var left = LiftMotion.Place(new ScreenRect(24, 1359, 464, 1423), 0, 2560, TaskbarTop, Scale, tall: false);
        Assert.Equal(24, left.Card.Left);

        var right = LiftMotion.Place(new ScreenRect(2400, 1359, 2840, 1423), 0, 2560, TaskbarTop, Scale, tall: false);
        Assert.Equal(2560 - 24, right.Card.Right);
    }

    [Fact]
    public void The_window_holds_both_the_card_and_the_big_pill()
    {
        var pill = new ScreenRect(24, 1359, 464, 1423);
        var lift = LiftMotion.Place(pill, 0, 2560, TaskbarTop, Scale, tall: false);

        Assert.True(lift.Window.Left <= lift.Card.Left - 48 && lift.Window.Left <= pill.Left);   // room for the bounce
        Assert.True(lift.Window.Right >= lift.Card.Right + 48 && lift.Window.Right >= pill.Right);
        Assert.Equal(lift.Card.Top - (int)(LiftMotion.Room * Scale), lift.Window.Top);
        Assert.Equal(pill.Bottom, lift.Window.Bottom);
    }

    [Fact]
    public void Ai_done_and_ai_trouble_get_the_long_card_the_rest_standard()
    {
        Assert.True(LiftMotion.IsTall(new ActivityState { RawKind = ActivityState.KindToString(ActivityKind.AgentDone), Agent = "claude" }));
        Assert.True(LiftMotion.IsTall(new ActivityState { RawKind = ActivityState.KindToString(ActivityKind.Notice), Agent = "claude", Stage = "interrupted" }));
        Assert.True(LiftMotion.IsTall(new ActivityState { RawKind = ActivityState.KindToString(ActivityKind.Error), Agent = "codex" }));
        Assert.False(LiftMotion.IsTall(new ActivityState { RawKind = ActivityState.KindToString(ActivityKind.AgentWorking), Agent = "claude" }));
        Assert.False(LiftMotion.IsTall(new ActivityState { RawKind = ActivityState.KindToString(ActivityKind.Notice) }));
        Assert.False(LiftMotion.IsTall(new ActivityState { RawKind = ActivityState.KindToString(ActivityKind.Music) }));
    }

    [Fact]
    public void The_setting_reads_back_and_defaults_to_morph()
    {
        Assert.Equal(LiftStyle.Morph, LiftMotion.Parse(null));
        Assert.Equal(LiftStyle.Morph, LiftMotion.Parse("nonsense"));
        Assert.Equal(LiftStyle.SlideUp, LiftMotion.Parse(LiftMotion.Key(LiftStyle.SlideUp)));
    }

    [Fact]
    public void Closing_is_opening_backwards_and_the_sheet_stands_up()
    {
        Assert.Equal(0, LiftMotion.Opening(0, bounce: false), 3);
        Assert.Equal(1, LiftMotion.Opening(1, bounce: true), 3);
        var peak = Enumerable.Range(1, 99).Max(i => LiftMotion.Opening(i / 100.0, bounce: true));
        Assert.InRange(peak, 1.02, 1.035);   // a light overshoot, ~3%
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
