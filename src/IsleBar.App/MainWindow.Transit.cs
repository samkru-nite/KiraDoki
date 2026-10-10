using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.App.Tracking;
using IsleBar.Core.Island;
using IsleBar.Core.SystemWatch;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace IsleBar.App;

/// <summary>
/// How the transit station moves on the pills (TODO ④ 丙, user 10-10). Told as what the big pill shows instead of the stack's own
/// choice (<see cref="Pretend"/>), so the usual card changes (<see cref="Transition"/>) animate it:
/// <list type="bullet">
/// <item>files dragged in: "放入中转站" on the big pill — the card there goes down into a cell, the station comes up from its own
/// cell (they swap places, as a click does) or drops in; dragged away again without letting go, everything goes back;</item>
/// <item>let go: the station stays up for its debut (the drop hint holds the place until the stack has it, no flicker);</item>
/// <item>the station dragged out: it is lifted off (gone at once — the drag image is it), the card the big pill would show without
/// it comes up; brought back over the pills, they swap back; dropped elsewhere, its icon pops into the small pill if files are
/// left (copied), or it's gone (all moved).</item>
/// </list>
/// Log "transit: …".
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The transit station's card key.</summary>
    private static readonly string TransitKey = SystemNotice.SourcePrefix + TransitShelf.Key;

    /// <summary>After a drop the hint holds the big pill this long at most, until the stack shows the station; nothing could go in →
    /// "无法放入" this long (user 10-10: 2 s, so nothing flickers).</summary>
    private static readonly TimeSpan LandingLength = TimeSpan.FromSeconds(2);

    /// <summary>After a drag out the station stays off the pills this long: the first look for moved files (0.5 s) decides whether its
    /// icon comes back at all (it popped up, then vanished).</summary>
    private static readonly TimeSpan PutDownLength = TimeSpan.FromSeconds(0.8);

    private DateTimeOffset _landingUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _heldOutUntil = DateTimeOffset.MinValue;
    private bool _dragBack;    // our own drag is over its place (it begins there)
    private string? _swappedFor;   // files over the pills: the card that took the station's cell (it gives it back if they leave)
    private string? _cannotDrop;   // let go, but nothing had a file behind it: its name, for "无法放入"
    private string? _lastShownKey;
    private string? _beforeTransit;   // the card on the big pill before the station came up
    private string[] _draggedPaths = [];
    private DateTimeOffset _dragSince;
    private DateTime? _upSince;   // dragging out: the button came up (no DropCompleted yet)
    private NativeMethods.RECT _slot;   // the big pill's place when the station was picked up (screen pixels)

    /// <summary>How much of the pill in the hand must lie on its place to count as "back" (user 10-11: by the pill's area, not the
    /// mouse's point; a touch was too eager — it flicked as it passed by — half too late).</summary>
    private const double SlotShare = 1 / 3d;
    // where on the big pill it was held. In screen pixels, though the drag's GetPosition reads like XAML's dip: logged 47 "dip" down a
    // 32 dip pill at 200 % (10-11) — and the drag image's anchor wants the same pixels (in dip × 2 it sat off, user 10-11)
    private Windows.Foundation.Point _grab;

    /// <summary>The station is in the hand away from its place (or was just put down somewhere else): off the pills.</summary>
    private bool PickedUp => (_draggingOut && !_dragBack) || DateTimeOffset.UtcNow < _heldOutUntil;

    /// <summary>Brought back over its place: the station is on the big pill already, unseen, waiting for the hand to put it down — so
    /// the window is wide enough before the hand lets go (it grew at the drop: a black square, user 10-11).</summary>
    private bool _returnPending;

    /// <summary>The big pill's card: the stack's choice, or the drop hint / nothing while the station is dragged in or out.</summary>
    private Card? Pretend(Card? shown)
    {
        var now = DateTimeOffset.UtcNow;
        if (shown?.Key != _lastShownKey)
        {
            if (shown?.Key == TransitKey && _lastShownKey is not null)
            {
                _beforeTransit = _lastShownKey;
            }

            _lastShownKey = shown?.Key;
        }

        if (_dropHover || (now < _landingUntil && (shown?.Key != TransitKey || _cannotDrop is not null)))
        {
            return new Card(TransitKey, CardType.Status, false, DropHint());
        }

        if (_cannotDrop is not null)
        {
            _cannotDrop = null;   // "无法放入" shown its time: everything back as it was
            GiveCellBack();
        }

        // in the hand, only the hand decides: over its place (or the only card) the station, unseen; away, the card under it. The
        // stack's own turns (a debut started again as the mouse left) set off swaps of their own mid-drag: a pill flashed up on
        // the taskbar while it was still held (user 10-11)
        if (_draggingOut)
        {
            var station = _cards.Cards.FirstOrDefault(c => c.Key == TransitKey);
            return _dragBack || _keepUnseen ? station ?? shown : OtherThanTransit() ?? shown;
        }

        // lifted off — unless it was the only card: then it stays on the big pill unseen while in the hand (_keepUnseen)
        return PickedUp && shown?.Key == TransitKey && !(_keepUnseen && _draggingOut) ? null : shown;
    }

    /// <summary>The cards the small pill's cells come from: all but a lifted station.</summary>
    private IReadOnlyList<Card> CellSource => PickedUp ? _cards.Cards.Where(c => c.Key != TransitKey).ToList() : _cards.Cards;

    /// <summary>"放入中转站" (or "无法放入"), then the files' names in grey.</summary>
    private ActivityState DropHint() => new()
    {
        RawKind = ActivityState.KindNotice,
        Title = _cannotDrop is null ? _text.NoticeTransitDrop : _text.NoticeTransitCannot,
        Msg = _cannotDrop ?? _dropNames,
        Glyph = NoticeGlyphs.Transit,
    };

    /// <summary>What the big pill shows, decided again now, and the change animated.</summary>
    private void Restage()
    {
        _shownCard = Pretend(_cards.Shown);
        _snapshot = _shownCard is { } shown ? new IslandSnapshot(shown.State, _cards.Cards.Count - 1) : IslandSnapshot.Empty;
        _animator.Classify(_snapshot);   // the transition is the animation: the tick mustn't add its own
        ComputeCells();
        _ = Transition(hoverKey: null);   // draws it as it is when nothing moves
    }

    /// <summary>Files came over the pills: the station's cell goes to the card on the big pill (as a click on it would).</summary>
    private void BeginDropHover()
    {
        _swappedFor = null;
        _cannotDrop = null;
        _landingUntil = DateTimeOffset.MinValue;
        if (_cards.Shown is { } shown && shown.Key != TransitKey && _cellCards.Any(c => c.Key == TransitKey))
        {
            _slots.Swap(TransitKey, shown.Key, shown.Type == CardType.Temporary);
            _swappedFor = shown.Type == CardType.Temporary ? null : shown.Key;
        }

        Restage();
    }

    /// <summary>The files went away without being let go: everything back as it was.</summary>
    private void EndDropHover()
    {
        GiveCellBack();
        Restage();
    }

    private void GiveCellBack()
    {
        if (_swappedFor is { } key && _cards.Shown?.Key == key)
        {
            _slots.Swap(key, TransitKey, false);
        }

        _swappedFor = null;
    }

    /// <summary>Files let go on the pills: the hint keeps the big pill until the station (now with them) takes it.</summary>
    private void Landing() => _landingUntil = DateTimeOffset.UtcNow + LandingLength;

    /// <summary>The station has them: it took the hint's place (its debut follows).</summary>
    private void Landed()
    {
        _swappedFor = null;
        Restack();
    }

    /// <summary>Let go, but none of it had a file behind it (inside a zip, on a phone): "无法放入 name" for the landing's 2 s.</summary>
    private void CannotDrop(string name)
    {
        _cannotDrop = name;
        Landing();
        Restage();
    }

    /// <summary>The card the big pill would show without the station: a pinned one first, then the one up before it.</summary>
    private Card? OtherThanTransit()
    {
        var cards = _cards.Cards.Where(c => c.Key != TransitKey && !_cards.IsTucked(c.Key) && c.Type != CardType.Temporary).ToList();
        return cards.FirstOrDefault(c => c.Pinned) ?? cards.FirstOrDefault(c => c.Key == _beforeTransit) ?? cards.FirstOrDefault();
    }

    /// <summary>What a drag out of the station needs, got ready as the big pill is pressed (<see cref="DragReady"/>).</summary>
    private Task<(List<Windows.Storage.IStorageItem> Items, (Windows.Graphics.Imaging.SoftwareBitmap Bitmap, double Scale)? Picture)>? _dragReady;

    /// <summary>
    /// The big pill pressed while it shows the transit station: the files, the pill's picture and the drop window (shown off-screen)
    /// are got ready now. Done when the drag began, they kept the pill stuck to its place ~0.12 s (files 40 ms, picture 25 ms, the
    /// drop window 55 ms) before Windows' drag image came — then it jumped to the mouse (user 10-11 slow motion). A click only wastes it.
    /// </summary>
    private void DragReady()
    {
        if (!TransitShown || _feed.Watchers?.Transit is not { } transit)
        {
            _dragReady = null;
            return;
        }

        _catcher?.Warm();
        _dragReady = ReadyDragOut(transit);
    }

    private async Task<(List<Windows.Storage.IStorageItem>, (Windows.Graphics.Imaging.SoftwareBitmap Bitmap, double Scale)?)> ReadyDragOut(
        SystemWatch.TransitWatcher transit)
    {
        var items = new List<Windows.Storage.IStorageItem>();
        foreach (var path in transit.Paths)
        {
            if (File.Exists(path))
            {
                items.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
            }
            else if (Directory.Exists(path))
            {
                items.Add(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path));
            }
        }

        return (items, await PillPicture());
    }

    /// <summary>The station's drag began: it is lifted off the pills, the card under it comes up.</summary>
    private void PickUp(string[] paths)
    {
        // its place: where "back" is judged by how much of the pill in the hand lies on it (InSlot)
        if (NativeMethods.GetWindowRect(_hwnd, out var window) && _layout.Big is { } big)
        {
            _slot = new NativeMethods.RECT
            {
                Left = window.Left + big.Left, Top = window.Top + big.Top, Right = window.Left + big.Right, Bottom = window.Top + big.Bottom,
            };
        }

        _draggingOut = true;
        _draggedPaths = paths;
        _dragSince = DateTimeOffset.UtcNow;
        _dragKeys = -1;   // Ctrl / Shift as the drag begins: only changes from here nudge the mouse
        _upSince = null;
        // the press that began the drag isn't a click: its release (after the drop) lifted the card that had come up (user 10-11)
        _liftPress = false;
        PressFeedback.LetGo(PillBody);

        // Picked up, the pill in the hand still lies on its place: it starts "back" — the station stays on the big pill, unseen (the
        // drag image is it), and the card under it comes up only once it's moved off (TrackHome). Let go right there = put back.
        _dragBack = true;
        _returnPending = true;
        // the only card: nothing comes up even when it's away — the big pill stays, unseen. It is what's being dragged: folded away,
        // Windows never heard the drag end (the mouse spun, then it popped up anew, user 10-11)
        _keepUnseen = OtherThanTransit() is null;
        TaskbarHost.Log($"transit: picked up{(_keepUnseen ? " — the only card: it stays on the big pill, unseen" : string.Empty)}");
        try
        {
            CheckDrag();   // the drop window up now (not at the next 50 ms look): the picture is drawn on it
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TaskbarHost.Log("dropzone: " + ex.Message);
        }

        // our picture there, the pill hides in the same go (Windows' image comes over ours a few frames later). Hidden 50 ms later, as
        // when Windows' image was hidden for ours, the pill left on its place and the one in the hand made a double (user 10-11 photo)
        HoldOverPlace();
        Restage();
    }

    /// <summary>At least <see cref="SlotShare"/> of the pill in the hand (held at <see cref="_grab"/>) lies on its place.</summary>
    private bool InSlot(NativeMethods.POINT at)
    {
        var s = DpiSetup.Scale;
        double width = PillGeometry.BigWidth * s, height = PillGeometry.Height * s;
        double left = at.X - _grab.X, top = at.Y - _grab.Y;
        var across = Math.Min(left + width, _slot.Right) - Math.Max(left, _slot.Left);
        var down = Math.Min(top + height, _slot.Bottom) - Math.Max(top, _slot.Top);
        return across > 0 && down > 0 && across * down >= width * height * SlotShare;
    }

    /// <summary>The station was the only card when picked up: it stays on the big pill, unseen, while it's away.</summary>
    private bool _keepUnseen;

    /// <summary>The big pill is drawn but not seen: the station in the hand (when it was the only card) or waiting for it.</summary>
    private bool StationUnseen => _returnPending || (_keepUnseen && PickedUpOrHeld && _shownCard?.Key == TransitKey);

    private bool PickedUpOrHeld => _draggingOut || DateTimeOffset.UtcNow < _heldOutUntil;

    /// <summary>
    /// The station's picture in the hand while it's over its place: drawn on the drop window (<see cref="IslandUi.DropCatcher"/>,
    /// see-through, over this stretch of taskbar and a pill's height above it), following the mouse every frame <em>under</em> Windows'
    /// drag image — so letting go needs no hand-over: Windows takes its image away at once at the drop, and ours, put there only then,
    /// came a moment later ("it vanished for an instant", user 10-11, slow motion). Under it, not in its place: hidden for ours, the
    /// pill in the hand stuttered whenever the bar was busy; Windows moves its own image. Windows' drag image is no window of the
    /// z-order (none was found over ours while dragging, 10-11), so ours, however topmost, stays under it. Not in the bar's own window:
    /// a strip inside the taskbar, it stuck the picture to the taskbar's edge, cut it at its end, and had to be reshaped every frame.
    /// </summary>
    private bool _handShown;

    /// <summary>Over its place: our picture lies under Windows' drag image, where that is.</summary>
    private void HoldOverPlace()
    {
        if (_handShown || _handShot is not { } shot || _catcher is not { IsUp: true } catcher)
        {
            return;
        }

        catcher.ShowHand(shot, PillGeometry.BigWidth, PillGeometry.Height);
        _handShown = true;
        PlaceHand();
        CompositionTarget.Rendering += FollowHand;
    }

    /// <summary>Off its place again (Windows' image is back), or put down.</summary>
    private void LetHandGo()
    {
        CompositionTarget.Rendering -= FollowHand;
        _catcher?.HideHand();
        _handShown = false;
    }

    private void FollowHand(object? sender, object e)
    {
        if (_draggingOut)
        {
            PlaceHand();
        }
    }

    private void PlaceHand()
    {
        NativeMethods.GetCursorPos(out var at);
        _catcher?.MoveHand(at.X - _grab.X, at.Y - _grab.Y);
    }

    /// <summary>
    /// Every 50 ms while it's in the hand: over its place (by the pill's area, <see cref="InSlot"/>) our picture lies under Windows'
    /// drag image at once; the cards follow — moved off, the card the big pill would show without it comes up; back on, that card goes
    /// down and the place waits for it. A swap still flying finishes first (the next look catches up): one cut short left the next
    /// one undone and the bar's own tick finished it without the station hidden — a pill flashed up mid-drag (user 10-11).
    /// </summary>
    private void TrackHome(NativeMethods.POINT at)
    {
        var home = InSlot(at);
        if (home && !_handShown)
        {
            HoldOverPlace();
        }
        else if (!home && _handShown)
        {
            LetHandGo();
        }

        if (home == _dragBack || _flying)
        {
            return;
        }

        _dragBack = home;
        _returnPending = home;
        TaskbarHost.Log(home ? "transit: dragged back over the pills" : "transit: dragged away again");
        if (_keepUnseen)
        {
            RenderIsland();   // the only card: it never left the big pill, nothing moves
            return;
        }

        if (home)
        {
            _cards.Choose(TransitKey, DateTimeOffset.UtcNow);   // the card that took its place goes down; the station comes up unseen
        }
        else if (OtherThanTransit() is { } other)
        {
            _cards.Choose(other.Key, DateTimeOffset.UtcNow);   // its unseen self goes; the other card comes back up
        }

        Restage();
    }

    /// <summary>The station is in the hand or waiting unseen for it: in a card swap it neither shows nor leaves a stand-in.</summary>
    private bool InHand(Card? card) => card?.Key == TransitKey && (PickedUpOrHeld || _returnPending);

    /// <summary>The drag ended (dropped, cancelled, or the button came up without word): moved files leave; the next ticks show what's left.</summary>
    private void FinishDragOut(string how)
    {
        if (!_draggingOut)
        {
            return;
        }

        NativeMethods.GetCursorPos(out var at);
        var back = InSlot(at);   // where it was let go decides, not the last 50 ms look
        var wasHome = _dragBack;   // the cards as they stand: the station waiting unseen on the big pill, or the other card up
        _draggingOut = false;
        _dragBack = false;
        var late = _upSince is { } up ? $", {(DateTime.UtcNow - up).TotalMilliseconds:0} ms after the button came up" : string.Empty;
        _upSince = null;
        TaskbarHost.Log($"transit: dropped — {how}{(back ? " (back on the pills)" : string.Empty)}{late}"
                        + $"{(_keepUnseen ? " [only card]" : string.Empty)}, pill {(PillBody.Visibility == Visibility.Visible ? "shown" : "folded")}");
        _ = AfterDragOut(_draggedPaths, _dragSince, back);
        _draggedPaths = [];
        if (back)
        {
            _ = PutBackAsync(at, wasHome);
            return;
        }

        _heldOutUntil = DateTimeOffset.UtcNow + PutDownLength;
        LetHandGo();
        if (_keepUnseen || wasHome)
        {
            // the unseen station goes without a stand-in (the only card: it comes back, copied, or not, moved); waiting on its place,
            // the card under it comes back up
            _returnPending = false;
            if (!_keepUnseen && OtherThanTransit() is { } other)
            {
                _cards.Choose(other.Key, DateTimeOffset.UtcNow);
            }

            _ = RestageWhenStill();
        }
    }

    /// <summary>Restages once the swap still flying is done (a swap asked for mid-flight is dropped).</summary>
    private async Task RestageWhenStill()
    {
        for (var wait = 0; _flying && wait < 40; wait++)
        {
            await Task.Delay(25);
        }

        Restage();
    }

    /// <summary>Put back while the card under it was up: that card goes down, the station takes the big pill unseen.</summary>
    private async Task SettleHome()
    {
        for (var wait = 0; _flying && wait < 40; wait++)
        {
            await Task.Delay(25);
        }

        if (!_keepUnseen)
        {
            _cards.Choose(TransitKey, DateTimeOffset.UtcNow);
        }

        Restage();
    }

    /// <summary>
    /// Put back (user 10-11): the pill's picture — lying under Windows' drag image while over its place, on the drop window — slides
    /// from where it was let go into its place, fast at once, slowing in, no bounce (<see cref="SwapMotion.PutBack"/>). Meanwhile the
    /// cards settle: if the card under it was still up (put back as a swap was flying), it goes down now and the station waits unseen.
    /// Then the real pill shows under the picture and the picture fades off. The drop window stays up till then
    /// (<see cref="IslandUi.DropCatcher.Holding"/>).
    /// </summary>
    private async Task PutBackAsync(NativeMethods.POINT at, bool wasHome)
    {
        CompositionTarget.Rendering -= FollowHand;   // it stays where it was let go
        var catcher = _catcher;
        if (catcher is not null)
        {
            catcher.Holding = true;
        }

        try
        {
            _returnPending = true;
            _feed.Watchers?.Transit?.PutBack();   // up for 2 s once down: a pinned card mustn't take the big pill at once (user 10-11)
            var settling = wasHome ? Task.CompletedTask : SettleHome();
            HoldOverPlace();   // (normally there already)
            if (_handShown && catcher is { IsUp: true })
            {
                var s = DpiSetup.Scale;
                var (fromX, fromY) = ((double)at.X - _grab.X, (double)at.Y - _grab.Y);   // screen pixels, the picture's top-left
                var (toX, toY) = ((double)_slot.Left, (double)_slot.Top);
                var distance = Math.Sqrt(Math.Pow(toX - fromX, 2) + Math.Pow(toY - fromY, 2)) / s;
                var length = Animations.On ? SwapMotion.PutBackLength(distance) : TimeSpan.FromMilliseconds(1);
                TaskbarHost.Log($"put back: from {fromX:0},{fromY:0} to {toX:0},{toY:0} px, {distance:0} dip in {length.TotalMilliseconds:0} ms"
                                + (wasHome ? string.Empty : " (the card under it goes down now)"));
                await Animate(length, t =>
                {
                    var e = SwapMotion.PutBack(t);
                    catcher.MoveHand(SwapMotion.Lerp(fromX, toX, e), SwapMotion.Lerp(fromY, toY, e));
                });
            }
            else
            {
                TaskbarHost.Log($"put back: no picture in the hand (shown {_handShown}, drop window up {catcher?.IsUp == true})");
            }

            await settling;
            for (var wait = 0; _flying && wait < 40; wait++)
            {
                await Task.Delay(25);   // the real pill shows only once the cards stand still
            }

            if (!_handShown || catcher is null)
            {
                return;
            }

            // down: the real pill shows under the picture, then the picture fades off it. Swapped in one frame, they flashed — the
            // picture was taken under the pressing hand (lit or dimmed), the pill now looks as it does (user 10-11)
            _returnPending = false;
            _keepUnseen = false;
            RenderIsland();
            await Animate(Animations.On ? PutBackFade : TimeSpan.FromMilliseconds(1), t => catcher.HandOpacity = 1 - t);
        }
        finally
        {
            _returnPending = false;
            _keepUnseen = false;
            RenderIsland();
            LetHandGo();
            if (catcher is not null)
            {
                catcher.Holding = false;
                if (!NativeMethods.PrimaryButtonDown())
                {
                    catcher.Down();
                    _zone = null;
                }
            }
        }
    }

    /// <summary>The picture fades off the real pill once it's down.</summary>
    private static readonly TimeSpan PutBackFade = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Files dragged out of the station (the whole pill or one row of the lifted card) were let go: which folder they landed in (the
    /// one under the mouse, or the subfolder whose icon it was on) goes to <see cref="SystemWatch.TransitWatcher.Dragged"/>, which says
    /// "已复制 / 已移动" once the files show it (user 10-10). Let go back on the pills: nothing can have happened, only moves are looked for.
    /// </summary>
    private async Task AfterDragOut(string[] paths, DateTimeOffset since, bool back = false)
    {
        string? target = null;
        try
        {
            NativeMethods.GetCursorPos(out var at);
            var onBar = TaskbarRect() is { } bar && at.X >= bar.Left && at.X < bar.Right && at.Y >= bar.Top && at.Y < bar.Bottom;
            // let go on the taskbar: no folder there (only our own drop window — asking it from another thread spun the mouse, 10-11)
            if (!back && !onBar)
            {
                var spot = await DropSpot.FindAsync(at.X, at.Y);
                target = spot?.Folder is { } folder && spot.Item is { Length: > 0 } item && Directory.Exists(Path.Combine(folder, item))
                    ? Path.Combine(folder, item)   // on a subfolder's icon: it went in there
                    : spot?.Folder;
                TaskbarHost.Log($"dropspot: let go on {spot?.WindowClass ?? "?"} — {(target is null ? "not a folder" : "a folder")}"
                                + (spot?.ItemType is { } type ? $" (under the mouse: {type})" : string.Empty));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // fire and forget: never let it take the bar down
        {
            TaskbarHost.Log("dropspot: " + ex.Message);
        }

        _feed.Watchers?.Transit?.Dragged(paths, target, since);
    }

    /// <summary>A drag out whose DropCompleted never came (the source changed under it): ended a second after the button came up.</summary>
    private void WatchDragOutEnd(bool down)
    {
        if (!_draggingOut)
        {
            return;
        }

        if (down)
        {
            _upSince = null;
        }
        else if (_upSince is null)
        {
            _upSince = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - _upSince > TimeSpan.FromSeconds(1))
        {
            FinishDragOut("button up, no word from the drop");
        }
    }

    /// <summary>The big pill as it was picked up: shown at once where it's let go back over its place, so nothing blinks while the
    /// flight home gets ready (its snapshots took ~80 ms: the drag image was gone, the pill not yet there — "it jumps", user 10-11).</summary>
    private ImageSource? _handShot;

    /// <summary>The big pill as it looks now, for the drag image (null when it can't be taken).</summary>
    private async Task<(Windows.Graphics.Imaging.SoftwareBitmap Bitmap, double Scale)?> PillPicture()
    {
        try
        {
            var shot = new RenderTargetBitmap();
            await shot.RenderAsync(PillBody);
            if (shot.PixelWidth == 0)
            {
                return null;
            }

            _handShot = shot;
            var pixels = await shot.GetPixelsAsync();
            var bitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(pixels,
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, shot.PixelWidth, shot.PixelHeight,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            return (bitmap, shot.PixelWidth / Math.Max(1, PillBody.ActualWidth));
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            TaskbarHost.Log("transit: no picture of the pill — " + ex.Message);
            return null;
        }
    }
}
