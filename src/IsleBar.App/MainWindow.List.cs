using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.Core.Island;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace IsleBar.App;

/// <summary>
/// The ⋯ list (TODO 7.9 F3, user 10-09): ⋯ opens every card as a big pill over the big pill (<see cref="ListWindow"/>); tapping one
/// puts it on the big pill (the usual swap underneath) and lifts its card straight out of that list pill. Each list pill is a
/// <see cref="PillView"/> of its own, drawn every tick like the big pill (plan B, 10-10: snapshots of the big pill drawing each card
/// made it flicker each second and caught timers mid-roll).
/// </summary>
public sealed partial class MainWindow
{
    private ListWindow? _list;

    /// <summary>The left button went down on ⋯; the list opens when it comes back up there.</summary>
    private bool _dotsPress;

    /// <summary>How dim the big pill's content and the task icons are (1 = not; <see cref="ListMotion.Dim"/> while the list is up).</summary>
    private double _listDim = 1;

    /// <summary>The cards for the list: the big pill's first, then the task icons' order, then the rest (behind ⋯, temporary) by stack.</summary>
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
        var rest = RealSearchBoxFill();
        var chip = ThemeColors.FromSystem().Light
            ? Blend(rest, Windows.UI.Color.FromArgb(255, 0, 0, 0), 0.07)
            : Blend(rest, Windows.UI.Color.FromArgb(255, 255, 255, 255), 0.10);
        pill.UseAsListed(rest, chip, CloseChip.Radius, _text.CloseCard, _theme.Foreground);
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

    /// <summary>⋯ was clicked: the list opens (a lifted card goes down first), or goes away.</summary>
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

        if (_flying || _typing || BigPillOnScreen() is not { } pill || DotsOnScreen() is not { } dots)
        {
            return;
        }

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
            dots,
            CornerRadiusNow(),
            RealSearchBoxFill(),
            _theme.Hint,
            !ThemeColors.FromSystem().Light,
            screen.X,
            screen.X + screen.Width,
            taskbarTop,
            _settings.Bounce,
            ListMotion.Stagger(_settings.ListStagger),
            _text.OnBigPill));
        _ = Dim(ListMotion.Dim, ListMotion.Length(_settings.Bounce));
    }

    private void CloseList() => _list?.PutAway();   // its Closing brightens the pills again

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

    /// <summary>Dims (or brightens back) the big pill's content and the task icons to <paramref name="to"/> over <paramref name="length"/>.</summary>
    private async Task Dim(double to, TimeSpan length)
    {
        var from = _listDim;
        var run = ++_dimRun;
        DimVeil.Fill = BrushOf(RealSearchBoxFill());
        await Animate(length, t =>
        {
            if (run == _dimRun)
            {
                ApplyDim(SwapMotion.Lerp(from, to, SwapMotion.Ease(t)));
            }
        });
    }

    /// <summary>Bumped to stop a running <see cref="Dim"/> (a newer one, or <see cref="SetDim"/>, takes over).</summary>
    private int _dimRun;

    /// <summary>Dims at once, stopping any dimming under way.</summary>
    private void SetDim(double value)
    {
        _dimRun++;
        ApplyDim(value);
    }

    private void ApplyDim(double value)
    {
        _listDim = value;
        DimVeil.Opacity = 1 - value;
        ApplyCellDim();
    }

    /// <summary>The task icons (not ⋯) at <see cref="_listDim"/>; called again whenever the cells are built.</summary>
    private void ApplyCellDim()
    {
        foreach (var cell in TaskCells.Children.OfType<FrameworkElement>())
        {
            cell.Opacity = cell.Tag as string == DotsTag ? 1 : _listDim;
        }
    }

    /// <summary>The ⋯ cell's 24 circle on screen (pixels), or null when ⋯ isn't drawn.</summary>
    private ScreenRect? DotsOnScreen()
    {
        if (TaskCells.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Tag as string == DotsTag) is not { } cell
            || !NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var bar))
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

    /// <summary>Each tick: typing, or no card on the big pill any more, puts the list away.</summary>
    private void KeepList()
    {
        if (_list?.IsOpen == true && (_typing || _shownCard is null))
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
