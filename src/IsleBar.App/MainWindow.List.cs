using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.Core.Island;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace IsleBar.App;

/// <summary>
/// The list (TODO 7.9 F3, user 10-09): the circle (TODO ①½, 10-10) opens every card as a big pill over the big pill (<see cref="ListWindow"/>); tapping one
/// puts it on the big pill (the usual swap underneath) and lifts its card straight out of that list pill. Each list pill is a
/// <see cref="PillView"/> of its own, drawn every tick like the big pill (plan B, 10-10: snapshots of the big pill drawing each card
/// made it flicker each second and caught timers mid-roll).
/// </summary>
public sealed partial class MainWindow
{
    private ListWindow? _list;

    /// <summary>How dim the big pill is, and the task icons (1 = not; <see cref="ListMotion.Dim"/> while the list or a lifted card is up).</summary>
    private double _bigDim = 1;
    private double _cellDim = 1;

    /// <summary>The cards for the list: the big pill's first, then the task icons' order, then the rest (no room for an icon, temporary) by stack.</summary>
    private List<Card> ListCards()
    {
        var list = new List<Card>();
        if (_shownCard is { } shown)
        {
            list.Add(shown);
        }

        foreach (var card in _slots.Cells.Concat(_cards.Cards))
        {
            if (list.All(c => c.Key != card.Key))
            {
                list.Add(card);
            }
        }

        return list;
    }

    /// <summary>A list pill readied: its colours at rest, the close button as the big pill's (<see cref="PaintFill"/>, <see cref="ApplyCorners"/>).</summary>
    private void PrepareListPill(PillView pill)
    {
        pill.UseAsListed(RealSearchBoxFill(), PillView.ColourOf(PillView.Colours.Chip), CloseChip.Radius, _text.CloseCard, _theme.Foreground);
    }

    /// <summary>Draws a card on its list pill as the big pill draws it (<see cref="RenderIsland"/>); a card gone keeps its last look.</summary>
    private void RenderListPill(string key, PillView pill)
    {
        if (_cards.Cards.FirstOrDefault(c => c.Key == key) is not { } card)
        {
            return;
        }

        pill.ShowCardLead();
        pill.ShowClose(card.Type == CardType.Inform);
        pill.UpdateProgress(card.State);
        pill.Render(card.State, card, plainIcon: false);
    }

    /// <summary>A picture of a list pill taken when the mouse came onto it: the lifted card starts from it (as <see cref="_pillShot"/>).</summary>
    private (string? Key, ImageSource? Shot) _listPillShot;

    private async void TakeListPillShot(string key, PillView pill)
    {
        var shot = await Capture(pill.CardRow);
        _listPillShot = (key, shot);
    }

    /// <summary>
    /// The circle was clicked: the list opens (a lifted card goes down first), or goes away. No cards: nothing (the circle only
    /// springs back). Every card pinned: the list stands where the big pill would be, over the empty taskbar (user 10-10).
    /// </summary>
    private void ToggleList()
    {
        if (_list?.IsOpen == true)
        {
            CloseList();
            return;
        }

        if (_lift?.OpenKey is not null)
        {
            _lift.Collapse();
            return;
        }

        if (_flying || _cards.Cards.Count == 0)
        {
            return;
        }

        if ((BigPillOnScreen() ?? BigPillWouldBe()) is not { } bigPill || BallOnScreen() is not { } circle)
        {
            return;
        }

        OpenList(bigPill, circle);
    }

    /// <summary>Opens the list over the big pill at <paramref name="pill"/>, flying out of the circle at <paramref name="ball"/> (screen pixels).</summary>
    private void OpenList(ScreenRect pill, ScreenRect ball)
    {
        var screen = Microsoft.UI.Windowing.DisplayArea.GetFromRect(
            new Windows.Graphics.RectInt32(pill.Left, pill.Top, pill.Width, pill.Height),
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).OuterBounds;
        var taskbarTop = TaskbarTop();
        var cards = ListCards();
        var fits = ListMotion.Fits(cards.Count, PillGeometry.Height, (taskbarTop - screen.Y) / DpiSetup.Scale, 0);
        if (fits < cards.Count)
        {
            TaskbarHost.Log($"list: {cards.Count} cards, {fits} fit under the screen top");
            cards = cards.Take(fits).ToList();
        }

        if (_list is null)
        {
            _list = new ListWindow();
            _list.Tapped = OnListTapped;
            _list.Hovered = TakeListPillShot;
            _list.Closing = () => _ = Dim(1, ListMotion.Close);
            _list.KeepsPress = OnBar;
        }

        _listPillShot = default;
        _list.Open(new ListRequest(
            cards.Select(c => c.Key).ToList(),
            this,
            PrepareListPill,
            RenderListPill,
            key => GlowOf(_cards.Cards.FirstOrDefault(c => c.Key == key)),
            pill,
            ball,
            CornerRadiusNow(),
            RealSearchBoxFill(),
            !ThemeColors.FromSystem().Light,
            screen.X,
            screen.X + screen.Width,
            taskbarTop,
            Animations.On));
        _ = Dim(ListMotion.Dim, ListMotion.Length(Animations.On));
    }

    private void CloseList() => _list?.PutAway();   // its Closing brightens the pills again

    /// <summary>
    /// Every card pinned, the big pill hidden: where it would be beside the small pill as it stands (screen pixels) — the list
    /// stands there over the empty taskbar (user 10-10: no empty pill under it).
    /// </summary>
    private ScreenRect? BigPillWouldBe()
    {
        if (PillGeometry.Compute(DpiSetup.Scale, true, PillGeometry.SmallWidth(TaskCellCount)).Big is not { } big
            || !NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var bar))
        {
            return null;
        }

        return new ScreenRect(bar.Left + big.Left, bar.Top + big.Top, bar.Left + big.Right - 1, bar.Top + big.Bottom - 1);
    }

    /// <summary>
    /// A list pill was tapped: its card goes on the big pill (the usual swap, under the card) and its card lifts out of that pill.
    /// </summary>
    private void OnListTapped(string key, ScreenRect from)
    {
        SetDim(1);   // at once: the swap snapshots the pills as they are
        if (_cards.Cards.FirstOrDefault(c => c.Key == key) is not { } card)
        {
            return;
        }

        if (_shownCard?.Key != key)
        {
            OnTaskCellClicked(key);
        }

        if (_shownCard?.Key == key)
        {
            OpenLift(card, _listPillShot.Key == key ? _listPillShot.Shot : null, from, GlowOf(card));
        }
    }

    /// <summary>
    /// Dims (or brightens back) the big pill to <paramref name="to"/> over <paramref name="length"/>, and the task icons too
    /// (<paramref name="cells"/>: the list; not under a lifted card — the icons still swap then). While dim the big pill holds
    /// still (user 10-10); it moves again once it is bright.
    /// </summary>
    private async Task Dim(double to, TimeSpan length, bool cells = true)
    {
        var (fromBig, fromCell) = (_bigDim, _cellDim);
        var run = ++_dimRun;
        DimVeil.Fill = BrushOf(RealSearchBoxFill());
        if (to < 1)
        {
            Big.Frozen = true;
            if (_closeHover)
            {
                _closeHover = false;   // the close button doesn't light up under the pointer while dim
                PaintFill();
            }
        }

        await Animate(length, t =>
        {
            if (run == _dimRun)
            {
                var e = SwapMotion.Ease(t);
                ApplyDim(SwapMotion.Lerp(fromBig, to, e), cells ? SwapMotion.Lerp(fromCell, to, e) : fromCell);
            }
        });
        if (run == _dimRun && to >= 1)
        {
            Big.Frozen = false;
        }
    }

    /// <summary>Bumped to stop a running <see cref="Dim"/> (a newer one, or <see cref="SetDim"/>, takes over).</summary>
    private int _dimRun;

    /// <summary>
    /// The big pill is dim under a lifted card or the list: its buttons (close) do nothing then — a click anywhere on it only
    /// puts the card or the list away, like a click outside (user 10-10).
    /// </summary>
    private bool BigDimmed => _lift?.OpenKey is not null || _list?.IsOpen == true;

    /// <summary>Dims at once (both), stopping any dimming under way.</summary>
    private void SetDim(double value)
    {
        _dimRun++;
        ApplyDim(value, value);
        Big.Frozen = value < 1;
    }

    private void ApplyDim(double big, double cells)
    {
        _bigDim = big;
        DimVeil.Opacity = 1 - big;
        PaintGlowDim();
        if (Math.Abs(cells - _cellDim) > 0.0001)
        {
            _cellDim = cells;
            ApplyCellDim();
        }
    }

    /// <summary>The pinned card's border colour (as the animator set it), so dimming can take its colour out.</summary>
    private Windows.UI.Color _glowColor;

    /// <summary>
    /// While dim the border turns grey — its colour goes, its lightness stays (user 10-10: not just fainter) — as far as the pill
    /// is dimmed; bright again, its own colour.
    /// </summary>
    private void PaintGlowDim()
    {
        var share = Math.Clamp((1 - _bigDim) / (1 - ListMotion.Dim), 0, 1);
        GlowRing.Edge = BrushOf(Desaturate(_glowColor, share));
    }

    /// <summary><paramref name="c"/> with <paramref name="share"/> of its colour taken out, keeping its lightness (luma).</summary>
    private static Windows.UI.Color Desaturate(Windows.UI.Color c, double share)
    {
        var y = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
        byte Mix(byte v) => (byte)Math.Round(v + ((y - v) * share));
        return Windows.UI.Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
    }

    /// <summary>The task icons at <see cref="_cellDim"/> (the circle stays lit: it puts the list away); called again whenever the cells are built.</summary>
    private void ApplyCellDim()
    {
        foreach (var cell in TaskCells.Children.OfType<FrameworkElement>())
        {
            cell.Opacity = _cellDim;
        }
    }

    /// <summary>The circle's 24 cell on screen (pixels): the list flies out of it and back.</summary>
    private ScreenRect? BallOnScreen()
    {
        var cell = BallCell;
        if (!NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var bar))
        {
            return null;
        }

        var s = DpiSetup.Scale;
        var centre = cell.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(cell.ActualWidth / 2, cell.ActualHeight / 2));
        var half = SwapMotion.Circle / 2;
        return new ScreenRect(
            bar.Left + (int)Math.Round((centre.X - half) * s), bar.Top + (int)Math.Round((centre.Y - half) * s),
            bar.Left + (int)Math.Round((centre.X + half) * s), bar.Top + (int)Math.Round((centre.Y + half) * s));
    }

    /// <summary>A press on the bar's own pills: the bar closes the list itself (and doesn't then swap or lift).</summary>
    private bool OnBar(int x, int y)
    {
        if (!NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var bar))
        {
            return false;
        }

        var s = DpiSetup.Scale;
        bool In(FrameworkElement e)
        {
            if (e.Visibility != Visibility.Visible)
            {
                return false;
            }

            var at = e.TransformToVisual(Root).TransformPoint(default);
            var left = bar.Left + (at.X * s);
            var top = bar.Top + (at.Y * s);
            return x >= left && x < left + (e.ActualWidth * s) && y >= top && y < top + (e.ActualHeight * s);
        }

        return In(PillBody) || In(SmallPill);
    }

    /// <summary>Each tick: no card left at all puts the list away (every card pinned keeps it: the big pill is empty under it, TODO ①½).</summary>
    private void KeepList()
    {
        if (_list?.IsOpen == true && _cards.Cards.Count == 0)
        {
            CloseList();
            return;
        }

        if (_list?.IsOpen == true)
        {
            _list.Redraw();
        }
    }
}
