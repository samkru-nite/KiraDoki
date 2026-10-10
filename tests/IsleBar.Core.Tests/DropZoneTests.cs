using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public class DropZoneTests
{
    private const double Scale = 2;
    private static readonly int Ball = PillGeometry.Px(PillGeometry.BallBox, Scale);   // 64
    private static readonly int Wide = PillGeometry.Px(DropZone.Wide, Scale);

    [Fact]
    public void With_the_big_pill_out_it_is_the_pills()
        => Assert.Equal((100, 700), DropZone.Span(BarPosition.Center, 100, 700, big: true, Scale));

    [Fact]
    public void On_the_left_it_reaches_where_the_big_pill_comes_out()
        => Assert.Equal((100, 100 + Wide), DropZone.Span(BarPosition.Left, 100, 100 + Ball, big: false, Scale));

    [Fact]
    public void In_the_middle_it_is_centred_on_the_circle()
    {
        var (left, right) = DropZone.Span(BarPosition.Center, 1000, 1000 + Ball, big: false, Scale);
        Assert.Equal(Wide, right - left);
        Assert.Equal(1000 + (Ball / 2), (left + right) / 2);
    }

    [Fact]
    public void Set_by_hand_it_is_three_circles_round_the_circle()
        => Assert.Equal((1000 - Ball, 1000 + (2 * Ball)), DropZone.Span(BarPosition.Manual, 1000, 1000 + Ball, big: false, Scale));

    [Fact]
    public void Never_narrower_than_the_pills()
        => Assert.Equal((100 - Ball, 2000), DropZone.Span(BarPosition.Manual, 100, 2000, big: false, Scale));   // many cells: to their end
}
