using IsleBar.App.Interop;
using IsleBar.Core.Island;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace IsleBar.App;

/// <summary>
/// Clicking a task icon swaps that card with the one on the big pill (TODO 7.3 / 7.9 D, user 10-07), with the animation the
/// settings pick (<see cref="SwapStyle"/>). All timing, easing and arc numbers live in <see cref="SwapMotion"/>.
/// <para>
/// The morph flies snapshots of the two cards' content (RenderTargetBitmap of <c>CardRow</c>, no background) in boxes drawn here,
/// over a hidden big pill, frame by frame on the UI thread (~20 frames). The window isn't clipped while they fly: re-clipping every
/// frame made the edges flicker (E1 test), and the arcs leave the pill row.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A swap animation is running: the tick doesn't redraw and the small pill doesn't change.</summary>
    private bool _flying;

    /// <summary>A task icon was clicked: its card goes on the big pill and the card there takes the clicked cell.</summary>
    private async void OnTaskCellClicked(string key)
    {
        if (_flying || _swapping)
        {
            return;
        }

        var old = _shownCard;
        var style = SwapMotion.Parse(_settings.SwapAnimation);
        var cellsBefore = TaskCellCount;
        var animate = old is not null && _snapshot.IsActive && PillBody.Visibility == Visibility.Visible;
        _flying = true;
        try
        {
            // the old card as it looks now, then hide the real one (a mover stands in for it from this frame on)
            var oldShot = animate ? await Capture(CardRow) : null;
            var oldIconX = IconX();
            var oldOrigin = Origin(PillBody);
            var pill = new MotionBox(oldOrigin.X, oldOrigin.Y, PillGeometry.BigWidth, PillGeometry.Height, CornerRadiusNow());
            var clickedCentre = CellCentre(key);

            // morph: the old card's mover covers the big pill from now on, so the new card drawn under it doesn't flash
            Mover? outgoing = null;
            if (oldShot is not null && style != SwapStyle.Flip)
            {
                outgoing = new Mover(this, oldShot, GlowOf(old));
                outgoing.Place(pill, pill.X, pill.Y, oldIconX, background: 1, ring: 1, disc: 0, opacity: 1);
            }

            _slots.Swap(key, old?.Key, old?.Type == CardType.Temporary);
            _cards.Choose(key, DateTimeOffset.UtcNow);
            _shownCard = _cards.Shown;
            _snapshot = _shownCard is { } shown ? new IslandSnapshot(shown.State, _cards.Cards.Count - 1) : IslandSnapshot.Empty;
            _animator.Classify(_snapshot);   // this is the swap itself: the tick mustn't play its own fade on top
            _flying = false;
            UpdateTaskCells();
            _flying = true;
            RenderIsland();
            Root.UpdateLayout();

            if (oldShot is null || clickedCentre is null || TaskCellCount != cellsBefore)
            {
                return;   // nothing to fly (or the small pill changed width — then just show the result)
            }

            var landing = old!.Type == CardType.Temporary ? null : CellOf(old.Key);
            if (landing is not null && style != SwapStyle.Flip)
            {
                landing.Children[^1].Opacity = 0;   // the old card's icon appears when its mover lands
            }

            if (style == SwapStyle.Flip)
            {
                await Flip(oldShot, oldOrigin, landing);
            }
            else
            {
                var newShot = await Capture(CardRow);
                if (newShot is null)
                {
                    return;
                }

                await Morph(style, pill, outgoing!, oldIconX, newShot, IconX(), clickedCentre.Value, landing, GlowOf(_shownCard));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TaskbarHost.Log($"swap animation failed: {ex.Message}");   // async void: never let it take the bar down
        }
        finally
        {
            FlyLayer.Children.Clear();
            PillBody.Opacity = 1;
            CardRow.Opacity = 1;
            CardRow.RenderTransform = null;
            foreach (var cell in TaskCells.Children.OfType<Grid>())
            {
                cell.Children[^1].Opacity = 1;
                cell.Children[^1].RenderTransform = null;
            }

            ApplyRegion();
            _flying = false;
            RenderIsland();
        }
    }

    /// <summary>The morph: the clicked circle opens into the pill (on top), the old card closes into the cell it leaves.</summary>
    private Task Morph(SwapStyle style, MotionBox pill, Mover outgoing, double oldIconX, ImageSource newShot, double newIconX,
        Windows.Foundation.Point clicked, Grid? landing, Windows.UI.Color? newGlow)
    {
        var rowTop = pill.Y;
        var from = SwapMotion.CircleAt(clicked.X, rowTop);
        var lift = style == SwapStyle.MorphArc ? SwapMotion.Arc : 0;
        var line = style == SwapStyle.MorphLine;
        var incoming = new Mover(this, newShot, newGlow);   // on top of the outgoing one
        PillBody.Opacity = 0;
        PillRegion.Clear(_taskbar.OuterWindow);

        return Animate(SwapMotion.Duration, t =>
        {
            var p = SwapMotion.Ease(t);
            var turn = SwapMotion.BackgroundTurn;

            // the new card: circle → pill, rising on the arc; its background comes in as it leaves the cell, where the cell's own
            // hover look shows (no flash of a dark disc on the lit cell — user 10-07)
            incoming.Place(SwapMotion.Between(from, pill, p, -lift), SwapMotion.ContentX(from, pill, newIconX, p), rowTop - (lift * Math.Sin(Math.PI * p)),
                newIconX, background: Math.Clamp(p / turn, 0, 1), ring: p, disc: 1 - Math.Clamp(p / turn, 0, 1), opacity: 1);

            if (landing is null)
            {
                outgoing.Place(pill, pill.X, rowTop, oldIconX, 1, 1, 0, opacity: 1 - p);   // a temporary card has no cell: it just fades
                return;
            }

            var to = SwapMotion.CircleAt(clicked.X, rowTop);
            var back = 1 - Math.Clamp((p - (1 - turn)) / turn, 0, 1);
            outgoing.Place(SwapMotion.Between(pill, to, p, lift), SwapMotion.ContentX(to, pill, oldIconX, 1 - p), rowTop + (lift * Math.Sin(Math.PI * p)),
                oldIconX, background: back, ring: 1 - p, disc: 1 - back, opacity: line ? SwapMotion.Lerp(1, SwapMotion.FadeTo, p) : 1);
        });
    }

    /// <summary>Flip in place: the old content drops out, the new drops in; the cell's new icon drops in too. Nothing leaves the row.</summary>
    private Task Flip(ImageSource oldShot, Windows.Foundation.Point origin, Grid? landing)
    {
        var image = new Image { Source = oldShot, Width = PillGeometry.BigWidth, Height = PillGeometry.Height, Stretch = Stretch.Fill };
        FlyLayer.Children.Add(image);
        Canvas.SetLeft(image, origin.X);
        var content = landing?.Children[^1];
        var total = SwapMotion.FlipInDelay + SwapMotion.FlipIn;
        if (SwapMotion.FlipCellIn > total)
        {
            total = SwapMotion.FlipCellIn;
        }

        var shift = new TranslateTransform();
        CardRow.RenderTransform = shift;
        var cellShift = new TranslateTransform();
        if (content is not null)
        {
            content.RenderTransform = cellShift;
        }

        return Animate(total, t =>
        {
            var ms = t * total.TotalMilliseconds;
            var gone = SwapMotion.EaseIn(ms / SwapMotion.FlipOut.TotalMilliseconds);
            Canvas.SetTop(image, origin.Y + (SwapMotion.FlipDrop * gone));
            image.Opacity = 1 - gone;

            var come = SwapMotion.EaseOut((ms - SwapMotion.FlipInDelay.TotalMilliseconds) / SwapMotion.FlipIn.TotalMilliseconds);
            shift.Y = -SwapMotion.FlipDrop * (1 - come);
            CardRow.Opacity = come;

            if (content is not null)
            {
                var cell = SwapMotion.EaseOut(ms / SwapMotion.FlipCellIn.TotalMilliseconds);
                cellShift.Y = -SwapMotion.FlipDrop * (1 - cell);
                content.Opacity = cell;
            }
        });
    }

    /// <summary>
    /// Runs <paramref name="frame"/> with the time share 0–1 on every rendered frame for <paramref name="length"/>. A backstop timer
    /// ends it if frames stop coming (screen locked, display off) — the bar must never stay stuck "flying".
    /// </summary>
    private Task Animate(TimeSpan length, Action<double> frame)
    {
        var done = new TaskCompletionSource();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        EventHandler<object>? tick = null;
        var backstop = _ui.CreateTimer();
        void Finish()
        {
            CompositionTarget.Rendering -= tick;
            backstop.Stop();
            done.TrySetResult();
        }

        tick = (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed / length);
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
            _box.Background = _fill;
            _image = new Image { Source = shot, Width = PillGeometry.BigWidth, Height = PillGeometry.Height, Stretch = Stretch.Fill };
            _disc = new Ellipse { Width = SwapMotion.Circle, Height = SwapMotion.Circle, Fill = new SolidColorBrush(HoverColor()) };
            _ring = new Border
            {
                BorderThickness = new Thickness(glow is null ? 0 : 2),
                BorderBrush = new SolidColorBrush(glow ?? Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            };
            _inside.Children.Add(_disc);
            _inside.Children.Add(_image);
            _box.Children.Add(_inside);
            _box.Children.Add(_ring);
            window.FlyLayer.Children.Add(_box);
        }

        /// <param name="contentX">Window x of the card content's left edge; <paramref name="contentY"/> its top.</param>
        public void Place(MotionBox box, double contentX, double contentY, double iconX, double background, double ring, double disc, double opacity)
        {
            Canvas.SetLeft(_box, box.X);
            Canvas.SetTop(_box, box.Y);
            _box.Width = box.Width;
            _box.Height = box.Height;
            _box.CornerRadius = new CornerRadius(box.Radius);
            _ring.CornerRadius = new CornerRadius(box.Radius);
            _box.Opacity = opacity;
            _fill.Color = Windows.UI.Color.FromArgb((byte)Math.Round(255 * Math.Clamp(background, 0, 1)), _pill.R, _pill.G, _pill.B);
            _ring.Opacity = Math.Clamp(ring, 0, 1);
            _inside.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, box.Width, box.Height) };
            Canvas.SetLeft(_image, contentX - box.X);
            Canvas.SetTop(_image, contentY - box.Y);
            Canvas.SetLeft(_disc, contentX + iconX - (SwapMotion.Circle / 2) - box.X);
            Canvas.SetTop(_disc, contentY + 4 - box.Y);
            _disc.Opacity = Math.Clamp(disc, 0, 1);
        }
    }

    /// <summary>A snapshot of an element as it is drawn now; null when it can't be taken (not drawn yet).</summary>
    private static async Task<ImageSource?> Capture(UIElement element)
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
            return shot;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>The centre x of the card's icon inside the big pill (an inform card's sits right of its close zone).</summary>
    private double IconX() => LeadSlot.TransformToVisual(PillBody).TransformPoint(new Windows.Foundation.Point(8, 8)).X;

    private Windows.Foundation.Point Origin(UIElement element) => element.TransformToVisual(Root).TransformPoint(default);

    private double CornerRadiusNow() => _settings.PillCorners != "small" ? PillGeometry.Height / 2 : PillGeometry.SmallCorner;

    private Grid? CellOf(string key) => TaskCells.Children.OfType<Grid>().FirstOrDefault(c => c.Tag as string == key);

    private Windows.Foundation.Point? CellCentre(string key)
        => CellOf(key) is { } cell ? cell.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(PillGeometry.Cell / 2, 16)) : null;

    /// <summary>A pinned card's border colour (pending orange, inform green, temporary warning red), as on the big pill; null = none.</summary>
    private Windows.UI.Color? GlowOf(Card? card) => card switch
    {
        { Pinned: true, Type: CardType.Pending } => Attention(),
        { Pinned: true, Type: CardType.Inform } => _theme.Ok,
        { Pinned: true, Type: CardType.Temporary } when card.State.RunState == ActivityRunState.Error => Alarm(),
        _ => null,
    };
}
