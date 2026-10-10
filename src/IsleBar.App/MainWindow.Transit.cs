using IsleBar.App.Interop;
using IsleBar.Core.Island;
using IsleBar.Core.SystemWatch;
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
    private bool _dragBack;    // our own drag is over the pills again
    private bool _dragLeft;    // … and had left them since it began (it begins over them)
    private string? _swappedFor;   // files over the pills: the card that took the station's cell (it gives it back if they leave)
    private string? _cannotDrop;   // let go, but nothing had a file behind it: its name, for "无法放入"
    private string? _lastShownKey;
    private string? _beforeTransit;   // the card on the big pill before the station came up
    private string[] _draggedPaths = [];
    private DateTimeOffset _dragSince;
    private DateTime? _upSince;   // dragging out: the button came up (no DropCompleted yet)

    /// <summary>The card lifted off by the drag that's out now (or just put down).</summary>
    private bool PickedUp => (_draggingOut && !_dragBack) || DateTimeOffset.UtcNow < _heldOutUntil;

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

        return PickedUp && shown?.Key == TransitKey ? null : shown;
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

    /// <summary>The station's drag began: it is lifted off the pills, the card under it comes up.</summary>
    private void PickUp(string[] paths)
    {
        _draggingOut = true;
        _draggedPaths = paths;
        _dragSince = DateTimeOffset.UtcNow;
        _dragBack = _dragLeft = false;
        _upSince = null;
        _vanishBig = true;
        if (OtherThanTransit() is { } other)
        {
            _cards.Choose(other.Key, DateTimeOffset.UtcNow);
        }

        Restage();
    }

    /// <summary>Our own drag over the pills (it begins there; it counts once it has left them).</summary>
    private void DragOverPills()
    {
        if (!_dragLeft || _dragBack)
        {
            return;
        }

        _dragBack = true;
        TaskbarHost.Log("transit: dragged back over the pills");
        _cards.Choose(TransitKey, DateTimeOffset.UtcNow);
        Restage();
    }

    private void DragLeftPills()
    {
        _dragLeft = true;
        if (!_dragBack)
        {
            return;
        }

        _dragBack = false;
        TaskbarHost.Log("transit: dragged away again");
        _vanishBig = true;
        if (OtherThanTransit() is { } other)
        {
            _cards.Choose(other.Key, DateTimeOffset.UtcNow);
        }

        Restage();
    }

    /// <summary>The drag ended (dropped, cancelled, or the button came up without word): moved files leave; the next ticks show what's left.</summary>
    private void FinishDragOut(string how)
    {
        if (!_draggingOut)
        {
            return;
        }

        _draggingOut = false;
        _upSince = null;
        TaskbarHost.Log($"transit: dropped — {how}{(_dragBack ? " (back on the pills)" : string.Empty)}");
        if (!_dragBack)
        {
            _heldOutUntil = DateTimeOffset.UtcNow + PutDownLength;
        }

        _ = AfterDragOut(_draggedPaths, _dragSince, back: _dragBack);
        _dragBack = _dragLeft = false;
        _draggedPaths = [];
    }

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
            if (!back)
            {
                NativeMethods.GetCursorPos(out var at);
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
