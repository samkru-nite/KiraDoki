using System.Diagnostics;
using IsleBar.App.Interop;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static IsleBar.App.Interop.NativeMethods;

namespace IsleBar.App.IslandUi;

/// <summary>What the bar hands over to open the list (screen pixels, colours of the moment). Key 0 is the big pill's card.</summary>
/// <param name="Host">What each list pill draws with (the bar).</param>
/// <param name="Prepare">Readies a new list pill (its colours as at rest).</param>
/// <param name="Render">Draws a card on its list pill — at opening and on every tick while the list is up.</param>
/// <param name="Glow">A card's border colour (null = none).</param>
/// <param name="Present">Whether a card is still there: one that went (closed, ended) leaves the list, the rest close the gap.</param>
/// <param name="ButtonTip">The hint over a card's button (null = none).</param>
internal sealed record ListRequest(
    IReadOnlyList<string> Keys,
    IPillHost Host,
    Action<PillView> Prepare,
    Action<string, PillView> Render,
    Func<string, Color?> Glow,
    Func<string, bool> Present,
    Func<string, string?> ButtonTip,
    ScreenRect Pill,
    ScreenRect Ball,
    double PillRadius,
    Color PillColor,
    bool Dark,
    int ScreenLeft,
    int ScreenRight,
    int TaskbarTop,
    bool Bounce);

/// <summary>
/// The list (TODO 7.9 F3, user 10-09; opened by the circle since TODO ①½): every card drawn by a pill of its own (<see cref="PillView"/>, the big pill's very
/// drawing, live: timers tick, bars flow — plan B, 10-10), stacked over the big pill, each flying out of
/// the circle and growing into its pill on the way (nearest first, a stagger apart, bowing up). A tap on one hands it to the bar,
/// which lifts its card out of that very pill (<see cref="LiftWindow"/>, with <see cref="LiftRequest.From"/>); the others fade.
/// A tap on a pill's button ("Close", ↗) does what the big pill's does (user 10-10); a card that goes while the list is up — closed
/// there, or ended by itself — shrinks away and the pills above come down (<see cref="ListMotion.Leave"/>).
/// Anything else closes it: they fly back into the circle. See-through, never focused, topmost, like the lifted card. Numbers: <see cref="ListMotion"/>.
/// </summary>
internal sealed class ListWindow : Window
{
    private readonly Canvas _root = new();
    private readonly Stopwatch _clock = new();
    private readonly OutsideClick _outside;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private readonly IntPtr _hwnd;
    private readonly List<Item> _items = [];

    private ListRequest? _request;
    private ScreenRect _window;
    private MotionBox _ball;
    private MotionBox _pill;
    private double _taskbarTop;
    private Phase _phase = Phase.Shut;
    private string? _taken;
    private bool _animating;
    private FrameMeter? _meter;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _fallback;

    private enum Phase
    {
        Shut,
        Opening,
        Open,
        Shifting,   // open; pills of cards that went shrink away, the rest come down
        Closing,
        Taking,
    }

    /// <summary>A pill was tapped: its card and where the pill is on screen now.</summary>
    public Action<string, ScreenRect>? Tapped { get; set; }

    /// <summary>A pill's button was tapped: its card.</summary>
    public Action<string>? ButtonTapped { get; set; }

    /// <summary>The list starts going back into the circle (however it was closed): the bar brightens its pills again.</summary>
    public Action? Closing { get; set; }

    /// <summary>A press there is the bar's own (it closes the list itself) — the list doesn't close on it.</summary>
    public Func<int, int, bool>? KeepsPress { get; set; }

    /// <summary>Up and not on its way out.</summary>
    public bool IsOpen => _phase is Phase.Opening or Phase.Open or Phase.Shifting;

    /// <summary>The mouse came onto a list pill (its card, its pill): a click nearly always follows.</summary>
    public Action<string, PillView>? Hovered { get; set; }

    /// <summary>Each tick while up: every pill draws its card as it is now, its border and its button; cards that went leave.</summary>
    public void Redraw()
    {
        if (_request is not { } r)
        {
            return;
        }

        foreach (var item in _items.Where(i => !i.Leaving))
        {
            r.Render(item.Key, item.Pill);
            item.SetGlow(r.Glow(item.Key));
            item.ShowButton(r.ButtonTip(item.Key));
        }

        if (_phase == Phase.Open && _items.Where(i => !r.Present(i.Key)).ToList() is { Count: > 0 } gone)
        {
            Shift(gone);   // while it opens or shifts already: on a later tick
        }
    }

    /// <summary>The pills of cards that went shrink away, the rest come down into their new places (lowest first, as opened).</summary>
    private void Shift(List<Item> gone)
    {
        var index = 0;
        foreach (var item in _items)
        {
            item.From = item.Slot;
            if (gone.Contains(item))
            {
                item.Leaving = true;
                item.ShowButton(null);
            }
            else
            {
                item.Slot = ListMotion.Slot(index++, _pill, _taskbarTop);
            }
        }

        // the window's click region stays as it was meanwhile: the old places cover the new ones (they only come down)
        _phase = Phase.Shifting;
        TaskbarHost.Log($"list: {string.Join(", ", gone.Select(i => i.Key))} went, {index} left");
        Start();
    }

    public ListWindow()
    {
        _hwnd = SeeThrough.Make(this, "list");
        Content = _root;
        _outside = new OutsideClick((x, y) => _ui.TryEnqueue(() => OnPress(x, y)));
        Closed += (_, _) => _outside.Disarm();
    }

    public void Open(ListRequest request)
    {
        _request = request;
        _taken = null;
        var s = DpiSetup.Scale;
        var n = request.Keys.Count;

        // the window: the stack, the circle and the big pill, with room for the bounce and the arc
        var room = PillGeometry.Px(LiftMotion.Room, s);
        var pillH = request.Pill.Height;
        var step = PillGeometry.Px(PillGeometry.Height + ListMotion.Gap, s);
        var top = request.TaskbarTop - PillGeometry.Px(ListMotion.Gap, s) - pillH - ((n - 1) * step);
        _window = new ScreenRect(
            Math.Min(request.Pill.Left, request.Ball.Left) - room,
            top - room - PillGeometry.Px(ListMotion.Arc, s),
            Math.Max(request.Pill.Right, request.Ball.Right) + room,
            Math.Max(request.Pill.Bottom, request.Ball.Bottom));

        MotionBox Local(ScreenRect r, double radius) => new((r.Left - _window.Left) / s, (r.Top - _window.Top) / s, r.Width / s, r.Height / s, radius);
        var pill = Local(request.Pill, request.PillRadius);
        _ball = Local(request.Ball, request.Ball.Width / s / 2);
        var taskbarTop = (request.TaskbarTop - _window.Top) / s;
        (_pill, _taskbarTop) = (pill, taskbarTop);

        _root.Children.Clear();
        _items.Clear();
        var edge = PillView.EdgeFill();   // they're pills: the pills' edge (TODO 丙④)
        for (var i = 0; i < n; i++)
        {
            var item = new Item(request.Keys[i], ListMotion.Slot(i, pill, taskbarTop), request.PillColor, edge, pill.Width, pill.Height, request.Host);
            request.Prepare(item.Pill);
            item.Box.Tapped += (_, _) => OnTapped(item);
            item.Box.PointerEntered += (_, _) => Hovered?.Invoke(item.Key, item.Pill);
            item.Button.Tapped += (_, e) =>
            {
                e.Handled = true;   // the button's, not the pill's (no lift)
                OnButton(item);
            };
            item.Button.PointerEntered += (_, _) => item.Pill.LightButton(_phase == Phase.Open);
            item.Button.PointerExited += (_, _) => item.Pill.LightButton(false);
            _items.Add(item);
            _root.Children.Add(item.Box);
        }

        // drawn twice, as the big pill is after a change: the second time the text fits its column's real width (a close button
        // narrows it), measured by the layout in between
        Redraw();
        _root.UpdateLayout();
        Redraw();

        _root.Width = _window.Width / s;
        _root.Height = _window.Height / s;

        SetWindowRgn(_hwnd, IntPtr.Zero, redraw: true);   // the whole window while it moves
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(_window.Left, _window.Top, _window.Width, _window.Height));
        _phase = Phase.Opening;
        Draw(TimeSpan.Zero);   // the first frame already in the start position
        AppWindow.Show(activateWindow: false);
        SeeThrough.OnTop(_hwnd);
        Start();
        _outside.Arm();
        TaskbarHost.Log($"list: open {n} ({string.Join(", ", request.Keys)}) stagger {ListMotion.StaggerMs} window={_window.Left},{_window.Top} {_window.Width}x{_window.Height}");
    }

    /// <summary>Back into the circle (nothing happens when it's not up).</summary>
    public void PutAway()
    {
        if (!IsOpen)
        {
            return;
        }

        foreach (var leaving in _items.Where(i => i.Leaving).ToList())
        {
            _root.Children.Remove(leaving.Box);   // shrinking away: just gone
            _items.Remove(leaving);
        }

        _phase = Phase.Closing;
        _outside.Disarm();
        SetWindowRgn(_hwnd, IntPtr.Zero, redraw: true);
        SeeThrough.OnTop(_hwnd);
        TaskbarHost.Log("list: close");
        Closing?.Invoke();
        Start();
    }

    private void OnTapped(Item item)
    {
        if (_phase != Phase.Open)
        {
            return;
        }

        var s = DpiSetup.Scale;
        var b = item.Slot;
        var rect = new ScreenRect(
            _window.Left + (int)Math.Round(b.X * s), _window.Top + (int)Math.Round(b.Y * s),
            _window.Left + (int)Math.Round((b.X + b.Width) * s), _window.Top + (int)Math.Round((b.Y + b.Height) * s));

        // the lifted card takes this pill's place from the next frame on: it goes now, the others fade
        _phase = Phase.Taking;
        _taken = item.Key;
        _outside.Disarm();
        TaskbarHost.Log($"list: tap {item.Key} at {rect.Left},{rect.Top}");
        Start();
        Tapped?.Invoke(item.Key, rect);
    }

    private void OnButton(Item item)
    {
        if (_phase != Phase.Open)
        {
            return;
        }

        TaskbarHost.Log($"list: button of {item.Key}");
        item.Pill.LightButton(false);
        ButtonTapped?.Invoke(item.Key);
        Redraw();   // a closed card leaves now, not on the next tick
    }

    private void OnPress(int x, int y)
    {
        if (!IsOpen)
        {
            return;
        }

        var s = DpiSetup.Scale;
        foreach (var item in _items)
        {
            var b = item.Slot;
            var left = _window.Left + (b.X * s);
            var top = _window.Top + (b.Y * s);
            if (x >= left && x < left + (b.Width * s) && y >= top && y < top + (b.Height * s))
            {
                return;   // its tap handles it
            }
        }

        if (KeepsPress?.Invoke(x, y) == true)
        {
            return;
        }

        PutAway();
    }

    // ---------------- animation ----------------

    private void Start()
    {
        _clock.Restart();
        _meter = new FrameMeter($"list {_phase.ToString().ToLowerInvariant()}");
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnFrame;
        }

        _fallback?.Stop();
        _fallback = _ui.CreateTimer();
        _fallback.Interval = Length() + TimeSpan.FromMilliseconds(400);
        _fallback.IsRepeating = false;
        _fallback.Tick += (_, _) => Finish();
        _fallback.Start();
    }

    private TimeSpan Length() => _phase switch
    {
        Phase.Opening => ListMotion.Total(_items.Count, _request?.Bounce == true, ListMotion.StaggerMs),
        Phase.Taking => LiftMotion.OpenLength(_request?.Bounce == true),
        Phase.Shifting => ListMotion.Length(_request?.Bounce == true),
        _ => ListMotion.Close,
    };

    private void OnFrame(object? sender, object e)
    {
        _meter?.Frame();
        var work = Perf.Now;   // DIAGNOSTIC (performance, 10-12): our own work in this frame
        var elapsed = _clock.Elapsed;
        Draw(elapsed);
        if (_phase != Phase.Taking)
        {
            SeeThrough.OnTop(_hwnd);   // a click on the taskbar lifts it over every topmost window — but never over the lifted card
        }
        _meter?.Work(work);
        if (elapsed >= Length())
        {
            Finish();
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

        if (_phase == Phase.Opening)
        {
            _phase = Phase.Open;
            Draw(Length());
            ClickOnlyPills();
            return;
        }

        if (_phase == Phase.Shifting)
        {
            foreach (var leaving in _items.Where(i => i.Leaving).ToList())
            {
                _root.Children.Remove(leaving.Box);
                _items.Remove(leaving);
            }

            _phase = Phase.Open;
            Draw(Length());
            if (_items.Count == 0)
            {
                PutAway();   // the last one went
                return;
            }

            ClickOnlyPills();
            return;
        }

        if (_phase is Phase.Closing or Phase.Taking)
        {
            _phase = Phase.Shut;
            AppWindow.Hide();
            AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
            _root.Children.Clear();
            _items.Clear();
            _request = null;
        }
    }

    /// <summary>Settled: only the pills take clicks — the bar under the window is the bar's again.</summary>
    private void ClickOnlyPills()
    {
        var s = DpiSetup.Scale;
        var region = CreateRectRgn(0, 0, 0, 0);
        foreach (var item in _items)
        {
            var b = item.Slot;
            var one = CreateRectRgn((int)Math.Round(b.X * s), (int)Math.Round(b.Y * s), (int)Math.Round((b.X + b.Width) * s), (int)Math.Round((b.Y + b.Height) * s));
            CombineRgn(region, region, one, RGN_OR);
            DeleteObject(one);
        }

        SetWindowRgn(_hwnd, region, redraw: true);   // the system owns the region now
    }

    private void Draw(TimeSpan elapsed)
    {
        if (_request is not { } r)
        {
            return;
        }

        var t = Math.Min(1, elapsed / Length());
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            switch (_phase)
            {
                case Phase.Opening:
                    var p = ListMotion.Out(i, elapsed, r.Bounce, ListMotion.StaggerMs);
                    var (outBox, outScale) = ListMotion.Box(_ball, item.Slot, p);
                    item.Place(outBox, p, outScale);
                    break;
                case Phase.Closing:
                    var back = ListMotion.Back(t);
                    var (backBox, backScale) = ListMotion.Box(_ball, item.Slot, back);
                    item.Place(backBox, back, backScale);
                    break;
                case Phase.Shifting:
                    if (item.Leaving)
                    {
                        var (leaveBox, leaveScale, shown) = ListMotion.Leave(item.From, t);
                        item.Place(leaveBox, 1, leaveScale);
                        item.Box.Opacity = shown;
                    }
                    else
                    {
                        item.Place(SwapMotion.Between(item.From, item.Slot, SwapMotion.Move(t, r.Bounce)), 1);
                    }

                    break;
                case Phase.Taking:
                    // the tapped one stays until the lifted card is surely drawn over it (a frame or two), then the card is it
                    item.Box.Opacity = item.Key == _taken
                        ? (elapsed < ListMotion.Handover ? 1 : 0)
                        : 1 - LiftMotion.Window(t, 0, ListMotion.OthersOut);
                    break;
                default:
                    item.Place(item.Slot, 1);
                    break;
            }
        }
    }

    /// <summary>One pill of the list: the pill's surface, a <see cref="PillView"/> drawing the card on it, its border colour over all.</summary>
    private sealed class Item
    {
        private readonly SmoothBorder _back;
        private readonly SmoothBorder _ring = new() { EdgeWidth = 2, IsHitTestVisible = false };
        private readonly SolidColorBrush _ringBrush = new();
        private readonly Canvas _inside = new() { IsHitTestVisible = false };
        private readonly RectangleGeometry _clip = new();
        private readonly ScaleTransform _zoom = new();

        public Item(string key, MotionBox slot, Color pill, Color edge, double width, double height, IPillHost host)
        {
            Key = key;
            Slot = slot;
            _back = new SmoothBorder { Fill = new SolidColorBrush(pill), Edge = new SolidColorBrush(edge), EdgeWidth = 1, IsHitTestVisible = false };
            Pill = new PillView { Host = host, Width = width, Height = height, IsHitTestVisible = false };
            _ring.Edge = _ringBrush;
            _ring.Visibility = Visibility.Collapsed;
            _inside.Children.Add(Pill);
            _inside.Clip = _clip;
            _inside.RenderTransform = _zoom;
            Box.Children.Add(_back);
            Box.Children.Add(_inside);
            Box.Children.Add(_ring);
            Box.Children.Add(Button);
        }

        /// <summary>
        /// Over the card's button zone (the pill draws the button but takes no input): from the text's right edge to the pill's end,
        /// its whole height, as on the big pill. <paramref name="tip"/> = its hint.
        /// </summary>
        public void ShowButton(string? tip)
        {
            var zone = Pill.CloseZone;
            var shown = !Leaving && zone.Visibility == Visibility.Visible;
            Button.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (shown)
            {
                Button.Width = zone.Width;
            }
            else
            {
                Pill.LightButton(false);
            }

            if (tip != _tip)
            {
                _tip = tip;
                ToolTipService.SetToolTip(Button, tip);
            }
        }

        private string? _tip;

        /// <summary>Takes the taps on the card's button.</summary>
        public Border Button { get; } = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed,
        };

        /// <summary>Its card went: it shrinks away (<see cref="ListMotion.Leave"/>).</summary>
        public bool Leaving { get; set; }

        /// <summary>Where it was when the list began to shift.</summary>
        public MotionBox From { get; set; }

        public string Key { get; }

        /// <summary>Draws the card (the bar renders it).</summary>
        public PillView Pill { get; }

        /// <summary>The card's border colour, or none.</summary>
        public void SetGlow(Color? glow)
        {
            _ring.Visibility = glow is null ? Visibility.Collapsed : Visibility.Visible;
            _ringBrush.Color = glow ?? Color.FromArgb(0, 0, 0, 0);
        }

        public MotionBox Slot { get; set; }

        /// <summary>Takes the taps (a transparent background so the whole pill does).</summary>
        public Grid Box { get; } = new() { Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };

        public double ContentOpacity => _inside.Opacity;

        /// <summary>Draws the pill as <paramref name="box"/>, at that box's share of its way out (shown / content by <see cref="ListMotion.Shows"/>).</summary>
        /// <param name="scale">The card drawn this much bigger: past its slot it swells as a whole (<see cref="SwapMotion.Settle"/>).</param>
        public void Place(MotionBox box, double share, double scale = 1)
        {
            var (shown, content) = ListMotion.Shows(share);
            var w = Math.Max(0, box.Width);
            var h = Math.Max(0, box.Height);
            var radius = Math.Clamp(box.Radius, 0, Math.Min(w, h) / 2);
            Canvas.SetLeft(Box, box.X);
            Canvas.SetTop(Box, box.Y);
            Box.Width = w;
            Box.Height = h;
            _back.Radius = radius;
            _back.SetSize(w, h);
            _ring.Radius = radius;
            _ring.SetSize(w, h);

            _zoom.ScaleX = _zoom.ScaleY = scale;
            _clip.Rect = new Windows.Foundation.Rect(0, 0, w / scale, h / scale);   // in the content's own (unscaled) units
            Box.Opacity = shown;
            _inside.Opacity = content;
        }
    }
}
