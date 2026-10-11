using System.Diagnostics;
using System.Numerics;
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
    bool Bounce,
    SheetPalette Palette,
    UIElement Icon,
    LiftRider Rider,
    CardSheet Sheet,
    ImageSource? Cover,
    string? Language,
    ScreenRect? From = null,
    Color? FromGlow = null)
;

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
    private readonly Canvas _stage = new();    // the sheet: frame + content, tilting around its bottom edge
    private readonly CompositeTransform3D _tilt = new();
    private readonly SmoothBorder _frame = new() { EdgeWidth = 1 };   // smooth corners, like the pills (user 10-08)
    private readonly ScaleTransform _frameStretch = new();

    /// <summary>
    /// The glass (丙④): the system's live blur under the frame's see-through fill (<see cref="_live"/>), its shape moved with the
    /// frame every frame. <see cref="_glassShare"/> = how far the fill has thinned (0 solid … 1 <see cref="LiftMotion.GlassDensity"/>).
    /// </summary>
    private bool _live;
    private double _glassShare;

    /// <summary>XAML leans the sheet's top towards you; System.Numerics' positive X turn takes it away — so the glass turns the other way (user 10-10, slowed down: the edge stood wider than the glass at the top).</summary>
    private const float GlassLeanSign = -1;
    private readonly Image _shot = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas _content = new();
    private readonly CompositeTransform _zoom = new();
    private readonly RectangleGeometry _contentClip = new();
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

    /// <summary>The transit list's ✕ / a row dragged out (the file's path).</summary>
    public Action<string>? RemoveFile { get; set; }
    public Action<IReadOnlyList<string>>? FileDragged { get; set; }
    public Action<IReadOnlyList<string>>? FileDragStarting { get; set; }

    /// <summary>The card's inside (F2), and its height (200 / 300 / 400, <see cref="LiftMotion.Fit"/>).</summary>
    private SheetView? _view;
    private double _height = LiftMotion.Height;

    /// <summary>Growing a step taller while open (more came in; it never shrinks back while open — user 10-08): from where, since when.</summary>
    private ScreenRect? _growFrom;
    private readonly Stopwatch _growClock = new();

    /// <summary>Called once the card has fully gone (after the closing animation).</summary>
    public Action? Gone { get; set; }

    /// <summary>The card starts going down (however it was closed): the bar brightens the big pill again.</summary>
    public Action? Closing { get; set; }

    /// <summary>Takes a fresh picture of the big pill's content (its text), for the morph to fade out or back in.</summary>
    public Func<Task<ImageSource?>>? TakeShot { get; set; }

    /// <summary>Called when a morph has put the card back into the big pill (the pill then gives one press).</summary>
    public Action? Landed { get; set; }

    /// <summary>The card shown, or null when closed or closing.</summary>
    public string? OpenKey => _request is { } r && !_closing ? r.Key : null;

    public LiftWindow()
    {
        _hwnd = SeeThrough.Make(this, "lift");

        _frame.Fill = _fill;
        _frame.Edge = _edge;
        _frame.RenderTransform = _frameStretch;

        _content.Clip = _contentClip;
        _content.RenderTransform = _zoom;
        _stage.Children.Add(_frame);
        _stage.Children.Add(_content);
        _stage.Transform3D = _tilt;
        _root.Children.Add(_stage);
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
        _glassShare = 0;

        // the whole window in the card's theme before its inside is made: the buttons came out light for a frame, then dark (user
        // 10-10: "the buttons flash from bright to dark")
        _root.RequestedTheme = request.Palette.Dark ? ElementTheme.Dark : ElementTheme.Light;

        // the card's inside first: an adaptive card takes the lowest height its middle fits in (200 / 300 / 400, user 10-10)
        BuildView(request.Sheet);
        _height = LiftMotion.Fit(request.Sheet, _view!.ContentHeight());
        _layout = LiftMotion.Place(request.Pill, request.ScreenLeft, request.ScreenRight, request.TaskbarTop, DpiSetup.Scale, _height, request.Bar,
            reserveTall: request.Sheet.Adaptive, from: request.From);
        _view!.SetSize(LiftMotion.Width, _height);
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

        SetWindowRgn(_hwnd, IntPtr.Zero, redraw: true);   // the whole window while it moves
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(win.Left, win.Top, win.Width, win.Height));
        // Windows' own acrylic (user 10-10: the glass doesn't follow the taskbar; reference = the notification centre). Dark: between
        // WinUI's "base" (#202020 × 0.5 — a bit dark) and "default" (#2C2C2C × 0.15 — a bit light), luminosity 0.96; light: WinUI's
        // default, #FCFCFC × 0, 0.85
        var dark = request.Palette.Dark;
        TaskbarHost.Log($"lift: glass? {Frost.Why()}");
        _live = Frost.Allowed() && SystemBackdrop is TransparentBackdrop backdrop && backdrop.StartGlass(win.Width, win.Height,
            dark ? Color.FromArgb(255, 0x26, 0x26, 0x26) : Color.FromArgb(255, 0xFC, 0xFC, 0xFC), dark ? 0.3f : 0f, dark ? 0.96f : 0.85f);
        if (GetWindowRect(_hwnd, out var outer))
        {
            TaskbarHost.Log($"lift: window rect {outer.Left},{outer.Top} {outer.Right - outer.Left}x{outer.Bottom - outer.Top}, client {AppWindow.ClientSize.Width}x{AppWindow.ClientSize.Height}");
        }
        Draw(0);   // the first frame already in the start position — no flash of the end state
        _foregroundBefore = GetForegroundWindow();
        AppWindow.Show(activateWindow: false);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        _slow = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IsleBar", "slow"));
        Start();
        _outside.Arm();
        TaskbarHost.Log($"lift: open {request.Key} {_height}{(request.Sheet.Adaptive ? " (adaptive)" : string.Empty)} card={_layout.Card.Left},{_layout.Card.Top} {_layout.Card.Width}x{_layout.Card.Height} window={win.Left},{win.Top} {win.Width}x{win.Height} pill={request.Pill.Left},{request.Pill.Top} {request.Pill.Width}x{request.Pill.Height}");
    }

    /// <summary>
    /// Puts the card back into the big pill. Nothing happens when it's not open. With <paramref name="follow"/> it goes down into wherever that is on each frame instead — the card swapped off the big pill flying into its
    /// task cell (user 10-08) — in the swap's time and curve, so the two land together.
    /// </summary>
    public void Collapse(Func<LiftTarget?>? follow = null, bool bounce = false)
    {
        if (_request is null || _closing)
        {
            return;
        }

        _closing = true;

        Closing?.Invoke();
        _follow = follow;
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

    /// <summary>
    /// The live glass's shape this frame, in window pixels: the frame's rounded rectangle (upright size), stretched by 1/cos and
    /// leaning around its bottom edge as the XAML sheet does, seen in the root's perspective (centre = the window's centre).
    /// </summary>
    private void MoveGlass(LiftRequest r, MotionBox box, double upright, double bottom, double stretch, double lean, Color solid)
    {
        if (SystemBackdrop is not TransparentBackdrop backdrop || _layout is not { } lay)
        {
            return;
        }

        var s = (float)DpiSetup.Scale;
        var win = lay.Window;
        var cx = (float)(box.X + (box.Width / 2)) * s;
        var by = (float)bottom * s;
        var perspective = Matrix4x4.Identity;
        perspective.M34 = -1 / (float)(LiftMotion.Depth * s);
        var centre = Matrix4x4.CreateTranslation(-win.Width / 2f, -win.Height / 2f, 0);
        var m = Matrix4x4.CreateTranslation(-cx, -by, 0)
            * Matrix4x4.CreateScale(1, (float)stretch, 1)
            * Matrix4x4.CreateRotationX(GlassLeanSign * (float)(lean * Math.PI / 180))
            * Matrix4x4.CreateTranslation(cx, by, 0)
            * centre * perspective * Matrix4x4.CreateTranslation(win.Width / 2f, win.Height / 2f, 0);
        var cut = win.Height;
        backdrop.MoveGlass(new Vector2((float)box.X * s, (float)(bottom - upright) * s), new Vector2((float)box.Width * s, (float)upright * s),
            (float)_frame.Radius * s, m, (float)_stage.Opacity, cut, solid);
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
        _glassShare = LiftMotion.GlassAt(t, _closing);
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

    private TimeSpan Length() => (!_closing ? LiftMotion.OpenLength(_request?.Bounce == true)
        : _follow is not null ? SwapMotion.Length(_followBounce)
        : LiftMotion.Close) * (_slow ? 8 : 1);

    /// <summary>DIAGNOSTIC (丙④, temporary): the card's animations 8× slower while %LOCALAPPDATA%\IsleBar\slow exists (read at each open).</summary>
    private bool _slow;

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
            var landed = _request is not null && _follow is null;   // into a task cell: the big pill (another card now) gives no press
            _follow = null;
            AppWindow.Hide();
            AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
            _request = null;
            _shot.Source = null;
            if (_live)
            {
                _live = false;
                (SystemBackdrop as TransparentBackdrop)?.HideGlass();
            }
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
        // opening from a pill of the list (F3) it grows out of that pill; it always goes back into the big pill
        var pill = !_closing && r.From is { } from0 ? Local(from0, r.PillRadius) : Local(r.Pill, r.PillRadius);
        var cardColour = r.Palette.Card;
        var edgeColour = EdgeColor(r.Palette.Dark);

        // closing into a task cell: the end is the card flying there, where it is this frame (its own stand-in draws its look)
        var follow = _closing ? _follow?.Invoke() : null;
        var target = follow is not null ? Local(follow.Rect, follow.Radius) : pill;
        var following = _closing && _follow is not null;
        Ride(following, follow, q);

        // past the card's place it swells as a whole, its content with it (Apple's way, 10-10): the frame drawn bigger, the content
        // scaled to match — before, the frame's far edges ran out alone ("the skin pulled out")
        var (box, scale) = SwapMotion.Settle(target, card, q);
        var turn = LiftMotion.Window(q, 0, LiftMotion.ColourTurn);
        var shot = following ? 0 : 1 - LiftMotion.Window(q, 0, LiftMotion.PillTextOut);
        var content = LiftMotion.Window(q, LiftMotion.ContentIn, 1);

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

        // The frame is drawn at its upright height and stretched by 1/cos as a whole: leaning, its edge and corners then look as they
        // do upright — stretching only the shape left the top and bottom edges 0.87 thick and the corners squashed (user 10-10: the
        // card's bottom line didn't match the pill's as it went back in).
        var upright = box.Height / stretch;
        Canvas.SetLeft(_frame, box.X);
        Canvas.SetTop(_frame, bottom - upright);
        _frame.Radius = Math.Clamp(box.Radius, 0, Math.Max(0, Math.Min(box.Width, upright)) / 2);
        _frame.SetSize(Math.Max(0, box.Width), Math.Max(0, upright));   // the shape in this frame, not after layout
        _frameStretch.CenterY = upright;
        _frameStretch.ScaleY = stretch;
        _fill.Color = Mix(r.PillColor, cardColour, turn);
        if (_live)
        {
            // the card's colour goes over the glass in the glass's own layer (see TransparentBackdrop.MoveGlass); its own fill stays clear
            var solid = Color.FromArgb((byte)Math.Round(255 * (1 - _glassShare)), _fill.Color.R, _fill.Color.G, _fill.Color.B);
            _fill.Color = Color.FromArgb(0, _fill.Color.R, _fill.Color.G, _fill.Color.B);
            MoveGlass(r, box, upright, bottom, stretch, lean, solid);
        }
        if (!_closing && r.FromGlow is { } glow)
        {
            // a list pill with a coloured border (done green, waiting orange): the border turns into the card's edge
            _frame.EdgeWidth = 2 - turn;
            _edge.Color = Mix(glow, edgeColour, turn);
        }
        else
        {
            // from the pill's own faint edge to the card's (it faded from nothing: landing, the card had no edge and the pill's popped in)
            _frame.EdgeWidth = 1;
            _edge.Color = Mix(PillView.EdgeFill(), edgeColour, turn);
        }

        Canvas.SetLeft(_shot, pill.X);
        Canvas.SetTop(_shot, pill.Y);
        _shot.Opacity = shot;

        // the content is laid out at the card's final place and size, never stretched; the frame uncovers it
        Place(_content, card);
        if (q > 1)
        {
            _zoom.ScaleX = _zoom.ScaleY = scale;
            _zoom.TranslateX = box.X - card.X;
            _zoom.TranslateY = box.Y - card.Y;   // upright by then (no lean past the end)
            _contentClip.Rect = new Windows.Foundation.Rect(0, 0, card.Width, card.Height);
        }
        else
        {
            _zoom.ScaleX = _zoom.ScaleY = 1;
            _zoom.TranslateX = _zoom.TranslateY = 0;
            _contentClip.Rect = new Windows.Foundation.Rect(box.X - card.X, box.Y - card.Y, Math.Max(0, box.Width), Math.Max(0, box.Height));
        }

        _content.Opacity = content;
    }

    /// <summary>
    /// Closing into a task cell: the icon rides from its place on the card to the flying card's icon, turning from the card's look
    /// into the cell's over the second half, on the swap's own schedule (<see cref="SwapMotion.CellLookIn"/>).
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

        // built again (another card, or the same one changed shape): the old card lets go of the icon first, so it can move over
        _view?.ReleaseIcon();
        _content.Children.Clear();
        if (r.Icon is FrameworkElement icon && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(icon) is { } still)
        {
            // checking icon.Parent alone missed it (10-09) — say where it hung if this ever comes back
            TaskbarHost.Log($"lift: icon still under {still.GetType().Name} ({icon.Parent?.GetType().Name ?? "no logical parent"}) — taken out");
            switch (still)
            {
                case Panel p:
                    p.Children.Remove(icon);
                    break;
                case Border b:
                    b.Child = null;
                    break;
                case ContentControl c:
                    c.Content = null;
                    break;
            }
        }

        _view = new SheetView(sheet, r.Icon, r.Cover, r.Palette, r.Language, LiftMotion.Width)
        {
            Act = a => Act?.Invoke(a), RemoveFile = p => RemoveFile?.Invoke(p), FileDragged = p => FileDragged?.Invoke(p),
            FileDragStarting = p => FileDragStarting?.Invoke(p),
        };
        _content.Children.Add(_view.Root);
    }

    /// <summary>
    /// The card's words of the moment (each tick while it's up): changed in place when only the moving parts differ, built again
    /// otherwise. An adaptive card whose middle no longer fits grows to the next height that holds it (300 / 400); it never shrinks back while open (user 10-08).
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
            _view!.SetSize(LiftMotion.Width, _height);
        }

        var fit = LiftMotion.Fit(sheet, _view!.ContentHeight());
        if (fit <= _height || _growFrom is not null)
        {
            return;   // only grows while open (user 10-08)
        }

        var grown = LiftMotion.Place(r.Pill, r.ScreenLeft, r.ScreenRight, r.TaskbarTop, DpiSetup.Scale, fit, r.Bar, reserveTall: true, from: r.From);
        if (grown.Window != lay.Window)
        {
            TaskbarHost.Log("lift: grow skipped, the window would move");
            return;
        }

        TaskbarHost.Log($"lift: grow {r.Key} {_height} to {fit}");
        _height = fit;
        _view.SetSize(LiftMotion.Width, fit);
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

    internal static Color EdgeColor(bool dark) => dark ? Color.FromArgb(255, 0x3C, 0x3C, 0x3C) : Color.FromArgb(255, 0xE0, 0xE0, 0xE0);

    internal static Color Mix(Color a, Color b, double t) => Color.FromArgb(
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, char[] name, int max);
}
