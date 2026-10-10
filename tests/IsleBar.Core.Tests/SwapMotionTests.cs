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

    [Theory]
    [InlineData(0.15, 1.003, 1.01)]    // snappy: ~0.6%
    [InlineData(0.3, 1.035, 1.055)]    // bouncy: ~4.6%
    public void The_bounce_is_one_overshoot_as_apple_measures_it(double bounce, double low, double high)
    {
        var peak = Enumerable.Range(1, 99).Max(i => SwapMotion.Spring(i / 100.0, bounce));
        Assert.InRange(peak, low, high);
        Assert.Equal(1, SwapMotion.Spring(1, bounce), 6);
        var after = Enumerable.Range(1, 99).Select(i => SwapMotion.Spring(i / 100.0, bounce)).SkipWhile(p => p < peak).ToList();
        Assert.True(after.All(p => p >= 0.997));   // no second wobble worth seeing
        Assert.Equal(1, SwapMotion.Spring(1, 0), 6);   // bounce 0 (smooth) reaches the end too
    }

    [Fact]
    public void Past_the_end_the_card_scales_as_a_whole_instead_of_stretching()
    {
        var circle = new MotionBox(0, 0, 24, 32, 12);
        var pill = new MotionBox(40, 0, 220, 32, 16);
        Assert.Equal((pill, 1.0), SwapMotion.Settle(circle, pill, 1));
        var (box, scale) = SwapMotion.Settle(circle, pill, 1.05);
        Assert.True(scale > 1);
        Assert.Equal(box.Width / pill.Width, box.Height / pill.Height, 6);   // both ways alike: no skin pulled out on one side
        Assert.Equal(scale, box.Width / pill.Width, 6);
        var (back, small) = SwapMotion.Settle(pill, circle, 1.05);   // going down into a cell: smaller for a moment
        Assert.True(small < 1 && back.Width < circle.Width);
    }
}
