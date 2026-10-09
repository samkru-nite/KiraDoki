using System.Diagnostics;
using IsleBar.App.Interop;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static IsleBar.App.Interop.NativeMethods;

namespace IsleBar.App.IslandUi;

/// <summary>What the bar hands over to open the ⋯ list (screen pixels, colours of the moment). Key 0 is the big pill's card.</summary>
/// <param name="Host">What each list pill draws with (the bar).</param>
/// <param name="Prepare">Readies a new list pill (its colours as at rest).</param>
/// <param name="Render">Draws a card on its list pill — at opening and on every tick while the list is up.</param>
/// <param name="Glow">A card's border colour (null = none).</param>
internal sealed record ListRequest(
    IReadOnlyList<string> Keys,
    IPillHost Host,
    Action<PillView> Prepare,
    Action<string, PillView> Render,
    Func<string, Color?> Glow,
    ScreenRect Pill,
    ScreenRect Dots,
    double PillRadius,
    Color PillColor,
    Color LabelColor,
    bool Dark,
    int ScreenLeft,
    int ScreenRight,
    int TaskbarTop,
    bool Bounce,
    string OnBigLabel);

/// <summary>
/// The ⋯ list (TODO 7.9 F3, user 10-09): every card drawn by a pill of its own (<see cref="PillView"/>, the big pill's very
/// drawing, live: timers tick, bars flow — plan B, 10-10), stacked over the big pill, each flying out of
/// the ⋯ circle and growing into its pill on the way (nearest first, a stagger apart, bowing up). A tap on one hands it to the bar,
/// which lifts its card out of that very pill (<see cref="LiftWindow"/>, with <see cref="LiftRequest.From"/>); the others fade.
/// Anything else closes it: they fly back into ⋯. See-through, never focused, topmost, like the lifted card. Numbers: <see cref="ListMotion"/>.
/// </summary>
internal sealed class ListWindow : Window
{
    private readonly Canvas _root = new();
    private readonly Stopwatch _clock = new();
    private readonly OutsideClick _outside;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private readonly IntPtr _hwnd;
    private readonly List<Item> _items = [];
    private readonly Grid _label = new() { IsHitTestVisible = false };

    private ListRequest? _request;
    private ScreenRect _window;
    private MotionBox _dots;
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
        Closing,
        Taking,
    }

    /// <summary>A pill was tapped: its card and where the pill is on screen now.</summary>
    public Action<string, ScreenRect>? Tapped { get; set; }

    /// <summary>The list starts going back into ⋯ (however it was closed): the bar brightens its pills again.</summary>
    public Action? Closing { get; set; }

    /// <summary>A press there is the bar's own (it closes the list itself) — the list doesn't close on it.</summary>
    public Func<int, int, bool>? KeepsPress { get; set; }

    /// <summary>Up and not on its way out.</summary>
    public bool IsOpen => _phase is Phase.Opening or Phase.Open;

    /// <summary>The mouse came onto a list pill (its card, its pill): a click nearly always follows.</summary>
    public Action<string, PillView>? Hovered { get; set; }

    /// <summary>Each tick while up: every pill draws its card as it is now, and its border.</summary>
    public void Redraw()
    {
        if (_request is not { } r)
        {
            return;
        }

        foreach (var item in _items)
        {
            r.Render(item.Key, item.Pill);
            item.SetGlow(r.Glow(item.Key));
        }
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

        // the window: the stack, the ⋯ circle and the big pill, with room for the bounce and the arc
        var room = PillGeometry.Px(LiftMotion.Room, s);
        var pillH = request.Pill.Height;
        var step = PillGeometry.Px(PillGeometry.Height + ListMotion.Gap, s);
        var top = request.TaskbarTop - PillGeometry.Px(ListMotion.Gap, s) - pillH - ((n - 1) * step);
        _window = new ScreenRect(
            Math.Min(request.Pill.Left, request.Dots.Left) - room,
            top - room - PillGeometry.Px(ListMotion.Arc, s),
            Math.Max(request.Pill.Right, request.Dots.Right) + room,
            Math.Max(request.Pill.Bottom, request.Dots.Bottom));

        MotionBox Local(ScreenRect r, double radius) => new((r.Left - _window.Left) / s, (r.Top - _window.Top) / s, r.Width / s, r.Height / s, radius);
        var pill = Local(request.Pill, request.PillRadius);
        _dots = Local(request.Dots, request.Dots.Width / s / 2);
        var taskbarTop = (request.TaskbarTop - _window.Top) / s;

        _root.Children.Clear();
        _items.Clear();
        var edge = PillView.EdgeFill();   // they're pills: the pills' edge (TODO 丙④)
        for (var i = 0; i < n; i++)
        {
            var item = new Item(request.Keys[i], ListMotion.Slot(i, pill, taskbarTop), request.PillColor, edge, pill.Width, pill.Height, request.Host);
            request.Prepare(item.Pill);
            item.Box.Tapped += (_, _) => OnTapped(item);
            item.Box.PointerEntered += (_, _) => Hovered?.Invoke(item.Key, item.Pill);
            _items.Add(item);
            _root.Children.Add(item.Box);
        }

        // drawn twice, as the big pill is after a change: the second time the text fits its column's real width (a close button
        // narrows it), measured by the layout in between
        Redraw();
        _root.UpdateLayout();
        Redraw();

        BuildLabel(request);
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

    /// <summary>Back into ⋯ (nothing happens when it's not up).</summary>
    public void PutAway()
    {
        if (!IsOpen)
        {
            return;
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

    private void OnPress(int x, int y)
    {
        if (_phase != Phase.Open && _phase != Phase.Opening)
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
        _ => ListMotion.Close,
    };

    private void OnFrame(object? sender, object e)
    {
        _meter?.Frame();
        var elapsed = _clock.Elapsed;
        Draw(elapsed);
        if (_phase != Phase.Taking)
        {
            SeeThrough.OnTop(_hwnd);   // a click on the taskbar lifts it over every topmost window — but never over the lifted card
        }
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
            // settled: only the pills take clicks — the bar under the window is the bar's again
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
                    item.Place(ListMotion.Box(_dots, item.Slot, p), p);
                    break;
                case Phase.Closing:
                    var back = ListMotion.Back(t);
                    item.Place(ListMotion.Box(_dots, item.Slot, back), back);
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

        // "on the pill" beside the lowest one, showing as it does
        if (_items.Count > 0)
        {
            var lowest = _items[0];
            Canvas.SetLeft(_label, lowest.Slot.X - 8 - _label.DesiredSize.Width);
            Canvas.SetTop(_label, lowest.Slot.Y + ((lowest.Slot.Height - _label.DesiredSize.Height) / 2));
            _label.Opacity = _phase == Phase.Taking
                ? Math.Min(_label.Opacity, 1 - LiftMotion.Window(t, 0, ListMotion.OthersOut))
                : lowest.Box.Opacity * lowest.ContentOpacity;
        }
    }

    private void BuildLabel(ListRequest r)
    {
        _label.Children.Clear();
        var chip = new SmoothBorder { Fill = new SolidColorBrush(r.PillColor), Radius = 4, IsHitTestVisible = false };
        var text = new TextBlock
        {
            Text = r.OnBigLabel,
            FontSize = 11,
            Foreground = new SolidColorBrush(r.LabelColor),
            Margin = new Thickness(6, 1, 6, 2),
            IsHitTestVisible = false,
        };
        _label.Children.Add(chip);
        _label.Children.Add(text);
        _label.Opacity = 0;
        _label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        _root.Children.Add(_label);
    }

    /// <summary>One pill of the list: the pill's surface, a <see cref="PillView"/> drawing the card on it, its border colour over all.</summary>
    private sealed class Item
    {
        private readonly SmoothBorder _back;
        private readonly SmoothBorder _ring = new() { EdgeWidth = 2, IsHitTestVisible = false };
        private readonly SolidColorBrush _ringBrush = new();
        private readonly Canvas _inside = new() { IsHitTestVisible = false };
        private readonly RectangleGeometry _clip = new();

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
            Box.Children.Add(_back);
            Box.Children.Add(_inside);
            Box.Children.Add(_ring);
        }

        public string Key { get; }

        /// <summary>Draws the card (the bar renders it).</summary>
        public PillView Pill { get; }

        /// <summary>The card's border colour, or none.</summary>
        public void SetGlow(Color? glow)
        {
            _ring.Visibility = glow is null ? Visibility.Collapsed : Visibility.Visible;
            _ringBrush.Color = glow ?? Color.FromArgb(0, 0, 0, 0);
        }

        public MotionBox Slot { get; }

        /// <summary>Takes the taps (a transparent background so the whole pill does).</summary>
        public Grid Box { get; } = new() { Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };

        public double ContentOpacity => _inside.Opacity;

        /// <summary>Draws the pill as <paramref name="box"/>, at that box's share of its way out (shown / content by <see cref="ListMotion.Shows"/>).</summary>
        public void Place(MotionBox box, double share)
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

            _clip.Rect = new Windows.Foundation.Rect(0, 0, w, h);
            Box.Opacity = shown;
            _inside.Opacity = content;
        }
    }
}
