namespace IsleBar.Core.Ui;

/// <summary>
/// The pills' colours, worked out from the taskbar colour beside them (TODO 7.9 丙④, user 10-10; preview pill-colour.html).
/// In OKLCH (lightness as the eye sees it): the pill is a step lighter than the taskbar, same hue, chroma × 0.7; the hover, the
/// close button and the edge are steps off the pill. A step goes lighter while there's room below near-white (L 0.995); a colour
/// already at white goes a step darker — so on a light taskbar the pill is near-white like Windows 11's search box and its
/// hover / close button are darker. The same numbers with the transparency mode on or off (only the card differs).
/// </summary>
public static class PillPalette
{
    /// <summary>Pill lightness over the taskbar's.</summary>
    public const double PillStep = 0.06;

    /// <summary>The taskbar's chroma kept on the pill.</summary>
    public const double ChromaShare = 0.7;

    /// <summary>Hover lightness over the pill's; the close button takes 0.7 of it at rest, 1.2 under the pointer.</summary>
    public const double HoverStep = 0.04;

    /// <summary>The lightest a step goes.</summary>
    public const double Top = 0.995;

    /// <summary>The pill colours for a taskbar of colour <paramref name="taskbar"/>.</summary>
    public static PillColours For(Rgb taskbar)
    {
        var t = Oklch.From(taskbar);
        var dark = t.L < 0.6;
        var c = t.C * ChromaShare;
        var pill = Step(t.L, PillStep);
        Rgb At(double l) => Oklch.ToRgb(new Oklch(l, c, t.H));
        return new PillColours(
            dark,
            At(pill),
            At(Step(pill, HoverStep)),
            At(Step(pill, HoverStep * 0.7)),
            At(Step(pill, HoverStep * 1.2)),
            At(pill + (dark ? 0.045 : -0.06)));
    }

    /// <summary>A step lighter, stopping at near-white; a colour already there a step darker.</summary>
    public static double Step(double lightness, double step)
        => Top - lightness >= 0.01 ? Math.Min(Top, lightness + step) : lightness - step;
}

/// <summary>The pills' colours. <paramref name="Dark"/>: the taskbar is dark.</summary>
public readonly record struct PillColours(bool Dark, Rgb Rest, Rgb Hover, Rgb Chip, Rgb ChipHover, Rgb Edge);

/// <summary>A colour in OKLCH: lightness 0–1, chroma, hue in degrees (Björn Ottosson's OKLab, in polar form).</summary>
public readonly record struct Oklch(double L, double C, double H)
{
    public static Oklch From(Rgb rgb)
    {
        var r = ToLinear(rgb.R / 255.0);
        var g = ToLinear(rgb.G / 255.0);
        var b = ToLinear(rgb.B / 255.0);
        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));
        var lightness = (0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s);
        var a = (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s);
        var bb = (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s);
        var hue = ((Math.Atan2(bb, a) * 180 / Math.PI) + 360) % 360;
        return new Oklch(lightness, Math.Sqrt((a * a) + (bb * bb)), hue);
    }

    /// <summary>Back to a screen colour; outside the screen's colours the chroma is lowered until it fits.</summary>
    public static Rgb ToRgb(Oklch colour)
    {
        var c = colour with { L = Math.Clamp(colour.L, 0, 1) };
        for (var i = 0; i < 60; i++)
        {
            var lin = ToLinearRgb(c);
            if (lin.All(v => v >= -1e-4 && v <= 1 + 1e-4))
            {
                return Pack(lin);
            }

            c = c with { C = c.C * 0.93 };
        }

        return Pack(ToLinearRgb(c));
    }

    private static double[] ToLinearRgb(Oklch c)
    {
        var a = c.C * Math.Cos(c.H * Math.PI / 180);
        var b = c.C * Math.Sin(c.H * Math.PI / 180);
        var l = Math.Pow(c.L + (0.3963377774 * a) + (0.2158037573 * b), 3);
        var m = Math.Pow(c.L - (0.1055613458 * a) - (0.0638541728 * b), 3);
        var s = Math.Pow(c.L - (0.0894841775 * a) - (1.2914855480 * b), 3);
        return
        [
            (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s),
            (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s),
            (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s),
        ];
    }

    private static Rgb Pack(double[] lin)
    {
        static byte Byte(double v) => (byte)Math.Round(Math.Clamp(ToGamma(Math.Clamp(v, 0, 1)), 0, 1) * 255);
        return new Rgb(Byte(lin[0]), Byte(lin[1]), Byte(lin[2]));
    }

    private static double ToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static double ToGamma(double v) => v <= 0.0031308 ? v * 12.92 : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;
}
