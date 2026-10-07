using IsleBar.Core.Island;
using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public class TimerDialTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void Countdown_goes_round_once_to_the_end()
    {
        var timer = TimerParser.ToActivity(TimeSpan.FromMinutes(10), Now.AddMinutes(-2.5));
        var dial = TimerDial.Of(timer, false, Now)!;
        Assert.Equal(90, dial.Angle, 3);
        Assert.Equal(270, dial.Turn, 3);
        Assert.Equal(TimeSpan.FromMinutes(7.5).TotalSeconds, dial.Over.TotalSeconds, 1);
        Assert.False(dial.Loop);
    }

    [Fact]
    public void Paused_countdown_stands_still()
    {
        var timer = TimerParser.Pause(TimerParser.ToActivity(TimeSpan.FromMinutes(10), Now.AddMinutes(-5)), Now);
        var dial = TimerDial.Of(timer, false, Now.AddMinutes(3))!;
        Assert.Equal(180, dial.Angle, 3);
        Assert.Equal(0, dial.Turn);
    }

    [Fact]
    public void Finished_points_at_twelve()
        => Assert.Equal(360, TimerDial.Of(TimerParser.ToActivity(TimeSpan.FromMinutes(1), Now.AddMinutes(-2)), true, Now)!.Angle);

    [Fact]
    public void Stopwatch_laps_every_minute()
    {
        var watch = TimerParser.ToStopwatch(Now.AddSeconds(-75), "x");
        var dial = TimerDial.Of(watch, false, Now)!;
        Assert.Equal(90, dial.Angle, 3);
        Assert.True(dial.Loop);
        Assert.Equal(TimerDial.StopwatchLap, dial.Over);
    }

    [Fact]
    public void Not_a_timer_has_no_dial()
        => Assert.Null(TimerDial.Of(new ActivityState { RawKind = ActivityState.KindNotice }, false, Now));
}
