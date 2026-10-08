using System.Diagnostics;
using System.Runtime.InteropServices;
using IsleBar.App.Interop;
using IsleBar.Core.Ui;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Media3D;
using Windows.UI;
using static IsleBar.App.Interop.NativeMethods;

namespace IsleBar.App.IslandUi;

/// <summary>What the big pill hands over when its card lifts (screen pixels, colours of the moment).</summary>
internal sealed record LiftRequest(
    string Key,
    ScreenRect Pill,
    ScreenRect Bar,
    double PillRadius,
    Color PillColor,
    ImageSource? PillShot,
    int ScreenLeft,
    int ScreenRight,
    int TaskbarTop,
    LiftStyle Style,
    bool Bounce,
    SheetPalette Palette,
    UIElement Icon,
    LiftRider Rider,
    CardSheet Sheet,
    ImageSource? Cover,
    string? Language);

/// <summary>
/// The card's icon once more, for riding down into its task cell: as on the card (<paramref name="Big"/>) and, when the cell draws
/// it otherwise (an agent's logo: makers' colours → one colour + green / orange / red), as in the cell (<paramref name="Cell"/>).
/// </summary>
internal sealed record LiftRider(UIElement Big, UIElement? Cell);

/// <summary>Where the card being closed into a task cell is on this frame (screen pixels); its icon's centre when it rides down.</summary>
internal sealed record LiftTarget(ScreenRect Rect, double Radius, (double X, double Y)? Icon);

/// <summary>
/// The lifted card (TODO 7.9 F1, 10-08): a see-through, never-focused, topmost window spanning the card and the big pill, in which
/// the card's frame is drawn — so it can grow out of the pill over the taskbar (morph) or slide up out of the taskbar's edge.
/// F1 is only the frame; what goes inside is F2 (for now the icon and the pill's text, as a placeholder).
/// <para>See-through: a <see cref="TransparentBackdrop"/> (a fully transparent system backdrop), so the frame's rounded corners are
/// drawn by XAML, anti-aliased, over whatever is behind. A window region would cut jagged corners; DWM's own rounding is a fixed 8
/// and can't change shape while moving.</para>
/// </summary>
internal sealed class LiftWindow : Window
{
    private readonly Canvas _root = new();     // carries the perspective
    private readonly Canvas _clipper = new();  // the slide's cut at the taskbar's top edge (flat, so the cut stays straight)
    private readonly Canvas _stage = new();    // the sheet: frame + content, tilting around its bottom edge
    private readonly CompositeTransform3D _tilt = new();
    private readonly SmoothBorder _frame = new() { EdgeWidth = 1 };   // smooth corners, like the pills (user 10-08)
    private readonly Image _shot = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas _content = new();
    private readonly RectangleGeometry _contentClip = new();
    private readonly RectangleGeometry _rootClip = new();
    private readonly SolidColorBrush _fill = new();
    private readonly SolidColorBrush _edge = new();
    private readonly Stopwatch _clock = new();
    private readonly OutsideClick _outside;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private readonly IntPtr _hwnd;

    private FrameMeter? _meter;
    private LiftRequest? _request;
    private LiftLayout? _layout;
    private bool _closing;
    private bool _animating;
    private bool _firstFrameChecked;
    private IntPtr _foregroundBefore;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _fallback;

    /// <summary>While closing into a moving target (the card flying down into a task cell): where it is now, screen pixels.</summary>
    private Func<LiftTarget?>? _follow;
    private bool _followBounce;

    /// <summary>The card's icon riding down into its cell (not tilted, not faded with the card), from where it sat on the card.</summary>
    private readonly Grid _rider = new() { Width = 24, Height = 24, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private Windows.Foundation.Point _riderFrom;

    /// <summary>A press there is the bar's own (a task cell: its swap closes the card itself) — it doesn't close the card.</summary>
    public Func<int, int, bool>? KeepsPress { get; set; }

    /// <summary>A button on the card was clicked.</summary>
    public Action<SheetAction>? Act { get; set; }

    /// <summary>The card's inside (F2), and whether it went 3:4.</summary>
    private SheetView? _view;
    private bool _tall;

    /// <summary>Growing to 3:4 while open (more came in; it never shrinks back while open — user 10-08): from where, since when.</summary>
    private ScreenRect? _growFrom;
    private readonly Stopwatch _growClock = new();

    /// <summary>Called once the card has fully gone (after the closing animation).</summary>
    public Action? Gone { get; set; }

    /// <summary>Takes a fresh picture of the big pill's content (its text), for the morph to fade out or back in.</summary>
    public Func<Task<ImageSource?>>? TakeShot { get; set; }

    /// <summary>Called when a morph has put the card back into the big pill (the pill then gives one press).</summary>
    public Action? Landed { get; set; }

    /// <summary>The card shown, or null when closed or closing.</summary>
    public string? OpenKey => _request is { } r && !_closing ? r.Key : null;

    public LiftWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        AppWindow.IsShownInSwitchers = false;
        SystemBackdrop = new TransparentBackdrop();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var ex = Unsigned32(GetWindowLong(_hwnd, GWL_EXSTYLE));
        SetWindowLong(_hwnd, GWL_EXSTYLE, Signed32(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));

        // A plain popup, no frame at all: with the frame styles left on (even without a title bar) a 1 px white system edge showed
        // around the see-through window (user 10-08 screenshot).
        SetWindowLong(_hwnd, GWL_STYLE, Signed32(WS_POPUP | WS_CLIPSIBLINGS));
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        var none = DWMWA_COLOR_NONE;
        var square = DWMWCP_DONOTROUND;
        var hrBorder = DwmSetWindowAttributeUInt(_hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(uint));
        var hrCorner = DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));
        // The transparent backdrop alone came out black where nothing is drawn (user 10-08 screenshots). DWM only composes a
        // window with its per-pixel alpha once "blur behind" is on; an empty, off-window blur region means nothing gets blurred.
        var nothing = CreateRectRgn(-2, -2, -1, -1);
        var blur = new BlurBehind { Flags = DWM_BB_ENABLE | DWM_BB_BLURREGION, Enable = true, Region = nothing };
        var hrAlpha = DwmEnableBlurBehindWindow(_hwnd, ref blur);
        DeleteObject(nothing);
        TaskbarHost.Log($"lift: per-pixel alpha (blur behind) hr={hrAlpha:X}");
        TaskbarHost.Log($"lift: window made, style={Unsigned32(GetWindowLong(_hwnd, GWL_STYLE)):X} dwm border={hrBorder:X} corner={hrCorner:X}");
        AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));

        _frame.Fill = _fill;
        _frame.Edge = _edge;
        _content.Clip = _contentClip;
        _stage.Children.Add(_frame);
        _stage.Children.Add(_content);
        _stage.Transform3D = _tilt;
        _clipper.Children.Add(_stage);
        _root.Children.Add(_clipper);
        _root.Children.Add(_shot);   // the pill's text fades out where it is, flat — words never fly or tilt
        _root.Children.Add(_rider);
        _root.Transform3D = new PerspectiveTransform3D { Depth = LiftMotion.Depth };
        Content = _root;

        _outside = new OutsideClick((x, y) => _ui.TryEnqueue(() => OnPress(x, y)));
        Closed += (_, _) => _outside.Disarm();   // the system-wide hook otherwise stays until the process ends
    }

    /// <summary>Lifts a card out of the big pill.</summary>
    public void Open(LiftRequest request)
    {
        _request = request;
        _closing = false;
        _follow = null;
        _rider.Children.Clear();
        _rider.Visibility = Visibility.Collapsed;
        _firstFrameChecked = false;
        _growFrom = null;

        // the card's inside first: an adaptive card is 3:2 when its middle fits, else 3:4 (user 10-08)
        BuildView(request.Sheet);
        _tall = request.Sheet.Adaptive && _view!.ContentHeight() > CardSheets.MiddleHeight(request.Sheet, LiftMotion.Height) + 0.5;
        _layout = LiftMotion.Place(request.Pill, request.ScreenLeft, request.ScreenRight, request.TaskbarTop, DpiSetup.Scale, _tall, request.Bar,
            reserveTall: request.Sheet.Adaptive);
        _view!.SetSize(LiftMotion.Width, _tall ? LiftMotion.TallHeight : LiftMotion.Height);
        var win = _layout.Window;

        // A picture taken ahead (when the mouse came onto the pill) — waiting for one here held the card back a beat (user 10-08).
        // Without one the card starts at once and the picture joins a frame later.
        _shot.Width = request.Pill.Width / DpiSetup.Scale;
        _shot.Height = request.Pill.Height / DpiSetup.Scale;
        _shot.Source = request.PillShot;
        if (request.PillShot is null)
        {
            _ = FetchShot();
        }

        _root.Width = win.Width / DpiSetup.Scale;   // the perspective's centre is the root's centre
        _root.Height = win.Height / DpiSetup.Scale;
        _clipper.Clip = request.Style == LiftStyle.SlideUp ? _rootClip : null;
        _rootClip.Rect = new Windows.Foundation.Rect(0, 0, win.Width / DpiSetup.Scale, (request.TaskbarTop - win.Top) / DpiSetup.Scale);

        SetWindowRgn(_hwnd, IntPtr.Zero, redraw: true);   // the whole window while it moves
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(win.Left, win.Top, win.Width, win.Height));
        if (GetWindowRect(_hwnd, out var outer))
        {
            TaskbarHost.Log($"lift: window rect {outer.Left},{outer.Top} {outer.Right - outer.Left}x{outer.Bottom - outer.Top}, client {AppWindow.ClientSize.Width}x{AppWindow.ClientSize.Height}");
        }
        Draw(0);   // the first frame already in the start position — no flash of the end state
        _foregroundBefore = GetForegroundWindow();
        AppWindow.Show(activateWindow: false);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Start();
        _outside.Arm();
        TaskbarHost.Log($"lift: open {request.Style} {request.Key} {(_tall ? "3:4" : "3:2")}{(request.Sheet.Adaptive ? " (adaptive)" : string.Empty)} card={_layout.Card.Left},{_layout.Card.Top} {_layout.Card.Width}x{_layout.Card.Height} window={win.Left},{win.Top} {win.Width}x{win.Height} pill={request.Pill.Left},{request.Pill.Top} {request.Pill.Width}x{request.Pill.Height}");
    }

    /// <summary>
    /// Puts the card back into the big pill (or the taskbar). Nothing happens when it's not open. With <paramref name="follow"/>
    /// (morph only) it goes down into wherever that is on each frame instead — the card swapped off the big pill flying into its
    /// task cell (user 10-08) — in the swap's time and curve, so the two land together.
    /// </summary>
    public void Collapse(Func<LiftTarget?>? follow = null, bool bounce = false)
    {
        if (_request is null || _closing)
        {
            return;
        }

        _closing = true;
        _follow = _request.Style == LiftStyle.Morph ? follow : null;
        _followBounce = bounce;
        if (_follow is not null && _request.Icon is FrameworkElement icon && icon.ActualWidth > 0)
        {
            // the icon leaves the card (which fades) and rides down on its own, turning into the cell's look on the way
            var at = icon.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point(icon.ActualWidth / 2, icon.ActualHeight / 2));
            _riderFrom = at;
            icon.Opacity = 0;
            _rider.Children.Clear();
            foreach (var look in new[] { _request.Rider.Big, _request.Rider.Cell })
            {
                if (look is FrameworkElement fe && fe.Parent is null)
                {
                    fe.HorizontalAlignment = HorizontalAlignment.Center;
                    fe.VerticalAlignment = VerticalAlignment.Center;
                    _rider.Children.Add(fe);
                }
            }
        }
        _ = FetchShot();   // the pill's text may have moved on while the card was up
        _outside.Disarm();
        // a click on the taskbar (the big pill) raised the taskbar over us — the morph has to land on top of it again
        SetWindowRgn(_hwnd, IntPtr.Zero, redraw: true);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        TaskbarHost.Log($"lift: close {_request.Key}{(_follow is null ? string.Empty : " into its task cell")}, top at pill = {Describe(TopAt(Centre(_request.Pill)))}");
        Start();
    }

    private void OnPress(int x, int y)
    {
        if (_request is null || _closing || _layout is null)
        {
            return;
        }

        // the big pill itself is the bar's (a click there toggles the card); anywhere else but the card closes it
        if (Inside(_layout.Card, x, y) || Inside(_request.Pill, x, y) || KeepsPress?.Invoke(x, y) == true)
        {
            return;
        }

        Collapse();
    }

    private static bool Inside(ScreenRect r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    // ---------------- animation ----------------

    private void Start()
    {
        _clock.Restart();
        _meter = new FrameMeter(_closing ? "lift close" : "lift open");
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnFrame;
        }

        // no frames come while the screen is locked: finish anyway
        _fallback?.Stop();
        _fallback = _ui.CreateTimer();
        _fallback.Interval = Length() + TimeSpan.FromMilliseconds(400);
        _fallback.IsRepeating = false;
        _fallback.Tick += (_, _) => Finish();
        _fallback.Start();
    }

    private void OnFrame(object? sender, object e)
    {
        _meter?.Frame();
        var t = Math.Min(1, _clock.Elapsed.TotalMilliseconds / Length().TotalMilliseconds);
        Draw(!_closing ? LiftMotion.Opening(t, _request?.Bounce == true)
            : _follow is not null ? 1 - SwapMotion.Move(t, _followBounce)   // the swap's own curve, springing into the cell with it
            : LiftMotion.Closing(t));
        // The click on the big pill activates the taskbar, which then lifts itself over every topmost window — sometimes after our
        // first frame (log 10-08: "top at pill = Shell_TrayWnd" half the time). So while moving, keep putting ourselves back on top.
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        if (!_firstFrameChecked && _request is { } r)
        {
            _firstFrameChecked = true;
            // F1's checks (TODO 7.9 F): on top of the taskbar? focus left where it was?
            TaskbarHost.Log($"lift: first frame, top at pill = {Describe(TopAt(Centre(r.Pill)))}, top at card = {Describe(TopAt(Centre(_layout!.Card)))}, "
                + $"foreground kept = {GetForegroundWindow() == _foregroundBefore} ({Describe(GetForegroundWindow())})");
        }

        if (t >= 1)
        {
            if (_request is { } done)
            {
                TaskbarHost.Log($"lift: last frame, top at pill = {Describe(TopAt(Centre(done.Pill)))}");
            }

            Finish();
        }
    }

    private TimeSpan Length() => !_closing ? LiftMotion.OpenLength(_request?.Bounce == true)
        : _follow is not null ? SwapMotion.Length(_followBounce)
        : LiftMotion.Close;

    private async Task FetchShot()
    {
        if (TakeShot is not { } take)
        {
            return;
        }

        var key = _request?.Key;
        var shot = await take();
        if (shot is not null && _request?.Key == key && key is not null)
        {
            _shot.Source = shot;
        }
    }

    private void Finish()
    {
        _fallback?.Stop();
        _meter?.Report();
        _meter = null;
        if (_animating)
        {
            _animating = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        TaskbarHost.Log($"lift: {(_closing ? "closed" : "opened")} in {_clock.ElapsedMilliseconds} ms");
        if (_closing)
        {
            var landed = _request?.Style == LiftStyle.Morph && _follow is null;   // into a task cell: the big pill (another card now) gives no press
            _follow = null;
            AppWindow.Hide();
            AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
            _request = null;
            _shot.Source = null;
            _closing = false;
            if (landed)
            {
                Landed?.Invoke();
            }

            Gone?.Invoke();
            return;
        }

        Draw(1);
        // open: only the card takes clicks — the big pill under the window is the bar's again (its click closes the card)
        if (_layout is { } lay)
        {
            var region = CreateRectRgn(lay.Card.Left - lay.Window.Left, lay.Card.Top - lay.Window.Top, lay.Card.Right - lay.Window.Left, lay.Card.Bottom - lay.Window.Top);
            SetWindowRgn(_hwnd, region, redraw: true);   // the system owns the region now
            TaskbarHost.Log($"lift: settled, top at card = {Describe(TopAt(Centre(lay.Card)))}, foreground kept = {GetForegroundWindow() == _foregroundBefore}");
        }
    }

    /// <summary>Draws the frame at share <paramref name="q"/> of the way from the pill (0) to the card (1).</summary>
    private void Draw(double q)
    {
        if (_request is not { } r || _layout is not { } lay)
        {
            return;
        }

        var s = DpiSetup.Scale;
        var win = lay.Window;
        MotionBox Local(ScreenRect rect, double radius) => new((rect.Left - win.Left) / s, (rect.Top - win.Top) / s, rect.Width / s, rect.Height / s, radius);
        var cardRect = _growFrom is { } from
            ? new ScreenRect(lay.Card.Left, (int)Math.Round(SwapMotion.Lerp(from.Top, lay.Card.Top, _growShare)), lay.Card.Right, lay.Card.Bottom)
            : lay.Card;
        var card = Local(cardRect, LiftMotion.Radius);
        var pill = Local(r.Pill, r.PillRadius);
        var cardColour = r.Palette.Card;
        var edgeColour = EdgeColor(r.Palette.Dark);

        // closing into a task cell: the end is the card flying there, where it is this frame (its own stand-in draws its look)
        var follow = _closing ? _follow?.Invoke() : null;
        var target = follow is not null ? Local(follow.Rect, follow.Radius) : pill;
        var following = _closing && _follow is not null;
        Ride(following, follow, q);

        MotionBox box;
        double turn, shot, content;
        if (r.Style == LiftStyle.Morph)
        {
            box = SwapMotion.Between(target, card, q, 0);
            turn = LiftMotion.Window(q, 0, LiftMotion.ColourTurn);
            shot = following ? 0 : 1 - LiftMotion.Window(q, 0, LiftMotion.PillTextOut);
            content = LiftMotion.Window(q, LiftMotion.ContentIn, 1);
        }
        else
        {
            // out of the taskbar's top edge: at 0 the card's top is at the edge, all of it hidden below (the root is clipped there)
            var edge = (r.TaskbarTop - win.Top) / s;
            box = card with { Y = card.Y + ((1 - q) * (edge - card.Y)) };
            turn = 1;
            shot = 0;
            content = 1;
        }

        // The sheet stands up around its bottom edge (user 10-08). Drawn taller by 1/cos so that, leaning, it still looks as tall as
        // the box — the pill at the start is the pill, not a squashed one.
        var lean = LiftMotion.TiltAt(q);
        var stretch = 1 / Math.Cos(lean * Math.PI / 180);
        var bottom = box.Y + box.Height;
        box = box with { Y = bottom - (box.Height * stretch), Height = box.Height * stretch };
        _tilt.CenterX = box.X + (box.Width / 2);
        _tilt.CenterY = bottom;
        _tilt.RotationX = lean;

        // into a task cell: near the end it has the stand-in's shape and gives way to it (else its fill hides the small icon, which
        // then pops up as the window goes)
        _stage.Opacity = following ? LiftMotion.Window(q, 0, LiftMotion.FollowFade) : 1;

        Canvas.SetLeft(_frame, box.X);
        Canvas.SetTop(_frame, box.Y);
        _frame.Radius = Math.Clamp(box.Radius, 0, Math.Max(0, Math.Min(box.Width, box.Height)) / 2);
        _frame.SetSize(Math.Max(0, box.Width), Math.Max(0, box.Height));   // the shape in this frame, not after layout
        _fill.Color = Mix(r.PillColor, cardColour, turn);
        _edge.Color = Color.FromArgb((byte)(edgeColour.A * turn), edgeColour.R, edgeColour.G, edgeColour.B);

        Canvas.SetLeft(_shot, pill.X);
        Canvas.SetTop(_shot, pill.Y);
        _shot.Opacity = shot;

        // the content is laid out at the card's final place and size, never stretched; the frame uncovers it
        var contentX = r.Style == LiftStyle.Morph ? card.X : box.X;
        var contentY = r.Style == LiftStyle.Morph ? card.Y : box.Y;
        Place(_content, card with { X = contentX, Y = contentY });
        _contentClip.Rect = new Windows.Foundation.Rect(box.X - contentX, box.Y - contentY, Math.Max(0, box.Width), Math.Max(0, box.Height));
        _content.Opacity = content;
    }

    /// <summary>
    /// Closing into a task cell: the icon rides from its place on the card to the flying card's icon, turning from the card's look
    /// into the cell's over the second half, on the swap's own schedule (<see cref="SwapMotion.CellLookIn"/>). Without a riding icon
    /// down there (the flip) the card's icon just fades with the card.
    /// </summary>
    private void Ride(bool following, LiftTarget? follow, double q)
    {
        if (!following || _request is not { } r || _layout is not { } lay || _rider.Children.Count == 0)
        {
            _rider.Visibility = Visibility.Collapsed;
            return;
        }

        if (follow?.Icon is not { } to)
        {
            _rider.Visibility = Visibility.Collapsed;
            r.Icon.Opacity = 1;
            return;
        }

        var s = DpiSetup.Scale;
        var p = 1 - q;
        var x = SwapMotion.Lerp(_riderFrom.X, (to.X - lay.Window.Left) / s, p);
        var y = SwapMotion.Lerp(_riderFrom.Y, (to.Y - lay.Window.Top) / s, p);
        Canvas.SetLeft(_rider, x - (_rider.Width / 2));
        Canvas.SetTop(_rider, y - (_rider.Height / 2));
        _rider.Visibility = Visibility.Visible;
        if (r.Rider.Cell is { } cell)
        {
            r.Rider.Big.Opacity = 1 - SwapMotion.BigLookOut(p);
            cell.Opacity = SwapMotion.CellLookIn(p);
        }
    }

    private static void Place(FrameworkElement element, MotionBox box)
    {
        Canvas.SetLeft(element, box.X);
        Canvas.SetTop(element, box.Y);
        element.Width = Math.Max(0, box.Width);
        element.Height = Math.Max(0, box.Height);
    }

    /// <summary>The card's inside (F2): the four zones of <see cref="SheetView"/>, laid out at the card's final size.</summary>
    private void BuildView(CardSheet sheet)
    {
        if (_request is not { } r)
        {
            return;
        }

        _content.Children.Clear();
        if (r.Icon is FrameworkElement icon && icon.Parent is Panel old)
        {
            old.Children.Remove(icon);   // built again (another card, or the same one changed shape): the icon moves over
        }

        _view = new SheetView(sheet, r.Icon, r.Cover, r.Palette, r.Language, LiftMotion.Width) { Act = a => Act?.Invoke(a) };
        _content.Children.Add(_view.Root);
    }

    /// <summary>
    /// The card's words of the moment (each tick while it's up): changed in place when only the moving parts differ, built again
    /// otherwise. An adaptive card whose middle no longer fits grows to 3:4 — once; it never shrinks back while open (user 10-08).
    /// </summary>
    public void Refresh(CardSheet sheet, UIElement? icon, ImageSource? cover)
    {
        try
        {
            RefreshNow(sheet, icon, cover);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // the tick would swallow it silently (10-08: a card went blank after Pause, nothing in the log)
            TaskbarHost.Log($"lift: refresh failed: {ex.ToString().Replace(Environment.NewLine, " | ")}");
        }
    }

    private void RefreshNow(CardSheet sheet, UIElement? icon, ImageSource? cover)
    {
        if (_request is not { } r || _closing || _animating || _view is null || _layout is not { } lay)
        {
            return;
        }

        _request = r = r with { Sheet = sheet, Icon = icon ?? r.Icon, Cover = cover };
        if (icon is not null)
        {
            _view.SetIcon(icon);
        }

        _view.SetCover(cover);
        if (!_view.Update(sheet))
        {
            TaskbarHost.Log($"lift: rebuild {r.Key} (its layout changed)");
            BuildView(sheet);
            _view!.SetSize(LiftMotion.Width, _tall ? LiftMotion.TallHeight : LiftMotion.Height);
        }

        if (_tall || _growFrom is not null || !sheet.Adaptive || _view!.ContentHeight() <= CardSheets.MiddleHeight(sheet, LiftMotion.Height) + 0.5)
        {
            return;
        }

        _tall = true;
        var grown = LiftMotion.Place(r.Pill, r.ScreenLeft, r.ScreenRight, r.TaskbarTop, DpiSetup.Scale, true, r.Bar, reserveTall: true);
        if (grown.Window != lay.Window)
        {
            TaskbarHost.Log("lift: grow skipped, the window would move");
            _tall = false;
            return;
        }

        TaskbarHost.Log($"lift: grow {r.Key} to 3:4");
        _view.SetSize(LiftMotion.Width, LiftMotion.TallHeight);
        _growFrom = lay.Card;
        _layout = grown;
        SetWindowRgn(_hwnd, IntPtr.Zero, redraw: true);
        _growClock.Restart();
        CompositionTarget.Rendering += OnGrowFrame;
    }

    private void OnGrowFrame(object? sender, object e)
    {
        var t = Math.Min(1, _growClock.Elapsed.TotalMilliseconds / SwapMotion.Duration.TotalMilliseconds);
        _growShare = SwapMotion.Ease(t);
        Draw(1);
        if (t < 1 && !_closing)
        {
            return;
        }

        CompositionTarget.Rendering -= OnGrowFrame;
        _growFrom = null;
        if (!_closing && _layout is { } lay)
        {
            Draw(1);
            SetWindowRgn(_hwnd, CreateRectRgn(lay.Card.Left - lay.Window.Left, lay.Card.Top - lay.Window.Top, lay.Card.Right - lay.Window.Left, lay.Card.Bottom - lay.Window.Top), redraw: true);
        }
    }

    private double _growShare;

    /// <summary>The card's fill (Windows 11's popups), for <see cref="SheetPalette.Card"/>.</summary>
    public static Color CardColor(bool dark) => dark ? Color.FromArgb(255, 0x2C, 0x2C, 0x2C) : Color.FromArgb(255, 0xF9, 0xF9, 0xF9);

    private static Color EdgeColor(bool dark) => dark ? Color.FromArgb(255, 0x3C, 0x3C, 0x3C) : Color.FromArgb(255, 0xE0, 0xE0, 0xE0);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + ((b.A - a.A) * t)),
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));

    // ---------------- diagnostics (F1: verify before relying on it) ----------------

    private static POINT Centre(ScreenRect r) => new() { X = r.Left + (r.Width / 2), Y = r.Top + (r.Height / 2) };

    private static IntPtr TopAt(POINT p) => GetAncestor(WindowFromPoint(p), GA_ROOT);

    private string Describe(IntPtr window)
    {
        if (window == _hwnd)
        {
            return "ours";
        }

        var name = new char[64];
        var n = GetClassNameW(window, name, name.Length);
        return n > 0 ? new string(name, 0, n) : window.ToString("X");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    private const uint DWM_BB_ENABLE = 0x1;
    private const uint DWM_BB_BLURREGION = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Enable;
        public IntPtr Region;
        [MarshalAs(UnmanagedType.Bool)]
        public bool TransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr window, ref BlurBehind blur);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, char[] name, int max);
}
