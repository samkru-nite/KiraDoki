using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class PillPaletteTests
{
    private static double L(Rgb c) => Oklch.From(c).L;

    [Theory]
    [InlineData(0x1C, 0x1C, 0x1C)]
    [InlineData(0x80, 0x40, 0x20)]
    [InlineData(0x1D, 0x35, 0x57)]
    [InlineData(0xF9, 0xF9, 0xF9)]
    public void Oklch_round_trips(byte r, byte g, byte b)
    {
        var back = Oklch.ToRgb(Oklch.From(new Rgb(r, g, b)));
        Assert.InRange(back.R, r - 1, r + 1);
        Assert.InRange(back.G, g - 1, g + 1);
        Assert.InRange(back.B, b - 1, b + 1);
    }

    [Fact]
    public void A_dark_taskbar_gets_a_lighter_pill_and_a_lighter_hover_and_edge()
    {
        var bar = new Rgb(0x1C, 0x1C, 0x1C);
        var p = PillPalette.For(bar);
        Assert.True(p.Dark);
        Assert.InRange(L(p.Rest) - L(bar), 0.055, 0.065);
        Assert.True(L(p.Hover) > L(p.Rest));
        Assert.True(L(p.ChipHover) > L(p.Chip) && L(p.Chip) > L(p.Rest));
        Assert.True(L(p.Edge) > L(p.Rest));
        Assert.Equal(p.Rest.R, p.Rest.G);   // a grey taskbar stays grey
    }

    [Fact]
    public void A_light_taskbar_gets_a_near_white_pill_and_darker_steps()
    {
        var p = PillPalette.For(new Rgb(0xEE, 0xF0, 0xF3));
        Assert.False(p.Dark);
        Assert.InRange(L(p.Rest), 0.99, 1);
        Assert.True(L(p.Hover) < L(p.Rest));
        Assert.True(L(p.Chip) < L(p.Rest));
        Assert.True(L(p.Edge) < L(p.Rest));
    }

    [Fact]
    public void A_white_taskbar_gets_a_darker_pill()
    {
        var bar = new Rgb(255, 255, 255);
        Assert.True(L(PillPalette.For(bar).Rest) < L(bar));
    }

    [Fact]
    public void A_coloured_taskbar_keeps_its_hue_with_less_chroma()
    {
        var bar = Oklch.From(new Rgb(0x1D, 0x35, 0x57));
        var pill = Oklch.From(PillPalette.For(new Rgb(0x1D, 0x35, 0x57)).Rest);
        Assert.InRange(pill.H, bar.H - 3, bar.H + 3);
        Assert.InRange(pill.C, bar.C * 0.6, bar.C * 0.8);
    }

    [Theory]
    [InlineData(0.5, 0.56)]
    [InlineData(0.97, 0.995)]   // stops at near-white
    [InlineData(0.99, 0.93)]    // already there: darker
    public void Step_goes_lighter_until_near_white(double from, double to)
        => Assert.Equal(to, PillPalette.Step(from, 0.06), 3);
}
