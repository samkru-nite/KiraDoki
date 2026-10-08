using IsleBar.App.Interop;
using IsleBar.App.Tracking;
using IsleBar.Core.Island;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace IsleBar.App;

/// <summary>
/// Every change of what the pills show moves from where it was to where it goes (TODO 7.3 / 7.9 D, user 10-07): a card going
/// up from a cell or down into one, an icon coming or going, the big pill sliding as the small pill grows or shrinks. The click
/// on a task icon (swap) is one such change. Numbers live in <see cref="SwapMotion"/>.
/// <para>
/// How: the old and new scenes are laid out by <see cref="PillGeometry"/> (pure arithmetic — no waiting for XAML layout); the new
/// state is drawn under stand-ins, its card content snapshotted; then the real pills are hidden and stand-ins fly in
/// <c>FlyLayer</c> from old to new, frame by frame on the UI thread. Cards fly as snapshots of their content (<c>CardRow</c>, no
/// background) in drawn boxes. The window spans both places and isn't clipped while they fly.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A transition is running: the tick doesn't redraw, the small pill and the window don't change.</summary>
    private bool _flying;

    /// <summary>The card the big pill draws now (null = hidden or showing the input box / a drop hint).</summary>
    private Card? _bigCard;

    /// <summary>The cards the small pill's cells draw now, left to right (⋯ not included).</summary>
    private readonly List<Card> _renderedCells = [];

    /// <summary>Whether the small pill draws ⋯ now.</summary>
    private bool DotsDrawn => TaskCells.Children.OfType<Grid>().Any(c => c.Tag as string == DotsTag);

    /// <summary>The card the big pill should draw for the state decided now.</summary>
    private Card? WantedBig => !_typing && _compose is null && !_dropHover && _snapshot.IsActive ? _shownCard : null;

    /// <summary>The scene to draw differs from the one drawn: another card on the big pill, or other cells.</summary>
    private bool SceneChanged()
        => WantedBig?.Key != _bigCard?.Key || DotsDrawn != _dotsOn
           || !_renderedCells.Select(c => c.Key).SequenceEqual(_cellCards.Select(c => c.Key));

    /// <summary>A task icon was clicked: its card goes on the big pill and the card there takes the clicked cell.</summary>
    private void OnTaskCellClicked(string key)
    {
        if (_flying)
        {
            return;
        }

        var old = _shownCard;
        _slots.Swap(key, old?.Key, old?.Type == CardType.Temporary);
        _cards.Choose(key, DateTimeOffset.UtcNow);
        _shownCard = _cards.Shown;
        _snapshot = _shownCard is { } shown ? new IslandSnapshot(shown.State, _cards.Cards.Count - 1) : IslandSnapshot.Empty;
        _animator.Classify(_snapshot);   // the transition is the animation: the tick mustn't add its own
        ComputeCells();
        _ = Transition(hoverKey: key);
    }

    /// <summary>
    /// Draws the state already decided (<see cref="_shownCard"/>, <see cref="_cellCards"/>) and animates the change.
    /// <paramref name="hoverKey"/> = the clicked cell (its lit look is kept at the cell end of the flight).
    /// </summary>
    private async Task Transition(string? hoverKey)
    {
        if (_flying)
        {
            return;
        }

        if (!SceneChanged() || _typing || _compose is not null || _dropHover || !_placement.Visible || !_taskbar.IsAttached)
        {
            RenderTaskCells();
            RenderIsland();
            return;
        }

        _flying = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var phases = new System.Text.StringBuilder();
        void Phase(string name) => phases.Append($" {name} {clock.ElapsedMilliseconds}");
        var style = SwapMotion.Parse(_settings.SwapAnimation);
        var bounce = _settings.Bounce;
        var left = _settings.BallSide == "left";
        try
        {
            var oldCells = _renderedCells.ToList();
            var oldDots = DotsDrawn;
            var oldBig = _bigCard is not null && PillBody.Visibility == Visibility.Visible ? _bigCard : null;
            var oldWinPx = _placement.X;
            var winY = _placement.Y;

            // where the new layout will sit, worked out before drawing it — so the window can grow first
            var next = PillGeometry.Compute(DpiSetup.Scale, WantedBig is not null, left, PillGeometry.SmallWidth(_cellCards.Count + (_dotsOn ? 1 : 0)), _settings.PillCorners != "small");
            var spot = _tracker.PlaceNow(next.Width);
            if (!spot.Visible)
            {
                return;
            }

            // The window spans the old and the new place, plus room on the right for the bounce to overshoot (user 10-07: things
            // looked cut). It grows before anything flies: a resized window takes a frame or two to show its new size. Its left edge
            // only moves when the bar itself moves (centre / tray positions); the content keeps its place on screen.
            var spanLeftPx = Math.Min(oldWinPx, spot.X);
            var spanRightPx = Math.Max(oldWinPx + _layout.Width, spot.X + next.Width) + DpiSetup.Px(SwapMotion.Overshoot);
            var oldShift = (oldWinPx - spanLeftPx) / DpiSetup.Scale;
            var newShift = (spot.X - spanLeftPx) / DpiSetup.Scale;
            var oldLayout = _layout;
            var dx = oldWinPx - spanLeftPx;
            PillRegion.ApplyRects(_taskbar.OuterWindow, new[] { oldLayout.Big, (PillRect?)oldLayout.Small }
                .OfType<PillRect>().Select(r => (r.Left + dx, r.Top, r.Right + dx, r.Bottom)).ToList());
            PillRow.Margin = new Thickness(oldShift, PillGeometry.ArcRoom, 0, 0);
            _taskbar.PlaceAt(spanLeftPx, winY, spanRightPx - spanLeftPx, _layout.Height);
            var spanWidth = (spanRightPx - spanLeftPx) / DpiSetup.Scale;

            RestMarquee();   // a scrolling title would be snapshotted whole and unclipped
            var oldShot = oldBig is null ? null : await Capture(CardRow);
            var oldIconX = oldBig is null ? 0 : IconX();
            Phase("old-card");
            var smallShot = await Capture(SmallPill);
            Phase("old-small");
            var before = Lay(0, oldBig, oldShot, oldIconX, oldCells, oldDots, left).Shift(oldShift);

            // stand-ins for the old look over a taskbar-coloured sheet, while the new state is drawn underneath (no flash of it)
            Put(new Rectangle { Width = spanWidth, Height = _layout.Height / DpiSetup.Scale, Fill = Root.Background }, 0, 0);
            if (oldBig is not null && oldShot is not null)
            {
                new Mover(this, oldShot, GlowOf(oldBig)).Place(before.BigBox!.Value, before.BigBox.Value.X, before.RowTop, oldIconX, 1, 1, 0, 1);
            }

            if (smallShot is not null)
            {
                Put(Snapshot(smallShot), before.SmallX, before.RowTop);
            }

            RenderTaskCells();
            RenderIsland();   // still "flying": the window keeps its span and stays unclipped
            Root.UpdateLayout();
            // the text was fitted to the slot's old width (say a plain card's 176) before layout gave the new card its own
            // (a song's is narrower: cover, equalizer) — it ran over the equalizer while flying, then snapped back (user 10-07)
            RenderIsland();
            Root.UpdateLayout();

            var newBig = _bigCard;
            var newShot = newBig is null ? null : newBig.Key == oldBig?.Key ? oldShot : await Capture(CardRow);
            Phase("new-card");
            var after = Lay(0, newBig, newShot, newBig is null ? 0 : IconX(), _renderedCells.ToList(), DotsDrawn, left).Shift(newShift);
            if ((oldBig is not null && oldShot is null) || (newBig is not null && newShot is null))
            {
                return;   // no snapshot (screen locked): just show the result
            }

            FlyLayer.Children.Clear();
            PillRow.Opacity = 0;
            var frames = BuildFlight(before, after, style, hoverKey);
            Phase("fly");
            await Animate(SwapMotion.Length(bounce), t =>
            {
                var p = SwapMotion.Move(t, bounce);
                foreach (var frame in frames)
                {
                    frame(t, p);
                }

                ClipToFlying();
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TaskbarHost.Log($"transition failed: {ex.ToString().Replace(Environment.NewLine, " | ")}");   // async: never let it take the bar down
        }
        finally
        {
            Phase("end");
            TaskbarHost.Log($"transition{phases}");   // ms since it started — a slow step shows here
            FlyLayer.Children.Clear();
            PillRow.Opacity = 1;
            PillRow.Margin = new Thickness(0, PillGeometry.ArcRoom, 0, 0);
            _flying = false;
            _placement = new BarPlacement(false, int.MinValue, int.MinValue);   // place the window for the new layout now
            RenderTaskCells();
            RenderIsland();
            if (_lapAfterFlight)
            {
                _lapAfterFlight = false;
                ReplayMarquee();   // the new title's lap, held back while it flew
            }

            FollowTaskbar();
            ApplyRegion();
        }
    }

    /// <summary>What the pills show and where, in dip from the window's left edge (<see cref="WinX"/> = that edge on screen).</summary>
    private sealed record Scene(
        double WinX,
        double RowTop,
        Card? Big,
        MotionBox? BigBox,
        double IconX,
        ImageSource? Shot,
        IReadOnlyList<(string Key, Card? Card, double CentreX)> Cells,
        double SmallX,
        double SmallWidth,
        double BallX,
        double? RuleX)
    {
        /// <summary>The same scene moved right by <paramref name="dx"/> (into the spanning window).</summary>
        public Scene Shift(double dx) => this with
        {
            BigBox = BigBox is { } box ? box with { X = box.X + dx } : null,
            Cells = Cells.Select(c => (c.Key, c.Card, c.CentreX + dx)).ToList(),
            SmallX = SmallX + dx,
            BallX = BallX + dx,
            RuleX = RuleX + dx,
        };
    }

    /// <summary>
    /// Where everything sits for a scene, by the same rules as the XAML (TODO 7.9 D): small pill right of the big one — big 0, gap 8,
    /// then 4 + cells (⋯ last) + line + circle; left — circle, line, cells (⋯ first) + 4, gap 8, big.
    /// </summary>
    private Scene Lay(double winX, Card? big, ImageSource? shot, double iconX, IReadOnlyList<Card> cells, bool dots, bool smallLeft)
    {
        var keys = cells.Select(c => (c.Key, (Card?)c)).ToList();
        if (dots)
        {
            keys.Insert(smallLeft ? 0 : keys.Count, (DotsTag, null));
        }

        var smallWidth = PillGeometry.SmallWidth(keys.Count);
        var smallX = big is not null && !smallLeft ? PillGeometry.BigWidth + PillGeometry.Gap : 0;
        var bigX = smallLeft ? smallWidth + PillGeometry.Gap : 0;
        var firstCell = smallLeft ? smallX + PillGeometry.BallBox + PillGeometry.Rule : smallX + PillGeometry.CellMargin;
        var ballX = smallLeft ? smallX + (PillGeometry.BallBox / 2) : smallX + smallWidth - (PillGeometry.BallBox / 2);
        double? ruleX = keys.Count == 0 ? null : smallLeft ? smallX + PillGeometry.BallBox : firstCell + (keys.Count * PillGeometry.Cell);
        var row = PillGeometry.ArcRoom;
        return new Scene(
            winX,
            row,
            big,
            big is null ? null : new MotionBox(bigX, row, PillGeometry.BigWidth, PillGeometry.Height, CornerRadiusNow()),
            iconX,
            shot,
            keys.Select((k, i) => (k.Key, k.Item2, firstCell + (i * PillGeometry.Cell) + (PillGeometry.Cell / 2))).ToList(),
            smallX,
            smallWidth,
            ballX,
            ruleX);
    }

    /// <summary>The stand-ins and, for each, what it does on a frame (time share t, moved share p — past 1 while bouncing).</summary>
    private List<Action<double, double>> BuildFlight(Scene a, Scene b, SwapStyle style, string? hoverKey)
    {
        var frames = new List<Action<double, double>>();
        var row = b.RowTop;
        var radius = CornerRadiusNow();

        // the small pill's own surface, the line and the circle
        var surface = new Border
        {
            Height = PillGeometry.Height, CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1),
            Background = BrushOf(RealSearchBoxFill()), BorderBrush = SmallSurface.BorderBrush, Tag = radius,
        };
        Put(surface, a.SmallX, row);
        var rule = new Rectangle { Width = 1, Height = 16, Fill = SmallRule.Fill };
        Put(rule, a.RuleX ?? b.RuleX ?? 0, row + 8);
        var ring = new Ellipse { Width = 8.4, Height = 8.4, StrokeThickness = 1.2, Stroke = BallRing.Stroke };
        Put(ring, a.BallX - 4.2, row + 11.8);
        frames.Add((t, p) =>
        {
            Canvas.SetLeft(surface, SwapMotion.Lerp(a.SmallX, b.SmallX, p));
            surface.Width = Math.Max(PillGeometry.BallBox, SwapMotion.Lerp(a.SmallWidth, b.SmallWidth, p));
            Canvas.SetLeft(rule, SwapMotion.Lerp(a.RuleX ?? b.RuleX ?? 0, b.RuleX ?? a.RuleX ?? 0, p));
            rule.Opacity = SwapMotion.Lerp(a.RuleX is null ? 0 : 1, b.RuleX is null ? 0 : 1, Math.Clamp(p, 0, 1));
            Canvas.SetLeft(ring, SwapMotion.Lerp(a.BallX, b.BallX, p) - 4.2);
        });

        // cells that stay slide; the ones that come or go grow / shrink in place — unless they are a card going up or down
        var morph = style != SwapStyle.Flip;
        var cellsA = a.Cells.ToDictionary(c => c.Key);
        var cellsB = b.Cells.ToDictionary(c => c.Key);
        var down = a.Big is { } oldCard && b.Big?.Key != oldCard.Key && cellsB.TryGetValue(oldCard.Key, out var landing) ? landing : default;
        var up = b.Big is { } newCard && a.Big?.Key != newCard.Key && cellsA.TryGetValue(newCard.Key, out var start) ? start : default;
        foreach (var key in cellsA.Keys.Union(cellsB.Keys))
        {
            var inA = cellsA.TryGetValue(key, out var ca);
            var inB = cellsB.TryGetValue(key, out var cb);
            if (morph && ((!inB && key == up.Key) || (!inA && key == down.Key)))
            {
                continue;   // flies as a card below
            }

            var ghost = CellGhost(key, inB ? cb.Card : ca.Card);
            var scale = new ScaleTransform { CenterX = PillGeometry.Cell / 2, CenterY = 16 };
            ghost.RenderTransform = scale;
            Put(ghost, (inA ? ca.CentreX : cb.CentreX) - (PillGeometry.Cell / 2), row);
            frames.Add((t, p) =>
            {
                if (inA && inB)
                {
                    Canvas.SetLeft(ghost, SwapMotion.Lerp(ca.CentreX, cb.CentreX, p) - (PillGeometry.Cell / 2));
                    return;
                }

                var show = inB ? p : 1 - SwapMotion.Ease(t);   // coming: grows with the bounce; going: shrinks away
                scale.ScaleX = scale.ScaleY = SwapMotion.Lerp(0.5, 1, show);
                ghost.Opacity = Math.Clamp(show, 0, 1);
            });
        }

        // the cards: down into a cell (below), up from a cell (on top), sliding along, dropping out or in
        var lift = style == SwapStyle.MorphArc ? SwapMotion.Arc : 0;
        var turn = SwapMotion.BackgroundTurn;
        if (a.Big is { } ac && a.BigBox is { } aBox)
        {
            var mover = new Mover(this, a.Shot!, GlowOf(ac));
            if (b.Big?.Key == ac.Key && b.BigBox is { } same)
            {
                frames.Add((t, p) => mover.Place(SwapMotion.Between(aBox, same, p, 0), SwapMotion.Lerp(aBox.X, same.X, p), row, a.IconX, 1, 1, 0, 1));
            }
            else if (morph && down.Key is not null)
            {
                var circle = SwapMotion.CircleAt(down.CentreX, row);
                var lit = down.Key == hoverKey;
                // the card's text fades as it closes (text cut by the closing circle looked like being sliced) while its icon stays
                // and rides the card down into the cell — fading the small icon in at the end looked like it popped up (user 10-07)
                var icon = CellGhost(down.Key, ac);
                Put(icon, 0, row);
                frames.Add((t, p) =>
                {
                    var back = 1 - Math.Clamp((p - (1 - turn)) / turn, 0, 1);
                    var x = SwapMotion.ContentX(circle, aBox, a.IconX, 1 - p);
                    var y = row + (lift * Math.Sin(Math.PI * p));
                    mover.Place(SwapMotion.Between(aBox, circle, p, lift), x, y, a.IconX, 1, 1 - p, lit ? 1 - back : 0,
                        style == SwapStyle.MorphLine ? SwapMotion.Lerp(1, SwapMotion.FadeTo, Math.Clamp(p, 0, 1)) : 1,
                        content: 1 - Math.Clamp(p / 0.35, 0, 1));
                    Canvas.SetLeft(icon, x + a.IconX - (PillGeometry.Cell / 2));
                    Canvas.SetTop(icon, y);
                });
            }
            else
            {
                frames.Add((t, p) =>
                {
                    var gone = SwapMotion.EaseIn(t * 2);   // drops out in the first half
                    var y = row + (SwapMotion.FlipDrop * gone);
                    mover.Place(aBox with { Y = y }, aBox.X, y, a.IconX, 1, 1, 0, 1 - gone);
                });
            }
        }

        if (b.Big is { } bc && b.BigBox is { } bBox && bc.Key != a.Big?.Key)
        {
            var mover = new Mover(this, b.Shot!, GlowOf(bc));
            if (morph && up.Key is not null)
            {
                var circle = SwapMotion.CircleAt(up.CentreX, row);
                var lit = up.Key == hoverKey;
                var icon = CellGhost(up.Key, bc);   // the small icon rides the card up to its place, then the card's content takes over
                Put(icon, up.CentreX - (PillGeometry.Cell / 2), row);
                frames.Add((t, p) =>
                {
                    var bg = Math.Clamp(p / turn, 0, 1);
                    var x = SwapMotion.ContentX(circle, bBox, b.IconX, p);
                    var y = row - (lift * Math.Sin(Math.PI * p));
                    mover.Place(SwapMotion.Between(circle, bBox, p, -lift), x, y,
                        b.IconX, 1, p, lit ? 1 - bg : 0, 1, content: Math.Clamp((p - 0.45) / 0.4, 0, 1));   // shows once the box is nearly open
                    Canvas.SetLeft(icon, x + b.IconX - (PillGeometry.Cell / 2));
                    Canvas.SetTop(icon, y);
                    icon.Opacity = 1 - Math.Clamp((p - 0.7) / 0.25, 0, 1);   // under it the content's own icon is already there
                });
            }
            else
            {
                var bounce = _settings.Bounce;
                frames.Add((t, p) =>
                {
                    var q = SwapMotion.Move(Math.Clamp((t - 0.3) / 0.7, 0, 1), bounce);   // drops in once the old one is leaving
                    var y = row - (SwapMotion.FlipDrop * (1 - q));
                    mover.Place(bBox with { Y = y }, bBox.X, y, b.IconX, 1, 1, 0, q);
                });
            }
        }

        return frames;
    }

    /// <summary>
    /// Clips the window to the flying shapes: the small pill's surface and the cards' boxes, with their round corners (their
    /// <c>Tag</c> holds the corner radius; the icons, the line and the circle sit inside the surface).
    /// </summary>
    private void ClipToFlying()
    {
        var scale = DpiSetup.Scale;
        var shapes = new List<(int, int, int, int, int)>();
        foreach (var child in FlyLayer.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is not double radius || child.Opacity <= 0.01 || double.IsNaN(child.Width) || child.Width <= 0)
            {
                continue;
            }

            var x = Canvas.GetLeft(child);
            var y = Canvas.GetTop(child);
            shapes.Add(((int)Math.Round(x * scale), (int)Math.Round(y * scale), (int)Math.Round((x + child.Width) * scale),
                (int)Math.Round((y + child.Height) * scale), (int)Math.Round(2 * radius * scale)));
        }

        PillRegion.ApplyShapes(_taskbar.OuterWindow, shapes);
    }

    /// <summary>A stand-in for one cell: its icon (or ⋯) as the small pill draws it.</summary>
    private Grid CellGhost(string key, Card? card)
    {
        UIElement content = key == DotsTag || card is null
            ? new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16, Glyph = "", Foreground = BrushOf(_theme.Icon) }
            : CellContent(card, TaskIcons.For(card, CoverOf(card) is not null, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
        var cell = new Grid { Width = PillGeometry.Cell, Height = PillGeometry.Height, IsHitTestVisible = false };
        cell.Children.Add(content);
        return cell;
    }

    private void Put(FrameworkElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        FlyLayer.Children.Add(element);
    }

    /// <summary>A snapshot shown at its own size: pixels ÷ the screen scale (stretched to a fixed size it blew the text up — user 10-07).</summary>
    private Image Snapshot(ImageSource shot)
    {
        var (w, h) = shot is RenderTargetBitmap bitmap && bitmap.PixelWidth > 0
            ? (bitmap.PixelWidth / RasterScale, bitmap.PixelHeight / RasterScale)
            : (PillGeometry.BigWidth, PillGeometry.Height);
        return new Image { Source = shot, Width = w, Height = h, Stretch = Stretch.Fill, IsHitTestVisible = false };
    }

    /// <summary>
    /// Runs <paramref name="frame"/> with the time share 0–1 on every rendered frame for <paramref name="length"/>. A backstop timer
    /// ends it if frames stop coming (screen locked, display off) — the bar must never stay stuck "flying".
    /// </summary>
    private Task Animate(TimeSpan length, Action<double> frame)
    {
        var done = new TaskCompletionSource();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var meter = new FrameMeter("swap");
        EventHandler<object>? tick = null;
        var backstop = _ui.CreateTimer();
        void Finish()
        {
            CompositionTarget.Rendering -= tick;
            backstop.Stop();
            meter.Report();
            done.TrySetResult();
        }

        tick = (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed / length);
            meter.Frame();
            try
            {
                frame(t);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                t = 1;
            }

            if (t >= 1)
            {
                Finish();
            }
        };
        backstop.Interval = length + TimeSpan.FromMilliseconds(500);
        backstop.IsRepeating = false;
        backstop.Tick += (_, _) => Finish();
        frame(0);
        CompositionTarget.Rendering += tick;
        backstop.Start();
        return done.Task;
    }

    /// <summary>
    /// One flying card: a box (pill colour, rounded, optional coloured border) showing a snapshot of the card's content, which
    /// slides inside it so the icon stays at the circle's centre. A hover-coloured disc stands for the cell's lit look at the cell end.
    /// </summary>
    private sealed class Mover
    {
        private readonly Grid _box = new() { IsHitTestVisible = false };
        private readonly Border _back = new();   // the rounded fill (a Grid's own background ignored its corner radius: square slabs, user 10-07)
        private readonly Canvas _inside = new();
        private readonly Image _image;
        private readonly Ellipse _disc;
        private readonly Border _ring;
        private readonly SolidColorBrush _fill;
        private readonly Windows.UI.Color _pill;

        public Mover(MainWindow window, ImageSource shot, Windows.UI.Color? glow)
        {
            _pill = window.RealSearchBoxFill();
            _fill = new SolidColorBrush(_pill);
            _back.Background = _fill;
            _image = window.Snapshot(shot);
            _disc = new Ellipse { Width = SwapMotion.Circle, Height = SwapMotion.Circle, Fill = new SolidColorBrush(HoverColor()) };
            _ring = new Border
            {
                BorderThickness = new Thickness(glow is null ? 0 : 2),
                BorderBrush = new SolidColorBrush(glow ?? Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            };
            _inside.Children.Add(_disc);
            _inside.Children.Add(_image);
            _box.Children.Add(_back);
            _box.Children.Add(_inside);
            _box.Children.Add(_ring);
            window.FlyLayer.Children.Add(_box);
        }

        /// <param name="contentX">Window x of the card content's left edge; <paramref name="contentY"/> its top.</param>
        public void Place(MotionBox box, double contentX, double contentY, double iconX, double background, double ring, double disc, double opacity, double content = 1)
        {
            _image.Opacity = Math.Clamp(content, 0, 1);
            if (box.X < 0)
            {
                box = box with { X = 0, Width = box.Width + box.X };   // the window's left edge: a bounce past it is dropped, not cut
            }

            var w = Math.Max(0, box.Width);
            var h = Math.Max(0, box.Height);
            Canvas.SetLeft(_box, box.X);
            Canvas.SetTop(_box, box.Y);
            _box.Width = w;
            _box.Height = h;
            _box.CornerRadius = new CornerRadius(Math.Clamp(box.Radius, 0, h / 2));
            _box.Tag = _box.CornerRadius.TopLeft;   // the window is clipped to this shape (ClipToFlying)
            _ring.CornerRadius = _box.CornerRadius;
            _back.CornerRadius = _box.CornerRadius;
            _box.Opacity = Math.Clamp(opacity, 0, 1);
            _fill.Color = Windows.UI.Color.FromArgb((byte)Math.Round(255 * Math.Clamp(background, 0, 1)), _pill.R, _pill.G, _pill.B);
            _ring.Opacity = Math.Clamp(ring, 0, 1);
            _inside.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, w, h) };
            Canvas.SetLeft(_image, contentX - box.X);
            Canvas.SetTop(_image, contentY - box.Y);
            Canvas.SetLeft(_disc, contentX + iconX - (SwapMotion.Circle / 2) - box.X);
            Canvas.SetTop(_disc, contentY + 4 - box.Y);
            _disc.Opacity = Math.Clamp(disc, 0, 1);
        }
    }

    /// <summary>The screen scale XAML draws at (pixels per dip).</summary>
    private double RasterScale => Root.XamlRoot?.RasterizationScale ?? DpiSetup.Scale;

    /// <summary>A snapshot of an element as it is drawn now; null when it can't be taken (not drawn yet, screen locked).</summary>
    private async Task<ImageSource?> Capture(FrameworkElement element)
    {
        try
        {
            var shot = new RenderTargetBitmap();
            var render = shot.RenderAsync(element).AsTask();
            // it completes on the next drawn frame — none come while the screen is locked
            if (await Task.WhenAny(render, Task.Delay(500)) != render)
            {
                return null;
            }

            await render;
            if (_shotsLogged++ < 2)
            {
                TaskbarHost.Log($"snapshot {element.Name}: {shot.PixelWidth}x{shot.PixelHeight} px of {element.ActualWidth}x{element.ActualHeight} dip, scale {RasterScale}");
            }

            return shot;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private int _shotsLogged;

    /// <summary>The centre x of the card's icon inside the big pill (an inform card's sits right of its close zone).</summary>
    private double IconX() => LeadSlot.TransformToVisual(PillBody).TransformPoint(new Windows.Foundation.Point(8, 8)).X;

    private double CornerRadiusNow() => _settings.PillCorners != "small" ? PillGeometry.Height / 2 : PillGeometry.SmallCorner;

    /// <summary>A pinned card's border colour (pending orange, inform green, temporary warning red), as on the big pill; null = none.</summary>
    private Windows.UI.Color? GlowOf(Card? card) => card switch
    {
        { Pinned: true, Type: CardType.Pending } => Attention(),
        { Pinned: true, Type: CardType.Inform } when card.State.RunState == ActivityRunState.Error => Alarm(),
        { Pinned: true, Type: CardType.Inform } => _theme.Ok,
        { Pinned: true, Type: CardType.Temporary } when card.State.RunState == ActivityRunState.Error => Alarm(),
        _ => null,
    };
}
