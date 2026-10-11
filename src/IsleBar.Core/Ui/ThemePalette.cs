namespace IsleBar.Core.Ui;

/// <summary>A single color. Kept separately in Core so it is not tied to a window library.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Rules and palettes for choosing text color from background brightness.
///
/// <para><b>Why this lives in Core.</b> During a theme switch the background changed but the text color did not follow,
/// and <b>text actually vanished</b> (HANDOFF lesson). If that judgement is buried in window code
/// it can only be checked by launching on a PC. The math is pulled out and verified here.</para>
/// </summary>
public static class ThemePalette
{
    /// <summary>Above this brightness the background counts as light and dark text is used.</summary>
    public const double LightThreshold = 140.0;

    /// <summary>Average brightness (0–255).</summary>
    public static double Brightness(Rgb color) => (color.R + color.G + color.B) / 3.0;

    /// <summary>
    /// Decides from <b>the background color actually drawn</b> — not from the setting.
    /// This is the key to keeping text from blending into the background even mid theme switch.
    /// </summary>
    public static bool IsLightBackground(Rgb background) => Brightness(background) > LightThreshold;

    /// <summary>
    /// Palette matching the pill's colour (TODO ⑤ 全岛排版, user 10-10): the main text is whichever of white / near-black stands out
    /// more on it (was: brightness over 140), the grey is <see cref="HintOn"/> — so on a coloured taskbar it carries the pill's tint
    /// and never sinks below <see cref="HintContrast"/>.
    /// </summary>
    public static ThemeColorSet ForBackground(Rgb background, Rgb accent)
    {
        var light = Contrast(DarkText, background) > Contrast(LightText, background);
        var set = For(light, accent);
        return set with { Hint = HintOn(background, set.Foreground, light) };
    }

    /// <summary>The main text on a dark pill / on a light pill.</summary>
    public static readonly Rgb LightText = new(0xFF, 0xFF, 0xFF);

    /// <inheritdoc cref="LightText"/>
    public static readonly Rgb DarkText = new(0x1A, 0x1A, 0x1A);

    /// <summary>The least contrast the grey keeps on the pill (14 px text, WCAG AA).</summary>
    public const double HintContrast = 4.5;

    /// <summary>
    /// The grey on <paramref name="background"/>: the main text with the pill showing through (Windows' secondary text is the main
    /// colour with some transparency). How much shows through is set so that on Windows' own dark / light taskbar it is exactly the
    /// grey used before (#C5C5C5 / #5C5D5F) — nothing changes there; a coloured pill tints it. Too faint → pushed toward the main colour.
    /// </summary>
    public static Rgb HintOn(Rgb background, Rgb main, bool light)
    {
        var opacity = light ? LightHintOpacity : DarkHintOpacity;
        var hint = Mix(background, main, opacity);
        for (var i = 0; i < 40 && Contrast(hint, background) < HintContrast; i++)
        {
            opacity = Math.Min(1, opacity + 0.02);
            hint = Mix(background, main, opacity);
        }

        return hint;
    }

    // how much of the main colour the grey is, from the old fixed greys on Windows' default taskbars
    private static readonly double DarkHintOpacity = Share(PillPalette.For(new Rgb(0x20, 0x20, 0x20)).Rest, LightText, new Rgb(0xC5, 0xC5, 0xC5));
    private static readonly double LightHintOpacity = Share(PillPalette.For(new Rgb(0xDF, 0xE6, 0xF3)).Rest, DarkText, new Rgb(0x5C, 0x5D, 0x5F));

    private static double Share(Rgb background, Rgb main, Rgb grey)
        => Math.Clamp((Brightness(grey) - Brightness(background)) / (Brightness(main) - Brightness(background)), 0, 1);

    /// <summary><paramref name="to"/> laid over <paramref name="from"/> at <paramref name="amount"/> (0 = from, 1 = to).</summary>
    public static Rgb Mix(Rgb from, Rgb to, double amount)
    {
        byte One(byte a, byte b) => (byte)Math.Round(a + ((b - a) * amount));
        return new Rgb(One(from.R, to.R), One(from.G, to.G), One(from.B, to.B));
    }

    /// <summary>WCAG contrast ratio, 1–21.</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static double Luminance(Rgb c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    /// <summary>
    /// The accent made readable on the pill: Windows' default blue (#0078D4) nearly vanished on the dark pill — the ⌄ that marks
    /// changed launch options, the progress line and the input caret all use it (design check 10-01). Like Windows' own lighter
    /// accent shades in dark mode, a too-dark accent is mixed toward white on dark, a too-light one toward black on light.
    /// </summary>
    public static Rgb ReadableAccent(bool light, Rgb accent)
    {
        const double Limit = 150;   // average brightness: dark pill wants at least this, light pill at most this
        var c = accent;
        for (var i = 0; i < 8 && (light ? Brightness(c) > Limit : Brightness(c) < Limit); i++)
        {
            var target = light ? (byte)0 : (byte)255;
            c = new Rgb(Mix(c.R, target), Mix(c.G, target), Mix(c.B, target));
        }

        return c;

        static byte Mix(byte from, byte to) => (byte)Math.Round(from + ((to - from) * 0.2));
    }

    /// <summary>Light/dark palettes.</summary>
    public static ThemeColorSet For(bool light, Rgb accent)
    {
        accent = ReadableAccent(light, accent);
        return light
            ? new ThemeColorSet(
                Light: true, accent,
                Foreground: new Rgb(0x1A, 0x1A, 0x1A), Hint: new Rgb(0x5C, 0x5D, 0x5F), Icon: new Rgb(0x1F, 0x1F, 0x1F),
                Flyout: new Rgb(0xF9, 0xF9, 0xF9), Divider: new Rgb(0xE5, 0xE5, 0xE5),
                Ok: new Rgb(0x0F, 0x7B, 0x0F), Bad: new Rgb(0xC4, 0x2B, 0x1C))
            : new ThemeColorSet(
                Light: false, accent,
                Foreground: new Rgb(0xFF, 0xFF, 0xFF), Hint: new Rgb(0xC5, 0xC5, 0xC5), Icon: new Rgb(0xFF, 0xFF, 0xFF),
                Flyout: new Rgb(0x2C, 0x2C, 0x2C), Divider: new Rgb(0x3D, 0x3D, 0x3D),
                Ok: new Rgb(0x6C, 0xCB, 0x5F), Bad: new Rgb(0xFF, 0x99, 0xA4));
    }

    /// <summary>
    /// The accent color in the Windows registry (DWM) is stored as <b>ABGR</b> — read it in reverse order.
    /// Getting this wrong once swaps red and blue.
    /// </summary>
    public static Rgb AccentFromDwm(int value)
        => new((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));

    /// <summary>Value used when the accent color cannot be read (Windows default blue).</summary>
    public static int DefaultAccentValue => unchecked((int)0xFFD4_7800);
}

/// <summary>One set of colors.</summary>
public sealed record ThemeColorSet(
    bool Light,
    Rgb Accent,
    Rgb Foreground,
    Rgb Hint,
    Rgb Icon,
    Rgb Flyout,
    Rgb Divider,
    Rgb Ok,
    Rgb Bad);
