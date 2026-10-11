using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.App.Media;
using IsleBar.Core.Island;
using IsleBar.Core.Ui;

namespace IsleBar.App;

/// <summary>
/// Lifting the big pill's card (TODO 7.9 F): a click on the big pill opens it, another click (or a click anywhere else) puts it
/// back. The card window is <see cref="LiftWindow"/>; while it's up the big pill keeps showing that card.
/// </summary>
public sealed partial class MainWindow
{
    private LiftWindow? _lift;

    /// <summary>The left button went down on the big pill; the card lifts when it comes back up there.</summary>
    private bool _liftPress;

    /// <summary>The task cell the left button went down on; it swaps when the button comes back up on it.</summary>
    private string? _cellPress;

    /// <summary>The card is going back because its big pill was clicked: the pill is already springing up, so no press on landing.</summary>
    private bool _closedByPill;

    private void ToggleLift()
    {
        if (_list?.IsOpen == true)
        {
            CloseList();   // with the list up, the big pill only puts it away (TODO 7.9 F)
            return;
        }

        if (_lift?.OpenKey is not null)
        {
            _lift.Collapse();
            if (PressFeedback.IsHeld(PillBody))
            {
                // Timed so the pill is back to full size just as the card lands (user 10-08: held down until landing was a noticeable
                // wait): start springing when the card's remaining way equals the pill's rise. Today they're about equal (0.22 / 0.23 s).
                _closedByPill = true;
                PressFeedback.LetGoAfter(PillBody, LiftMotion.Close - PressFeedback.RiseTime(PillBody));
            }

            return;
        }

        if (_shownCard is not { } card)
        {
            return;
        }

        // the pill's text, which the morph fades out where it is: taken when the mouse came onto the pill, so nothing waits now
        var shot = _pillShot.Key == card.Key ? _pillShot.Shot : null;
        _pillShot = default;
        OpenLift(card, shot);
    }

    /// <summary>The big pill on screen (pixels), or null while it's hidden.</summary>
    private ScreenRect? BigPillOnScreen()
    {
        if (_layout.Big is not { } big || !NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var bar))
        {
            return null;
        }

        // PillRect's right/bottom are one past for the region API; the pill itself ends one pixel earlier
        return new ScreenRect(bar.Left + big.Left, bar.Top + big.Top, bar.Left + big.Right - 1, bar.Top + big.Bottom - 1);
    }

    /// <summary>
    /// Lifts <paramref name="card"/> (the big pill's) out of the big pill, or — tapped in the list (F3) — out of that list pill
    /// (<paramref name="from"/>, drawn as <paramref name="shot"/>, with its border colour <paramref name="glow"/>).
    /// </summary>
    private void OpenLift(Card card, Microsoft.UI.Xaml.Media.ImageSource? shot, ScreenRect? from = null, Windows.UI.Color? glow = null)
    {
        if (BigPillOnScreen() is not { } pill || !NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var bar))
        {
            return;
        }

        var screen = Microsoft.UI.Windowing.DisplayArea.GetFromRect(
            new Windows.Graphics.RectInt32(pill.Left, pill.Top, pill.Width, pill.Height),
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).OuterBounds;
        if (_lift is null)
        {
            _lift = new LiftWindow();
            _lift.Gone = () =>
            {
                CloseKeptCard();
                _cards.SetHover(_hovering, DateTimeOffset.UtcNow);
                PressFeedback.LetGo(PillBody);   // the slide doesn't land in the pill: a pill held by its click comes up here
                if (_hoverBig)
                {
                    TakePillShotAhead();   // still on the pill: ready for the next click
                }
            };
            _lift.TakeShot = () => Capture(CardRow);
            _lift.Closing = () => _ = Dim(1, LiftMotion.Close, cells: false);
            _lift.KeepsPress = OnTaskCell;
            _lift.Act = OnSheetAction;
            _lift.RemoveFile = p => _feed.Watchers?.Transit?.Remove([p]);
            // copied / moved → said on the card, moved away → out of the list (the drag began moments ago: a copy is made after that)
            _lift.FileDragged = p => _ = AfterDragOut([p], DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30));
            _lift.FileDragStarting = p => _feed.Watchers?.Transit?.Snap([p]);
            _lift.Landed = () =>
            {
                // the card lands back in the pill: after a click on the pill, the pill (kept down since) springs up now; after a
                // click elsewhere it gives one whole press — either way one press per close (user 10-08)
                if (_closedByPill)
                {
                    _closedByPill = false;   // its click's press is finishing right now — one press per close
                }
                else
                {
                    PressFeedback.Pulse(PillBody);
                }
            };
        }

        var cover = CoverOf(card)?.Image;
        var look = TaskIcons.For(card, cover is not null, DateTimeOffset.UtcNow);
        _liftIconKey = LiftIconKey(card);
        _liftKept = card.Type == CardType.Temporary ? card.State : null;
        _ = Dim(ListMotion.Dim, LiftMotion.OpenLength(Animations.On), cells: false);   // the big pill dims under its card, as under the list (user 10-10)
        _lift.Open(new LiftRequest(
            card.Key,
            pill,
            new ScreenRect(bar.Left, bar.Top, bar.Right, bar.Bottom),
            CornerRadiusNow(),
            RealSearchBoxFill(),
            shot,
            screen.X,
            screen.X + screen.Width,
            TaskbarTop(),
            Animations.On,
            SheetColors(),
            LiftIcon(card),   // as on the big pill: an agent's logo in its makers' colours, a timer's turning dial (user 10-08)
            new LiftRider(LiftIcon(card), HasBigLook(look) ? CellContent(card, look, DateTimeOffset.UtcNow) : null),
            CardSheets.Build(card, DateTimeOffset.UtcNow, _text, _feed.Tails.For(card.State)),
            CoverOf(card)?.Large,
            _language,
            from,
            glow));
    }

    /// <summary>The lifted card's colours: Windows 11's popup, the theme's text, the pills' green / orange / red, the system accent for bars.</summary>
    private SheetPalette SheetColors()
    {
        var dark = !ThemeColors.FromSystem().Light;
        var ui = new Windows.UI.ViewManagement.UISettings();
        var accent = ui.GetColorValue(dark ? Windows.UI.ViewManagement.UIColorType.AccentLight2 : Windows.UI.ViewManagement.UIColorType.AccentDark1);
        return new SheetPalette(
            LiftWindow.CardColor(dark), _theme.Foreground, _theme.Hint, _theme.Ok, Attention(), Alarm(), accent,
            dark ? Windows.UI.Color.FromArgb(41, 255, 255, 255) : Windows.UI.Color.FromArgb(31, 0, 0, 0),
            dark);
    }

    /// <summary>A button on the lifted card (TODO 7.9 F2). Closing ones put the card down first, so it goes back into the pill it came from.</summary>
    private void OnSheetAction(SheetAction action)
    {
        if (_shownCard is not { } card)
        {
            return;
        }

        var state = card.State;
        TaskbarHost.Log($"lift: button {action} on {card.Key}");
        switch (action)
        {
            case SheetAction.Close or SheetAction.Hide or SheetAction.TimerCancel:
                _lift?.Collapse();
                CloseCard(card);
                break;
            case SheetAction.TimerPause or SheetAction.TimerResume:
                _feed.TogglePause(state);
                break;
            case SheetAction.TimerAgain when state.Total is > 0:
                _lift?.Collapse();
                _feed.RestartTimer(state, TimerParser.ToActivity(TimeSpan.FromSeconds(state.Total.Value), DateTimeOffset.UtcNow, state.Name));
                break;
            case SheetAction.MediaPrevious:
                _ = _feed.Media.SendAsync(MediaAction.Previous, card.Key);
                break;
            case SheetAction.MediaPlayPause:
                _ = _feed.Media.SendAsync(MediaAction.PlayPause, card.Key);
                break;
            case SheetAction.MediaNext:
                _ = _feed.Media.SendAsync(MediaAction.Next, card.Key);
                break;
            case SheetAction.Open when state.Open is { } open:
                _lift?.Collapse();
                Engine.FileActions.Open(open, revealInFolder: false);
                break;
            case SheetAction.OpenFolder:
                _lift?.Collapse();
                if (state.Open is { } done && File.Exists(done))
                {
                    Engine.FileActions.Open(done, revealInFolder: true);
                }
                else
                {
                    Engine.FileActions.Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), revealInFolder: false);
                }

                break;
            case SheetAction.Clear:
                _lift?.Collapse();
                _feed.Watchers?.Transit?.Remove(null);   // forgets where they are; the files stay where they are
                break;
            case SheetAction.Eject:
                _lift?.Collapse();
                _feed.Watchers?.Eject(state);   // the card then says "Safe to remove" or "Can't eject" (TODO ④, 10-10)
                break;
            case SheetAction.PrivacySettings:
                _lift?.Collapse();
                Engine.FileActions.Open(state.Glyph == IsleBar.Core.SystemWatch.NoticeGlyphs.Camera ? "ms-settings:privacy-webcam" : "ms-settings:privacy-microphone", revealInFolder: false);
                break;
            case SheetAction.OpenChat:
                _lift?.Collapse();
                OpenChat(card);   // a done / interrupted card goes too, like its ↗ on the pill (user 10-10)
                break;
        }
    }

    /// <summary>
    /// Each tick: while a card is lifted the big pill holds it (as if the mouse were on it); if it goes anyway (closed, finished),
    /// the lifted card goes back down.
    /// </summary>
    private void KeepLift()
    {
        // safety: held down for a close that never came (released outside the bar, the card closed another way) — never stay sunk
        if (PressFeedback.IsHeld(PillBody) && _lift?.OpenKey is not null && !NativeMethods.MouseButtonDown())
        {
            PressFeedback.LetGo(PillBody);
        }

        // a swap is under way: if it takes the lifted card off the big pill, it sends the card down into its cell itself
        if (_lift?.OpenKey is not { } key || _flying)
        {
            return;
        }

        if (_shownCard?.Key != key)
        {
            _lift.Collapse();
            return;
        }

        _cards.SetHover(true, DateTimeOffset.UtcNow);
        // a timer's number, "2 min ago", more downloads, the next song; the icon only when it changes (a timer paused: its hand stops)
        var iconKey = LiftIconKey(_shownCard);
        var icon = iconKey == _liftIconKey ? null : LiftIcon(_shownCard);
        _liftIconKey = iconKey;
        _lift.Refresh(CardSheets.Build(_shownCard, DateTimeOffset.UtcNow, _text, _feed.Tails.For(_shownCard.State)), icon, CoverOf(_shownCard)?.Large);
    }

    private void CloseLift() => _lift?.Collapse();

    /// <summary>
    /// A temporary card (a notification, "copied") while it is lifted (TODO 7.9 F4): its countdown stops — it stays even after its
    /// source has let it go — and it goes away when the card is put down (<see cref="CloseKeptCard"/>).
    /// </summary>
    private ActivityState? _liftKept;

    /// <summary>The items of this tick, with the lifted temporary card put back if its source has let it go.</summary>
    private IReadOnlyList<ActivityState> KeepLiftedCard(IReadOnlyList<ActivityState> items)
    {
        if (_liftKept is not { } kept)
        {
            return items;
        }

        var key = CardStack.KeyOf(kept);
        if (items.FirstOrDefault(s => CardStack.KeyOf(s) == key) is { } now)
        {
            _liftKept = now;   // still there: the latest of it
            return items;
        }

        return [.. items, kept];
    }

    /// <summary>The lifted temporary card has been put down: it goes, as if closed.</summary>
    private void CloseKeptCard()
    {
        if (_liftKept is not { } kept)
        {
            return;
        }

        _liftKept = null;
        var key = CardStack.KeyOf(kept);
        TaskbarHost.Log($"lift: temporary {key} put down — closed");
        CloseCard(_cards.Cards.FirstOrDefault(c => c.Key == key));
    }

    /// <summary>The lifted card's icon: as on the big pill — a timer's dial turning on its own, an agent's logo in its own colours.</summary>
    private Microsoft.UI.Xaml.UIElement LiftIcon(Card card)
    {
        var now = DateTimeOffset.UtcNow;
        var plain = BigLook(card, now);

        // music and notifications name their app in the source row, so its own icon goes beside it, as in Windows' notifications
        // (F2b, 10-09); the usual icon stays until it has loaded, and for good if the app has none to give
        var s = card.State;
        if (s.AppId is not { Length: > 0 } app
            || !(s.Kind == IsleBar.Core.Island.ActivityKind.Music || (s.Kind == IsleBar.Core.Island.ActivityKind.Notice && s.Stage == "expand")))
        {
            return plain;
        }

        var image = new Microsoft.UI.Xaml.Controls.Image
        {
            Width = 16, Height = 16, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
        };
        var box = new Microsoft.UI.Xaml.Controls.Grid();
        box.Children.Add(plain);
        box.Children.Add(image);
        AppIcon.Resolve(app, source =>
        {
            image.Source = source;
            plain.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        });
        return box;
    }

    /// <summary>What the icon is drawn from; a new icon only when this changes.</summary>
    private string LiftIconKey(Card card)
    {
        var s = card.State;
        var look = TaskIcons.For(card, CoverOf(card) is not null, DateTimeOffset.UtcNow);
        return $"{card.Key}|{card.Type}|{look}|{s.Due}|{s.Left}|{s.Total}|{s.T0}|{s.Stage}|{_theme.Icon}|{CoverOf(card)?.Image.GetHashCode()}|{s.AppId}";
    }

    private string? _liftIconKey;

    /// <summary>
    /// A press at this screen point lands on a task cell: it doesn't close the lifted card — the swap it starts on release
    /// sends the card down into its cell (user 10-08: it went back to where the big pill had been while the pill was elsewhere).
    /// </summary>
    private bool OnTaskCell(int x, int y)
    {
        if (!NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var bar))
        {
            return false;
        }

        var s = DpiSetup.Scale;
        foreach (var cell in TaskCells.Children.OfType<Microsoft.UI.Xaml.Controls.Grid>())
        {
            if (cell.Tag is not string tag)
            {
                continue;
            }

            var at = cell.TransformToVisual(Root).TransformPoint(default);
            var left = bar.Left + (at.X * s);
            var top = bar.Top + (at.Y * s);
            if (x >= left && x < left + (cell.ActualWidth * s) && y >= top && y < top + (cell.ActualHeight * s))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Where the card swapped off the big pill is in its flight (dip, in this window), for the lifted card to follow.</summary>
    private MotionBox? _oldCardBox;

    /// <summary>Where that card's icon is (its centre, dip, in this window) while it rides down into its cell; null when it doesn't.</summary>
    private (double X, double Y)? _oldIconCentre;

    /// <summary><see cref="_oldCardBox"/> and <see cref="_oldIconCentre"/> on screen (pixels), or null before the flight has placed it.</summary>
    private LiftTarget? OldCardOnScreen()
    {
        if (_oldCardBox is not { } b || !NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var bar))
        {
            return null;
        }

        var s = DpiSetup.Scale;
        double X(double dip) => bar.Left + (dip * s);
        double Y(double dip) => bar.Top + (dip * s);
        var rect = new ScreenRect((int)Math.Round(X(b.X)), (int)Math.Round(Y(b.Y)), (int)Math.Round(X(b.X + b.Width)), (int)Math.Round(Y(b.Y + b.Height)));
        return new LiftTarget(rect, b.Radius, _oldIconCentre is { } c ? (X(c.X), Y(c.Y)) : null);
    }

    /// <summary>A picture of the big pill's content taken ahead, when the mouse comes onto it (a click nearly always follows).</summary>
    private (string? Key, Microsoft.UI.Xaml.Media.ImageSource? Shot) _pillShot;

    private async void TakePillShotAhead()
    {
        if (_shownCard is not { } card || _lift?.OpenKey is not null)
        {
            return;
        }

        var shot = await Capture(CardRow);
        _pillShot = (card.Key, shot);
    }
}
