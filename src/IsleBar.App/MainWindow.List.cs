using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.Core.Island;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace IsleBar.App;

/// <summary>
/// The ⋯ list (TODO 7.9 F3, user 10-09): ⋯ opens every card as a big pill over the big pill (<see cref="ListWindow"/>); tapping one
/// puts it on the big pill (the usual swap underneath) and lifts its card straight out of that list pill. The pills are snapshots of
/// the big pill drawing each card — the very same look — taken a moment ahead, when the mouse comes onto ⋯.
/// </summary>
public sealed partial class MainWindow
{
    private ListWindow? _list;

    /// <summary>The left button went down on ⋯; the list opens when it comes back up there.</summary>
    private bool _dotsPress;

    /// <summary>Each card drawn on the big pill (its snapshot, its flowing bar, what it showed), and when (taken ahead; used while fresh).</summary>
    private Dictionary<string, (ImageSource Shot, ListFlow? Flow, string Sig)> _listShots = [];
    private DateTimeOffset _listShotsAt;
    private bool _shooting;

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

    /// <summary>The mouse came onto ⋯: draw the cards for the list now, so the click doesn't wait.</summary>
    private void TakeListShotsAhead()
    {
        if (_list?.IsOpen != true && !_shooting && DateTimeOffset.UtcNow - _listShotsAt > ShotsFresh)
        {
            _shootAhead = ShootCards(ListCards(), keep: false);
        }
    }

    /// <summary>The drawing started ahead; a click that comes while it runs waits for it.</summary>
    private Task<bool>? _shootAhead;

    private static readonly TimeSpan ShotsFresh = TimeSpan.FromSeconds(2);

    /// <summary>
    /// What a card's pill shows, as far as it changes while the list is up (a timer's second, progress, words, colour): a pill is
    /// drawn again only when this differs. A flowing bar moves on its own (the list draws it), so it isn't in here.
    /// </summary>
    private string PillSig(Card card, DateTimeOffset now)
    {
        var s = card.State;
        var fraction = s.Kind == ActivityKind.Timer ? TimerFraction(s, now) : s.Fraction;
        return $"{card.Type}|{card.Pinned}|{s.Kind}|{PrefixFor(s, now)}|{s.Title}|{s.Msg}|{s.Name}|{s.Stage}|{s.RunState}|{s.MoreCount}|{s.IsPaused}|"
               + $"{(fraction is { } f ? Math.Round(f * ProgressLength) : -1)}|{CoverOf(card)?.Image.GetHashCode()}|{TaskIcons.For(card, CoverOf(card) is not null, now)}|{IconColorFor(card)}";
    }

    /// <summary>
    /// Draws each card on the big pill in turn and snapshots it, under a stand-in of the pill as it is (nothing flickers; dimmed as
    /// the pill is while the list is up). Takes a frame per card. <paramref name="keep"/>: add to the shots already taken (a refresh
    /// of a few) instead of starting over. False when it can't now (a swap flying, typing, no big pill).
    /// </summary>
    private async Task<bool> ShootCards(IReadOnlyList<Card> cards, bool keep)
    {
        if (_shooting || _flying || _typing || _bigCard is null || cards.Count == 0)
        {
            return false;
        }

        _shooting = true;
        _flying = true;   // the tick neither redraws nor moves anything meanwhile
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var shots = keep ? new Dictionary<string, (ImageSource Shot, ListFlow? Flow, string Sig)>(_listShots) : [];
        var drawn = 0;
        try
        {
            RestMarquee();
            var big = _bigCard;
            var (own, ownFlow) = await CaptureRow();
            if (own is null)
            {
                return false;
            }

            if (!keep || cards.Any(c => c.Key == big.Key))
            {
                shots[big.Key] = (own, ownFlow, PillSig(big, DateTimeOffset.UtcNow));
                drawn++;
            }

            var others = cards.Where(c => c.Key != big.Key).ToList();
            if (others.Count > 0)
            {
                var scene = Lay(0, big, own, IconX(), _renderedCells.ToList(), DotsDrawn, _settings.BallSide == "left");
                new Mover(this, own, GlowOf(big)).Place(scene.BigBox!.Value, scene.BigBox.Value.X, scene.RowTop, scene.IconX, 1, 1, 0, 1, content: _listDim);
                foreach (var card in others)
                {
                    _shownCard = card;
                    _snapshot = new IslandSnapshot(card.State, _cards.Cards.Count - 1);
                    RenderIsland();
                    Root.UpdateLayout();
                    RenderIsland();   // the text fitted to the new card's own width (see Transition)
                    Root.UpdateLayout();
                    RestMarquee();
                    var sig = PillSig(card, DateTimeOffset.UtcNow);
                    if (await CaptureRow() is ({ } shot, var flow))
                    {
                        shots[card.Key] = (shot, flow, sig);
                        drawn++;
                    }
                }
            }

            _listShots = shots;
            if (!keep)
            {
                _listShotsAt = DateTimeOffset.UtcNow;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TaskbarHost.Log($"list: shots failed: {ex.ToString().Replace(Environment.NewLine, " | ")}");
            return false;
        }
        finally
        {
            _shownCard = _cards.Shown;   // the tick may have moved on meanwhile
            _snapshot = _shownCard is { } now ? new IslandSnapshot(now.State, _cards.Cards.Count - 1) : IslandSnapshot.Empty;
            RenderIsland();
            Root.UpdateLayout();
            FlyLayer.Children.Clear();
            _flying = false;
            _shooting = false;
            TaskbarHost.Log($"list: drew {drawn} of {cards.Count} cards in {clock.ElapsedMilliseconds} ms{(keep ? " (refresh)" : string.Empty)}");
        }
    }

    /// <summary>
    /// A snapshot of the big pill's content without its flowing bar (frozen in a picture it would sit still), and where that bar
    /// runs, for the list to draw it moving.
    /// </summary>
    private async Task<(ImageSource? Shot, ListFlow? Flow)> CaptureRow()
    {
        ListFlow? flow = null;
        if (FlowTrack.Visibility == Visibility.Visible && _flowWidth > 0)
        {
            var at = FlowTrack.TransformToVisual(CardRow).TransformPoint(default);
            var colour = (FlowPiece.Background as SolidColorBrush)?.Color ?? _theme.Foreground;
            flow = new ListFlow(at.X, at.Y, _flowWidth, colour, TimeSpan.FromSeconds(FlowSeconds));
            FlowTrack.Opacity = 0;
        }

        try
        {
            return (await Capture(CardRow), flow);
        }
        finally
        {
            FlowTrack.Opacity = 1;
        }
    }

    /// <summary>Each tick while the list is up and still: the pills whose card changed (a timer's second…) are drawn again, at most once a second.</summary>
    private async void RefreshList()
    {
        if (_list is not { IsSettled: true } list || _shooting || _flying || DateTimeOffset.UtcNow - _listRefreshedAt < ListRefresh)
        {
            return;
        }

        _listRefreshedAt = DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        var changed = list.Keys
            .Select(k => _cards.Cards.FirstOrDefault(c => c.Key == k))
            .OfType<Card>()
            .Where(c => !_listShots.TryGetValue(c.Key, out var had) || had.Sig != PillSig(c, now))
            .ToList();
        if (changed.Count == 0 || !await ShootCards(changed, keep: true) || _list is not { IsOpen: true })
        {
            return;
        }

        foreach (var card in changed)
        {
            if (_listShots.TryGetValue(card.Key, out var shot))
            {
                _list.Refresh(new ListEntry(card.Key, shot.Shot, GlowOf(card), shot.Flow));
            }
        }
    }

    private static readonly TimeSpan ListRefresh = TimeSpan.FromSeconds(1);
    private DateTimeOffset _listRefreshedAt;

    /// <summary>⋯ was clicked: the list opens (a lifted card goes down first), or goes away.</summary>
    private async void ToggleList()
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

        if (_shootAhead is { IsCompleted: false } ahead)
        {
            await ahead;
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

        if ((DateTimeOffset.UtcNow - _listShotsAt > ShotsFresh || cards.Any(c => !_listShots.ContainsKey(c.Key))) && !await ShootCards(cards, keep: false))
        {
            return;
        }

        if (_flying || _typing || BigPillOnScreen() is null)
        {
            return;   // something moved while drawing
        }

        var entries = cards.Where(c => _listShots.ContainsKey(c.Key)).Select(c => new ListEntry(c.Key, _listShots[c.Key].Shot, GlowOf(c), _listShots[c.Key].Flow)).ToList();
        if (_list is null)
        {
            _list = new ListWindow();
            _list.Tapped = OnListTapped;
            _list.Closing = () => _ = Dim(1, ListMotion.Close);
            _list.KeepsPress = OnBar;
        }

        _list.Open(new ListRequest(
            entries,
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
            OpenLift(card, _listShots.TryGetValue(key, out var shot) ? shot.Shot : null, from, GlowOf(card));
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

        RefreshList();
    }
}
