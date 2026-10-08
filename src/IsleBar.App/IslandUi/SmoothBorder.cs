using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace IsleBar.App.IslandUi;

/// <summary>
/// A Border with smooth corners (<see cref="SmoothCorner"/>, user 10-08): XAML's own CornerRadius draws plain round corners, whose
/// join with the straight edge reads as a crease. Draws a fill and an edge ring as two paths; anything added to
/// <see cref="Panel.Children"/> after construction sits on top, as in a Grid. The shape follows the control's size; inside an
/// animation call <see cref="SetSize"/> so the shape changes in the same frame as the size.
/// </summary>
public sealed partial class SmoothBorder : Grid
{
    // The paths sit on a Canvas: it takes no size and cuts nothing. Straight in the Grid, a path was laid out at its previous
    // shape's size and the new one cut off there (both pills' right ends came out flat), and sized by hand it held the Grid at
    // that size (the circle-only small pill stayed 37 wide, its right end cut by the window) — 10-08
    private readonly Canvas _paths = new() { IsHitTestVisible = false };
    private readonly Path _edge = new() { IsHitTestVisible = false };
    private readonly Path _fill = new() { IsHitTestVisible = false };
    private double _radius = 16;
    private double _edgeWidth;
    private double _w;
    private double _h;

    public SmoothBorder()
    {
        _paths.Children.Add(_edge);
        _paths.Children.Add(_fill);
        Children.Add(_paths);
        SizeChanged += (_, e) => Redraw(e.NewSize.Width, e.NewSize.Height);
    }

    /// <summary>The inside (Border's Background), inset by <see cref="EdgeWidth"/>.</summary>
    public Brush? Fill
    {
        get => _fill.Fill;
        set => _fill.Fill = value;
    }

    /// <summary>The ring along the outline (Border's BorderBrush), <see cref="EdgeWidth"/> wide.</summary>
    public Brush? Edge
    {
        get => _edge.Fill;
        set => _edge.Fill = value;
    }

    public double EdgeWidth
    {
        get => _edgeWidth;
        set
        {
            if (value == _edgeWidth)
            {
                return;   // set every frame by animations: only a real change redraws
            }

            _edgeWidth = value;
            Redraw(_w, _h);
        }
    }

    /// <summary>The corner radius (dip); half the height = a pill.</summary>
    public double Radius
    {
        get => _radius;
        set
        {
            _radius = value;
            Redraw(_w, _h);
        }
    }

    /// <summary>Sets the size and redraws now (not on the next layout pass).</summary>
    public void SetSize(double width, double height)
    {
        Width = width;
        Height = height;
        Redraw(width, height);
    }

    private void Redraw(double w, double h)
    {
        if (double.IsNaN(w) || double.IsNaN(h))
        {
            return;
        }

        _w = w;
        _h = h;
        var e = Math.Clamp(_edgeWidth, 0, Math.Min(w, h) / 2);
        _fill.Data = Geometry(new[] { SmoothCorner.Inset(w, h, _radius, e) });
        _edge.Data = e > 0
            ? Geometry(new[] { SmoothCorner.Outline(0, 0, w, h, _radius), SmoothCorner.Inset(w, h, _radius, e) })   // even-odd: the band between
            : null;
    }

    private static PathGeometry Geometry(IEnumerable<IReadOnlyList<OutlinePoint>> outlines)
    {
        var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
        foreach (var outline in outlines)
        {
            if (outline.Count < 3)
            {
                continue;
            }

            var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(outline[0].X, outline[0].Y), IsClosed = true, IsFilled = true };
            var line = new PolyLineSegment();
            for (var i = 1; i < outline.Count; i++)
            {
                line.Points.Add(new Windows.Foundation.Point(outline[i].X, outline[i].Y));
            }

            figure.Segments.Add(line);
            geometry.Figures.Add(figure);
        }

        return geometry;
    }
}
