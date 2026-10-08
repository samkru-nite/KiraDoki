namespace IsleBar.Core.Ui;

/// <summary>A point of an outline (dip).</summary>
public readonly record struct OutlinePoint(double X, double Y);

/// <summary>
/// <b>Smooth ("squircle") corners</b> — Figma's corner smoothing, the version the user picked (TODO 7.9 F, 10-08 rounds 11–12).
/// A plain round corner joins its straight edges with a sudden change of bend, which reads as a crease; here each corner eases in
/// over a longer stretch: straight edge → cubic → an arc of the corner circle → cubic → straight edge.
/// <para>
/// Per corner (r = radius, s = smoothing): it takes p = (1+s)·r of each edge; θ = 45°·s, α = 90°·(1−s); the arc's chord step is
/// sin(α/2)·r·√2 on each axis; c = r·tan((90°−α)/4)·cos θ; d = c·tan θ; b = (p − arc − c − d)/3; a = 2b. The corner is the cubic
/// (a,0) (a+b,0) (a+b+c,d), the arc on the corner circle, then the mirror cubic down to the next edge.
/// </para>
/// <para>
/// A pill (r = half the height) has no room for p on its short sides: the user's preview drew the two corners overlapping and let
/// the fill cover it, which comes out as each corner cut where it crosses the middle of the side. That cut is made here, so the
/// outline never crosses itself (rings drawn from it stay clean). Near a circle (a 24 cell, the 32 circle-only small pill) the
/// smoothing fades to 0 so it stays a true circle — a circle has no crease to smooth.
/// </para>
/// Points are sampled densely enough to draw as a polygon; the outline runs clockwise from the top edge.
/// </summary>
public static class SmoothCorner
{
    /// <summary>The smoothing the user chose: 60% (iOS-like).</summary>
    public const double Smoothing = 0.6;

    private const int CurveSteps = 12;
    private const int ArcSteps = 10;

    /// <summary>The outline of a <paramref name="width"/> × <paramref name="height"/> box with corners of <paramref name="radius"/>, from (x, y).</summary>
    public static IReadOnlyList<OutlinePoint> Outline(double x, double y, double width, double height, double radius, double smoothing = Smoothing)
    {
        var w = Math.Max(0, width);
        var h = Math.Max(0, height);
        var r = Math.Clamp(radius, 0, Math.Min(w, h) / 2);
        var points = new List<OutlinePoint>();
        if (w <= 0 || h <= 0)
        {
            return points;
        }

        if (r <= 0)
        {
            points.Add(new(x, y));
            points.Add(new(x + w, y));
            points.Add(new(x + w, y + h));
            points.Add(new(x, y + h));
            return points;
        }

        // no straight edge to ease into → no smoothing (a circle stays a circle); full smoothing once the long side has room for p
        var s = smoothing <= 0 ? 0 : smoothing * Math.Clamp((Math.Max(w, h) - (2 * r)) / (2 * r * smoothing), 0, 1);
        var corner = TopRight(w, h, r, s);
        Add(points, corner, (px, py) => new(x + px, y + py), reverse: false);                // top right
        Add(points, corner, (px, py) => new(x + px, y + h - py), reverse: true);             // bottom right
        Add(points, corner, (px, py) => new(x + w - px, y + h - py), reverse: false);        // bottom left
        Add(points, corner, (px, py) => new(x + w - px, y + py), reverse: true);             // top left
        return points;
    }

    /// <summary>The same shape moved in by <paramref name="inset"/> on every side, its corners concentric (radius − inset).</summary>
    public static IReadOnlyList<OutlinePoint> Inset(double width, double height, double radius, double inset, double smoothing = Smoothing)
        => Outline(inset, inset, width - (2 * inset), height - (2 * inset), Math.Max(0, radius - inset), smoothing);

    private static void Add(List<OutlinePoint> into, List<OutlinePoint> corner, Func<double, double, OutlinePoint> map, bool reverse)
    {
        for (var i = 0; i < corner.Count; i++)
        {
            var p = corner[reverse ? corner.Count - 1 - i : i];
            var q = map(p.X, p.Y);
            if (into.Count == 0 || Math.Abs(into[^1].X - q.X) > 1e-9 || Math.Abs(into[^1].Y - q.Y) > 1e-9)
            {
                into.Add(q);
            }
        }
    }

    /// <summary>The top-right corner of a w × h box at the origin, from the top edge to the right edge, cut at the sides' middles.</summary>
    private static List<OutlinePoint> TopRight(double w, double h, double r, double s)
    {
        var deg = Math.PI / 180;
        var p = (1 + s) * r;
        var theta = 45 * s;
        var alpha = 90 * (1 - s);
        var arc = Math.Sin(alpha / 2 * deg) * r * Math.Sqrt(2);
        var c = r * Math.Tan((90 - alpha) / 4 * deg) * Math.Cos(theta * deg);
        var d = c * Math.Tan(theta * deg);
        var b = (p - arc - c - d) / 3;
        var a = 2 * b;

        var raw = new List<OutlinePoint>();
        var p0 = new OutlinePoint(w - p, 0);
        var p3 = new OutlinePoint(p0.X + a + b + c, d);
        Cubic(raw, p0, new(p0.X + a, 0), new(p0.X + a + b, 0), p3);
        var p4 = new OutlinePoint(p3.X + arc, p3.Y + arc);
        var centre = new OutlinePoint(w - r, r);
        var from = Math.Atan2(p3.Y - centre.Y, p3.X - centre.X);
        var to = Math.Atan2(p4.Y - centre.Y, p4.X - centre.X);
        for (var i = 1; i <= ArcSteps; i++)
        {
            var t = from + ((to - from) * i / ArcSteps);
            raw.Add(new(centre.X + (r * Math.Cos(t)), centre.Y + (r * Math.Sin(t))));
        }

        Cubic(raw, p4, new(p4.X + d, p4.Y + c), new(p4.X + d, p4.Y + b + c), new(w, p));

        // cut where the corner passes the middle of a side it hasn't room on (a pill's round ends)
        var cut = new List<OutlinePoint>();
        for (var i = 0; i < raw.Count; i++)
        {
            var q = raw[i];
            if (q.X < w / 2)
            {
                if (i + 1 < raw.Count && raw[i + 1].X >= w / 2)
                {
                    cut.Add(Between(q, raw[i + 1], (w / 2 - q.X) / (raw[i + 1].X - q.X)));
                }

                continue;
            }

            if (q.Y > h / 2)
            {
                if (i > 0 && raw[i - 1].Y <= h / 2)
                {
                    cut.Add(Between(raw[i - 1], q, (h / 2 - raw[i - 1].Y) / (q.Y - raw[i - 1].Y)));
                }

                break;
            }

            cut.Add(q);
        }

        return cut;
    }

    private static OutlinePoint Between(OutlinePoint a, OutlinePoint b, double t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

    private static void Cubic(List<OutlinePoint> into, OutlinePoint p0, OutlinePoint p1, OutlinePoint p2, OutlinePoint p3)
    {
        if (into.Count == 0)
        {
            into.Add(p0);
        }

        for (var i = 1; i <= CurveSteps; i++)
        {
            var t = (double)i / CurveSteps;
            var u = 1 - t;
            into.Add(new(
                (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X),
                (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y)));
        }
    }
}
