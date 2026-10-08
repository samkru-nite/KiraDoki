using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
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

    private void ToggleLift()
    {
        if (_lift?.OpenKey is not null)
        {
            _lift.Collapse();
            return;
        }

        if (_shownCard is not { } card || _layout.Big is not { } big || !NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var bar))
        {
            return;
        }

        // the pill's text, which the morph fades out where it is: taken when the mouse came onto the pill, so nothing waits now
        var shot = _pillShot.Key == card.Key ? _pillShot.Shot : null;
        _pillShot = default;

        // PillRect's right/bottom are one past for the region API; the pill itself ends one pixel earlier
        var pill = new ScreenRect(bar.Left + big.Left, bar.Top + big.Top, bar.Left + big.Right - 1, bar.Top + big.Bottom - 1);
        var screen = Microsoft.UI.Windowing.DisplayArea.GetFromRect(
            new Windows.Graphics.RectInt32(pill.Left, pill.Top, pill.Width, pill.Height),
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).OuterBounds;
        if (_lift is null)
        {
            _lift = new LiftWindow();
            _lift.Gone = () =>
            {
                _cards.SetHover(_hovering, DateTimeOffset.UtcNow);
                PressFeedback.LetGo(PillBody);   // the slide doesn't land in the pill: a pill held by its click comes up here
                if (_hoverBig)
                {
                    TakePillShotAhead();   // still on the pill: ready for the next click
                }
            };
            _lift.TakeShot = () => Capture(CardRow);
            _lift.Landed = () =>
            {
                // the card lands back in the pill: after a click on the pill, the pill (kept down since) springs up now; after a
                // click elsewhere it gives one whole press — either way one press per close (user 10-08)
                if (PressFeedback.IsHeld(PillBody))
                {
                    PressFeedback.LetGo(PillBody);
                }
                else
                {
                    PressFeedback.Pulse(PillBody);
                }
            };
        }

        var cover = CoverOf(card)?.Image;
        _lift.Open(new LiftRequest(
            card.Key,
            pill,
            CornerRadiusNow(),
            RealSearchBoxFill(),
            shot,
            screen.X,
            screen.X + screen.Width,
            TaskbarTop(),
            LiftMotion.IsTall(card.State),
            LiftMotion.Parse(_settings.LiftAnimation),
            _settings.Bounce,
            !ThemeColors.FromSystem().Light,
            _theme.Foreground,
            IconContent(TaskIcons.For(card, cover is not null, DateTimeOffset.UtcNow), cover),
            card.State.Title ?? card.State.Msg ?? card.Key));
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

        if (_lift?.OpenKey is not { } key)
        {
            return;
        }

        if (_shownCard?.Key != key || _typing)
        {
            _lift.Collapse();
            return;
        }

        _cards.SetHover(true, DateTimeOffset.UtcNow);
    }

    private void CloseLift() => _lift?.Collapse();

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
