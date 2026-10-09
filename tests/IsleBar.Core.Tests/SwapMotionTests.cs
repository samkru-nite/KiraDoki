using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class SwapMotionTests
{
    [Fact]
    public void Easing_starts_at_zero_ends_at_one_and_never_goes_back()
    {
        Assert.Equal(0, SwapMotion.Ease(0), 3);
        Assert.Equal(1, SwapMotion.Ease(1), 3);
        var last = 0.0;
        for (var t = 0.05; t <= 1; t += 0.05)
        {
            var p = SwapMotion.Ease(t);
            Assert.True(p >= last);
            last = p;
        }

        Assert.True(SwapMotion.Ease(0.5) > 0.5);   // the standard curve is past halfway at half time
    }

    [Fact]
    public void The_circle_opens_into_the_pill_and_the_content_keeps_its_icon_centred()
    {
        var circle = SwapMotion.CircleAt(300, 7);
        Assert.Equal(new MotionBox(288, 11, 24, 24, 12), circle);
        var pill = new MotionBox(0, 7, 220, 32, 16);
        Assert.Equal(circle, SwapMotion.Between(circle, pill, 0));
        Assert.Equal(pill, SwapMotion.Between(circle, pill, 1));
        Assert.Equal(9, SwapMotion.Between(circle, pill, 0.5).Y, 6);   // a straight line: halfway between 11 and 7

        Assert.Equal(300 - 16, SwapMotion.ContentX(circle, pill, 16, 0));   // icon at x 16 sits on the cell's centre
        Assert.Equal(0, SwapMotion.ContentX(circle, pill, 16, 1));
    }

    [Fact]
    public void The_bounce_is_one_light_overshoot()
    {
        var peak = Enumerable.Range(1, 99).Max(i => SwapMotion.Move(i / 100.0, bounce: true));
        Assert.InRange(peak, 1.02, 1.035);
        Assert.Equal(1, SwapMotion.Move(1, bounce: true), 6);
        var after = Enumerable.Range(1, 99).Select(i => SwapMotion.Move(i / 100.0, bounce: true)).SkipWhile(p => p < peak).ToList();
        Assert.True(after.All(p => p >= 0.999));   // back to 1 without dipping under: no second wobble
    }
}
