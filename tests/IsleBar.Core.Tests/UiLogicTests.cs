using IsleBar.Core.Supervisor;
using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>
/// Pure calculations that were buried inside the app (WinUI) are moved to Core and checked here.
/// WinUI can't be built on the server, so before the move these were <b>things we could only learn by going to the PC</b>.
/// </summary>
public sealed class UiLogicTests
{
    // ---------- Choosing the text colour ----------

    [Fact]
    public void Brightness_is_the_average()
    {
        Assert.Equal(0, ThemePalette.Brightness(new Rgb(0, 0, 0)));
        Assert.Equal(255, ThemePalette.Brightness(new Rgb(255, 255, 255)));
        Assert.Equal(100, ThemePalette.Brightness(new Rgb(50, 100, 150)));
    }

    [Theory]
    [InlineData(255, 255, 255, true)]   // white background → dark text
    [InlineData(0, 0, 0, false)]        // black background → light text
    [InlineData(141, 141, 141, true)]   // just above the threshold
    [InlineData(140, 140, 140, false)]  // the threshold itself is not included
    public void Lightness_is_decided_by_actual_background_brightness(byte r, byte g, byte b, bool light)
        => Assert.Equal(light, ThemePalette.IsLightBackground(new Rgb(r, g, b)));

    [Fact]
    public void Light_background_gets_dark_text()
    {
        var accent = new Rgb(0, 0x78, 0xD4);
        var set = ThemePalette.ForBackground(new Rgb(0xF3, 0xF3, 0xF3), accent);

        Assert.True(set.Light);
        Assert.Equal(accent, set.Accent);
        // Text must be darker than the background to be visible — if this flips, the text disappears
        Assert.True(ThemePalette.Brightness(set.Foreground) < ThemePalette.Brightness(new Rgb(0xF3, 0xF3, 0xF3)));
    }

    [Fact]
    public void Default_blue_accent_is_lightened_on_dark_and_kept_on_light()
    {
        var blue = new Rgb(0, 0x78, 0xD4);   // Windows default accent — hard to see on the dark pill (design check 10-01)
        Assert.Equal(blue, ThemePalette.For(true, blue).Accent);
        var dark = ThemePalette.For(false, blue).Accent;
        Assert.True(ThemePalette.Brightness(dark) >= 150);
        Assert.True(dark.B > dark.R);   // still blue, just lighter
        var pale = new Rgb(0xF0, 0xE0, 0x90);
        Assert.True(ThemePalette.Brightness(ThemePalette.For(true, pale).Accent) <= 150);   // too light for the light pill → darkened
        Assert.Equal(pale, ThemePalette.For(false, pale).Accent);
    }

    [Fact]
    public void Dark_background_gets_light_text()
    {
        var bg = new Rgb(0x20, 0x20, 0x20);
        var set = ThemePalette.ForBackground(bg, new Rgb(0, 0x78, 0xD4));

        Assert.False(set.Light);
        Assert.True(ThemePalette.Brightness(set.Foreground) > ThemePalette.Brightness(bg));
    }

    [Fact]
    public void Text_never_blends_into_any_background()
    {
        // Sweep 0–255 and check there's no range where the contrast flips
        for (var v = 0; v <= 255; v += 5)
        {
            var bg = new Rgb((byte)v, (byte)v, (byte)v);
            var set = ThemePalette.ForBackground(bg, new Rgb(0, 0x78, 0xD4));
            var gap = Math.Abs(ThemePalette.Brightness(set.Foreground) - ThemePalette.Brightness(bg));
            Assert.True(gap > 60, $"밝기 {v} 에서 글자와 바탕이 너무 비슷하다 (차이 {gap:F0})");
        }
    }

    [Fact]
    public void Accent_colour_is_read_as_reversed_ABGR()
    {
        // 0xFFD47800 → B=0x00, G=0x78, R=0xD4
        var c = ThemePalette.AccentFromDwm(ThemePalette.DefaultAccentValue);
        Assert.Equal(new Rgb(0x00, 0x78, 0xD4), c);
    }

    // ---------- The two pills in the window ----------

    [Fact]
    public void Big_pill_then_gap_then_small_pill_at_200_percent()
    {
        var l = PillGeometry.Compute(2.0, big: true, smallLeft: false, PillGeometry.SmallWidth(0), round: true);
        Assert.Equal(2 * (220 + 8 + 32), l.Width);
        Assert.Equal(2 * (32 + 14), l.Height);
        Assert.Equal(new PillRect(0, 14, 441, 79, 64), l.Big);
        Assert.Equal(new PillRect(456, 14, 521, 79, 64), l.Small);
    }

    [Fact]
    public void Small_pill_on_the_left_comes_first()
    {
        var l = PillGeometry.Compute(1.0, big: true, smallLeft: true, 32, round: false);
        Assert.Equal(new PillRect(0, 7, 33, 40, 16), l.Small);
        Assert.Equal(new PillRect(40, 7, 261, 40, 16), l.Big);
    }

    [Fact]
    public void Without_a_card_only_the_small_pill_shows()
    {
        var l = PillGeometry.Compute(1.5, big: false, smallLeft: false, 32, round: true);
        Assert.Null(l.Big);
        Assert.Equal(48, l.Width);
        Assert.Equal(0, l.Small.Left);
        Assert.Equal(48, l.Small.Diameter);
    }

    [Fact]
    public void Odd_scales_line_the_pieces_up_with_the_total()
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.25 })
        {
            var l = PillGeometry.Compute(scale, big: true, smallLeft: false, 32, round: true);
            Assert.Equal(l.Width + 1, l.Small.Right);   // the small pill ends at the window's edge
        }
    }

    [Fact]
    public void Small_pill_is_the_circle_alone_or_cells_line_and_circle()
    {
        Assert.Equal(32, PillGeometry.SmallWidth(0));
        Assert.Equal(4 + 24 + 1 + 32, PillGeometry.SmallWidth(1));
        Assert.Equal(4 + (24 * 4) + 1 + 32, PillGeometry.SmallWidth(4));
    }

    // ---------- Deciding whether to exit on its own ----------

    [Fact]
    public void Does_nothing_when_healthy()
    {
        var v = HealthCheck.Check(windowAlive: true, attachedTaskbar: 10, currentTaskbar: 10,
            startedLight: true, nowLight: true);

        Assert.True(v.Healthy);
        Assert.Equal(HealthProblem.None, v.Problem);
    }

    [Fact]
    public void Window_gone_is_abnormal_exit()
    {
        var v = HealthCheck.Check(false, 10, 10, true, true);

        Assert.Equal(HealthProblem.WindowGone, v.Problem);
        Assert.Equal(ExitCodes.Abnormal, v.ExitCode);
        Assert.True(ExitCodes.ShouldRelaunch(v.ExitCode));
        Assert.Equal(TimeSpan.Zero, v.SettleDelay);
    }

    [Fact]
    public void Explorer_restart_is_abnormal_exit()
    {
        var v = HealthCheck.Check(true, attachedTaskbar: 10, currentTaskbar: 99, startedLight: true, nowLight: true);

        Assert.Equal(HealthProblem.ExplorerRestarted, v.Problem);
        Assert.Equal(ExitCodes.Abnormal, v.ExitCode);
    }

    [Fact]
    public void Different_taskbar_is_ignored_before_attaching()
    {
        // attachedTaskbar 0 = not attached yet. Exiting here would kill it right after launch
        var v = HealthCheck.Check(true, attachedTaskbar: 0, currentTaskbar: 99, startedLight: true, nowLight: true);
        Assert.True(v.Healthy);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Theme_change_restarts_after_waiting_2_seconds(bool started, bool now)
    {
        var v = HealthCheck.Check(true, 10, 10, started, now);

        Assert.Equal(HealthProblem.ThemeChanged, v.Problem);
        Assert.Equal(ExitCodes.RestartRequested, v.ExitCode);
        Assert.Equal(TimeSpan.FromSeconds(2), v.SettleDelay);   // launching before the colours finish changing locks in the wrong colours
    }

    [Fact]
    public void Display_scale_change_restarts_after_waiting()
    {
        var v = HealthCheck.Check(true, 10, 10, true, true, startedDpi: 192, nowDpi: 168);
        Assert.Equal(HealthProblem.DpiChanged, v.Problem);
        Assert.Equal(ExitCodes.RestartRequested, v.ExitCode);
        Assert.Equal(TimeSpan.FromSeconds(2), v.SettleDelay);
    }

    [Theory]
    [InlineData(0u, 168u)]   // DPI unknown at start (no taskbar yet) — nothing to compare
    [InlineData(192u, 0u)]   // taskbar momentarily gone — the Explorer check handles that
    [InlineData(192u, 192u)]
    public void Unknown_or_same_scale_is_healthy(uint started, uint now)
        => Assert.True(HealthCheck.Check(true, 10, 10, true, true, started, now).Healthy);

    [Fact]
    public void Window_gone_takes_precedence_over_theme_change()
    {
        // Even if both apply, a missing window comes first
        var v = HealthCheck.Check(windowAlive: false, 10, 99, startedLight: true, nowLight: false);
        Assert.Equal(HealthProblem.WindowGone, v.Problem);
    }

    [Theory]
    [InlineData(ExitCodes.UserQuit, false)]
    [InlineData(ExitCodes.Abnormal, true)]
    [InlineData(ExitCodes.RestartRequested, true)]
    [InlineData(ExitCodes.AlreadyRunning, false)]
    [InlineData(-1073741189, true)]   // 0xC000027B — died from an exception WinUI couldn't handle
    [InlineData(1, true)]
    public void Relaunches_except_when_quit_or_already_running(int code, bool relaunch)
        => Assert.Equal(relaunch, ExitCodes.ShouldRelaunch(code));

    [Fact]
    public void Glyph_mask_takes_the_light_spark_off_an_orange_tile()
    {
        // 64×64 orange tile with a white 8-wide bar in the middle
        const int n = 64;
        var px = new byte[n * n * 4];
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var i = ((y * n) + x) * 4;
                var white = x is >= 28 and < 36 && y is >= 8 and < 56;
                (px[i], px[i + 1], px[i + 2], px[i + 3]) = white ? ((byte)250, (byte)250, (byte)250, (byte)255) : ((byte)87, (byte)119, (byte)217, (byte)255);
            }
        }

        var mask = GlyphMask.Build(px, n, n, 16)!;
        Assert.True(mask[(8 * 16) + 8] > 100);    // the bar is the shape (centre)
        Assert.Equal(0, mask[(8 * 16) + 1]);      // the tile around it is not
    }
}
