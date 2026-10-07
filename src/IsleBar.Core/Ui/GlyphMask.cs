namespace IsleBar.Core.Ui;

/// <summary>
/// Extracts only the orange spark shape from an executable's icon (BGRA pixels) to build an alpha mask for a single-color icon.
/// Same rules as the Python version's <c>claude_glyph()</c> (user feedback 09-29: the icon looked different from the one made before).
///
/// The original shape is used as-is, never hand-drawn. Edges are blurred and re-thresholded (removing jaggies),
/// rays are thinned by one pixel to match the magnifier stroke width, then downscaled by area averaging (smooth even when small).
/// Only pure computation lives here so it can be tested without a window — the Win32 calls that read the icon are done by the app.
/// </summary>
public static class GlyphMask
{
    /// <summary>
    /// Pixels counted as orange: sufficiently opaque, with high red, clearly greater than blue.
    /// Opacity must be over half — icons read on Windows carry stray colors in nearly transparent edges (alpha 1),
    /// so filtering only 0 caught border dots as orange, enlarging the shape bounds and shrinking the spark (measured on PC 09-29).
    /// </summary>
    public static bool IsOrange(byte b, byte g, byte r, byte a) => a > 127 && r > 140 && r - b > 60;

    /// <summary>A light pixel: the white spark on the Claude app's orange tile.</summary>
    public static bool IsLight(byte b, byte g, byte r, byte a) => a > 127 && r > 200 && g > 190 && b > 180;

    /// <summary>
    /// When orange fills more than this share of its bounding box it is a tile behind the spark (the Claude desktop app's icon,
    /// user 10-07), not the spark itself (the command-line claude.exe's icon): the spark is then the light pixels on it.
    /// </summary>
    private const double TileShare = 0.6;

    /// <summary>
    /// Builds a <paramref name="size"/>×<paramref name="size"/> alpha mask from <paramref name="bgra"/>
    /// (<paramref name="width"/> wide, <paramref name="height"/> tall). Null if there is no orange at all.
    /// </summary>
    public static byte[]? Build(byte[] bgra, int width, int height, int size)
    {
        if (size <= 0 || width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            return null;
        }

        // 1) orange pixels → 255
        var mask = new byte[width * height];
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                if (IsOrange(bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]))
                {
                    mask[(y * width) + x] = 255;
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        if (right < 0)
        {
            return null;
        }

        var orange = mask.Count(v => v != 0);
        if (orange > TileShare * (right - left + 1) * (bottom - top + 1))
        {
            // a tile: take the light shape on it, within the tile
            int l2 = width, t2 = height, r2 = -1, b2 = -1;
            var light = new byte[width * height];
            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    var i = ((y * width) + x) * 4;
                    if (IsLight(bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]))
                    {
                        light[(y * width) + x] = 255;
                        l2 = Math.Min(l2, x);
                        t2 = Math.Min(t2, y);
                        r2 = Math.Max(r2, x);
                        b2 = Math.Max(b2, y);
                    }
                }
            }

            if (r2 >= 0)
            {
                (mask, left, top, right, bottom) = (light, l2, t2, r2, b2);   // nothing light on it: the orange shape itself
            }
        }

        // 2) crop to the shape and center it in a square (margin 48 — same as the original)
        int cw = right - left + 1, ch = bottom - top + 1;
        var side = Math.Max(cw, ch) + 48;
        var square = new byte[side * side];
        int ox = (side - cw) / 2, oy = (side - ch) / 2;
        for (var y = 0; y < ch; y++)
        {
            Array.Copy(mask, ((top + y) * width) + left, square, ((oy + y) * side) + ox, cw);
        }

        // 3) remove jaggies: blur (radius 3) → re-threshold at half
        square = Threshold(BoxBlur(square, side, 3), 127);

        // 4) thin rays by one pixel (3×3 minimum)
        square = Erode(square, side);

        // 5) downscale by area averaging
        return Downscale(square, side, size);
    }

    private static byte[] BoxBlur(byte[] src, int side, int radius)
    {
        var tmp = new byte[src.Length];
        var dst = new byte[src.Length];
        var window = (radius * 2) + 1;
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var sum = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var xx = Math.Clamp(x + k, 0, side - 1);
                    sum += src[(y * side) + xx];
                }

                tmp[(y * side) + x] = (byte)(sum / window);
            }
        }

        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var sum = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var yy = Math.Clamp(y + k, 0, side - 1);
                    sum += tmp[(yy * side) + x];
                }

                dst[(y * side) + x] = (byte)(sum / window);
            }
        }

        return dst;
    }

    private static byte[] Threshold(byte[] src, byte level)
    {
        var dst = new byte[src.Length];
        for (var i = 0; i < src.Length; i++)
        {
            dst[i] = src[i] > level ? (byte)255 : (byte)0;
        }

        return dst;
    }

    private static byte[] Erode(byte[] src, int side)
    {
        var dst = new byte[src.Length];
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                byte min = 255;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        var v = xx < 0 || yy < 0 || xx >= side || yy >= side ? (byte)0 : src[(yy * side) + xx];
                        min = Math.Min(min, v);
                    }
                }

                dst[(y * side) + x] = min;
            }
        }

        return dst;
    }

    private static byte[] Downscale(byte[] src, int side, int size)
    {
        var dst = new byte[size * size];
        var scale = (double)side / size;
        for (var y = 0; y < size; y++)
        {
            int y0 = (int)(y * scale), y1 = Math.Max(y0 + 1, (int)((y + 1) * scale));
            for (var x = 0; x < size; x++)
            {
                int x0 = (int)(x * scale), x1 = Math.Max(x0 + 1, (int)((x + 1) * scale));
                long sum = 0;
                var count = 0;
                for (var yy = y0; yy < Math.Min(y1, side); yy++)
                {
                    for (var xx = x0; xx < Math.Min(x1, side); xx++)
                    {
                        sum += src[(yy * side) + xx];
                        count++;
                    }
                }

                dst[(y * size) + x] = count == 0 ? (byte)0 : (byte)(sum / count);
            }
        }

        return dst;
    }
}
