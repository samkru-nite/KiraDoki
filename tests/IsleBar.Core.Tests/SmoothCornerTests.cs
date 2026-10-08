using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class SmoothCornerTests
{
    [Theory]
    [InlineData(220, 32, 16)]   // the big pill
    [InlineData(300, 200, 8)]   // a lifted card
    [InlineData(72, 28, 14)]    // the close button
    [InlineData(37, 32, 16)]    // a small pill with one cell, barely longer than tall
    public void The_outline_fills_its_box_and_never_folds(double w, double h, double r)
    {
        var o = SmoothCorner.Outline(0, 0, w, h, r);

        // a round end is cut where its two corners meet, a hair (≤ 0.2) short of the box — as the user's preview drew it
        Assert.InRange(o.Min(p => p.X), 0, 0.2);
        Assert.InRange(o.Max(p => p.X), w - 0.2, w);
        Assert.InRange(o.Min(p => p.Y), 0, 0.2);
        Assert.InRange(o.Max(p => p.Y), h - 0.2, h);

        // convex and turning one way all round (clockwise on screen) = no crease folding back, no crossing
        for (var i = 0; i < o.Count; i++)
        {
            var a = o[i];
            var b = o[(i + 1) % o.Count];
            var c = o[(i + 2) % o.Count];
            var cross = ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));
            Assert.True(cross >= -1e-6, $"turns back at point {i}");
        }
    }

    [Fact]
    public void A_square_with_half_its_side_as_radius_stays_a_circle()
    {
        foreach (var p in SmoothCorner.Outline(0, 0, 24, 24, 12))
        {
            Assert.Equal(12, Math.Sqrt(((p.X - 12) * (p.X - 12)) + ((p.Y - 12) * (p.Y - 12))), 2);
        }
    }

    [Fact]
    public void Without_smoothing_the_ends_are_plain_half_circles()
    {
        foreach (var p in SmoothCorner.Outline(0, 0, 220, 32, 16, smoothing: 0).Where(p => p.X > 204))
        {
            Assert.Equal(16, Math.Sqrt(((p.X - 204) * (p.X - 204)) + ((p.Y - 16) * (p.Y - 16))), 2);
        }
    }

    [Fact]
    public void A_smooth_corner_takes_more_of_the_edge_than_a_round_one()
    {
        // round: the 8 corner leaves the top edge 8 from the end; smooth: (1 + 0.6)·8 = 12.8 from it
        Assert.Contains(SmoothCorner.Outline(0, 0, 300, 200, 8), p => p.Y == 0 && Math.Abs(p.X - 287.2) < 1e-9);
        Assert.Contains(SmoothCorner.Outline(0, 0, 300, 200, 8, smoothing: 0), p => p.Y == 0 && Math.Abs(p.X - 292) < 1e-9);
    }

    [Fact]
    public void The_inset_sits_inside_and_is_concentric()
    {
        var ring = SmoothCorner.Inset(220, 32, 16, 2);
        Assert.InRange(ring.Min(p => p.X), 2, 2.2);
        Assert.InRange(ring.Max(p => p.X), 217.8, 218);
        Assert.InRange(ring.Min(p => p.Y), 2, 2.2);
        Assert.InRange(ring.Max(p => p.Y), 29.8, 30);
    }
}
