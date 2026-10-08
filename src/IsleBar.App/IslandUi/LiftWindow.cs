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
    double PillRadius,
    Color PillColor,
    ImageSource? PillShot,
    int ScreenLeft,
    int ScreenRight,
    int TaskbarTop,
    bool Tall,
    LiftStyle Style,
    bool Bounce,
    bool Dark,
    Color Foreground,
    UIElement Icon,
    string Title);

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
    private readonly Border _frame = new() { BorderThickness = new Thickness(1) };
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

        _frame.Background = _fill;
        _frame.BorderBrush = _edge;
        _content.Clip = _contentClip;
        _stage.Children.Add(_frame);
        _stage.Children.Add(_content);
        _stage.Transform3D = _tilt;
        _clipper.Children.Add(_stage);
        _root.Children.Add(_clipper);
        _root.Children.Add(_shot);   // the pill's text fades out where it is, flat — words never fly or tilt
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
        _firstFrameChecked = false;
        _layout = LiftMotion.Place(request.Pill, request.ScreenLeft, request.ScreenRight, request.TaskbarTop, DpiSetup.Scale, request.Tall);
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

        BuildPlaceholder(request);
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
        TaskbarHost.Log($"lift: open {request.Style} {request.Key} card={_layout.Card.Left},{_layout.Card.Top} {_layout.Card.Width}x{_layout.Card.Height} window={win.Left},{win.Top} {win.Width}x{win.Height} pill={request.Pill.Left},{request.Pill.Top} {request.Pill.Width}x{request.Pill.Height}");
    }

    /// <summary>Puts the card back into the big pill (or the taskbar). Nothing happens when it's not open.</summary>
    public void Collapse()
    {
        if (_request is null || _closing)
        {
            return;
        }

        _closing = true;
        _ = FetchShot();   // the pill's text may have moved on while the card was up
        _outside.Disarm();
        // a click on the taskbar (the big pill) raised the taskbar over us — the morph has to land on top of it again
        SetWindowRgn(_hwnd, IntPtr.Zero, redraw: true);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        TaskbarHost.Log($"lift: close {_request.Key}, top at pill = {Describe(TopAt(Centre(_request.Pill)))}");
        Start();
    }

    private void OnPress(int x, int y)
    {
        if (_request is null || _closing || _layout is null)
        {
            return;
        }

        // the big pill itself is the bar's (a click there toggles the card); anywhere else but the card closes it
        if (Inside(_layout.Card, x, y) || Inside(_request.Pill, x, y))
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
        Draw(_closing ? LiftMotion.Closing(t) : LiftMotion.Opening(t, _request?.Bounce == true));
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

    private TimeSpan Length() => _closing ? LiftMotion.Close : LiftMotion.OpenLength(_request?.Bounce == true);

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
            var landed = _request?.Style == LiftStyle.Morph;
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
        var card = Local(lay.Card, LiftMotion.Radius);
        var pill = Local(r.Pill, r.PillRadius);
        var cardColour = CardColor(r.Dark);
        var edgeColour = EdgeColor(r.Dark);

        MotionBox box;
        double turn, shot, content;
        if (r.Style == LiftStyle.Morph)
        {
            box = SwapMotion.Between(pill, card, q, 0);
            turn = LiftMotion.Window(q, 0, LiftMotion.ColourTurn);
            shot = 1 - LiftMotion.Window(q, 0, LiftMotion.PillTextOut);
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

        Place(_frame, box);
        _frame.CornerRadius = new CornerRadius(box.Radius);
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

    private static void Place(FrameworkElement element, MotionBox box)
    {
        Canvas.SetLeft(element, box.X);
        Canvas.SetTop(element, box.Y);
        element.Width = Math.Max(0, box.Width);
        element.Height = Math.Max(0, box.Height);
    }

    /// <summary>F1 placeholder: the card's icon and the pill's text, top left. The real inside of each card is F2.</summary>
    private void BuildPlaceholder(LiftRequest r)
    {
        _content.Children.Clear();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (r.Icon is FrameworkElement icon && icon.Parent is null)
        {
            row.Children.Add(icon);
        }

        row.Children.Add(new TextBlock
        {
            Text = r.Title,
            FontSize = 14,
            Foreground = new SolidColorBrush(r.Foreground),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = LiftMotion.Width - 56,
        });
        Canvas.SetLeft(row, 16);
        Canvas.SetTop(row, 16);
        _content.Children.Add(row);
    }

    private static Color CardColor(bool dark) => dark ? Color.FromArgb(255, 0x2C, 0x2C, 0x2C) : Color.FromArgb(255, 0xF9, 0xF9, 0xF9);

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
