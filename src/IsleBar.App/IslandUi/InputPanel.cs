using System.Numerics;
using IsleBar.App.Interop;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static IsleBar.App.Interop.NativeMethods;

namespace IsleBar.App.IslandUi;

/// <summary>What the panel needs to know when it opens. Rectangles in screen pixels.</summary>
internal sealed record PanelRequest(
    ScreenRect Circle,
    ScreenRect Pills,
    int ScreenLeft,
    int ScreenRight,
    int TaskbarTop,
    bool Dark,
    bool Glass,
    bool Bounce,
    double ListHeight);

/// <summary>
/// The input panel (TODO 7.9 H, user 10-09): grows out of the small pill's circle into a 361-wide sheet over the taskbar — the list
/// on top (file results / recent chats), the input box at the bottom — in the lifted card's glass and time, and shrinks back into
/// the circle. Unlike the card and the ⋯ list it <b>takes the keyboard</b>: a plain foreground window, so typing (and the IME) no
/// longer needs the bar to leave the taskbar. Closes on Esc, a click elsewhere (it loses the foreground), or the circle again.
/// </summary>
internal sealed class InputPanel : Window
{
    private readonly IntPtr _hwnd;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private readonly Grid _root = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly SmoothBorder _frame = new() { EdgeWidth = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly Grid _content = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Width = PanelMotion.Width };
    private readonly RectangleGeometry _contentClip = new();
    private readonly Grid _field = new() { ColumnSpacing = 4 };
    private readonly SmoothBorder _fieldBack = new() { Radius = 4, EdgeWidth = 1, IsHitTestVisible = false };
    private readonly Button _mode = new() { Width = 28, Height = 28, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Bottom, IsTabStop = false };
    private readonly SolidColorBrush _fill = new();
    private readonly SolidColorBrush _edge = new();
    private readonly System.Diagnostics.Stopwatch _clock = new();

    private PanelRequest? _request;
    private ScreenRect _window;
    private MotionBox _circleBox;
    private MotionBox _panelBox;
    private bool _closing;
    private bool _animating;
    private bool _live;
    private bool _solid;
    private double _pitch = 18.5;
    private ScrollViewer? _scroller;
    private int _lines = 1;
    private DateTimeOffset _openedAt;
    private readonly OutsideClick _outside;
    private ScreenRect _panelOnScreen;
    private readonly Canvas _caretLayer = new() { IsHitTestVisible = false };
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _caret = new() { Width = 1, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly SolidColorBrush _caretInk = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer _blink = null!;

    private static bool Inside(ScreenRect r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    /// <summary>The list area (H1b puts the file list here).</summary>
    public Grid List { get; } = new();

    /// <summary>The input box. Its text stays while the panel is down (the draft).</summary>
    public TextBox Box { get; } = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        BorderThickness = new Thickness(0),
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        Padding = new Thickness(0, 4, 0, 4),
        MinHeight = 28,   // one line: as tall as the mode button beside it, so their middles line up
        VerticalAlignment = VerticalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>The mode button was clicked (or Tab): the bar switches between asking AI and searching files.</summary>
    public Action? ModeClicked { get; set; }

    /// <summary>The panel starts going down: the bar brightens the big pill again.</summary>
    public Action? Closing { get; set; }

    /// <summary>The panel has gone (after its animation).</summary>
    public Action? Gone { get; set; }

    /// <summary>A key the box doesn't use itself (Enter, ↑↓, Esc…), before the box sees it. Return true when handled.</summary>
    public Func<Windows.System.VirtualKey, bool>? Key { get; set; }

    /// <summary>When it last started going down — the click on the circle that took the foreground away mustn't reopen it.</summary>
    public DateTimeOffset ClosedAt { get; private set; } = DateTimeOffset.MinValue;

    public bool IsOpen => _request is not null && !_closing;

    public InputPanel()
    {
        _hwnd = SeeThrough.Make(this, "panel", typing: true);
        _frame.Fill = _fill;
        _frame.Edge = _edge;

        // inside: the list, then the input box (the mode button on its left, at the bottom line)
        _content.Padding = new Thickness(PanelMotion.Pad);
        _content.RowSpacing = 4;
        _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.Children.Add(List);
        _field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _field.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _field.Padding = new Thickness(4, 4, 8, 4);
        _field.Children.Add(_fieldBack);
        Grid.SetColumnSpan(_fieldBack, 2);
        _mode.Margin = new Thickness(0, 0, 0, 0);
        _field.Children.Add(_mode);
        Grid.SetColumn(Box, 1);
        _field.Children.Add(Box);
        _caret.Fill = _caretInk;
        _caretLayer.Children.Add(_caret);
        Grid.SetColumnSpan(_caretLayer, 2);
        _field.Children.Add(_caretLayer);   // over the box: the panel's own caret (ShowCaret)
        _blink = _ui.CreateTimer();
        _blink.Interval = TimeSpan.FromMilliseconds(Math.Clamp(GetCaretBlinkTime(), 200u, 2000u));   // Windows' own rate (530 by default)
        _blink.IsRepeating = true;
        _blink.Tick += (_, _) => _caret.Visibility = _caret.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        Box.SelectionChanged += (_, _) => ShowCaret();
        Box.GotFocus += (_, _) => ShowCaret();
        Box.LostFocus += (_, _) => ShowCaret();
        Box.SizeChanged += (_, _) => ShowCaret();
        Box.Loaded += (_, _) =>
        {
            if (_scroller is null && FindScroller(Box) is { } sv)
            {
                _scroller = sv;
                sv.ViewChanged += (_, _) => ShowCaret();
            }
        };
        Box.TextCompositionChanged += (_, _) => ShowCaret();
        Grid.SetRow(_field, 1);
        _content.Children.Add(_field);
        _content.Clip = _contentClip;

        _root.Children.Add(_frame);
        _root.Children.Add(_content);
        Content = _root;

        // A press anywhere but the panel and the circle puts it away — watched system-wide like the lifted card's, since a panel
        // that didn't get the foreground never hears "deactivated" (user 10-09: a click outside sometimes did nothing)
        _outside = new OutsideClick((x, y) => _ui.TryEnqueue(() =>
        {
            if (IsOpen && _request is { } r && !Inside(_panelOnScreen, x, y) && !Inside(r.Circle, x, y))
            {
                TaskbarHost.Log("panel: press outside");
                Collapse();
            }
        }));
        Closed += (_, _) => _outside.Disarm();

        // the box draws nothing of its own (the field behind it is the look): no focus underline, no hover / focus fill
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
                 })
        {
            Box.Resources[key] = clear;
        }

        _mode.Background = clear;
        _mode.BorderThickness = new Thickness(0);
        _mode.Click += (_, _) => { ModeClicked?.Invoke(); Box.Focus(FocusState.Programmatic); };
        Box.TextChanged += (_, _) => { Regrow(); ShowCaret(); };
        Box.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnKey), handledEventsToo: true);
        // IME diagnostics (H1a): does a Chinese IME compose in this window? (2.3: letters only, sometimes, in the old taskbar box)
        Box.TextCompositionStarted += (_, _) => TaskbarHost.Log("panel: IME composing");
        Box.TextCompositionEnded += (_, _) => TaskbarHost.Log($"panel: IME done, text {Box.Text.Length} chars");
        Activated += (_, e) =>
        {
            TaskbarHost.Log($"panel: {e.WindowActivationState}, foreground {(GetForegroundWindow() == _hwnd ? "ours" : "elsewhere")}");
            if (e.WindowActivationState != WindowActivationState.Deactivated && IsOpen)
            {
                Box.Focus(FocusState.Programmatic);   // the caret: focus given before the window was active showed none (user 10-09)
            }

            if (e.WindowActivationState == WindowActivationState.Deactivated && IsOpen)
            {
                // A click elsewhere took the foreground → down. But the circle's own click sends one "deactivated" right after the
                // panel came up, with the foreground still ours (log 10-09: it shrank back half-way) — so look again a moment later.
                var timer = _ui.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(150);
                timer.IsRepeating = false;
                timer.Tick += (_, _) =>
                {
                    if (!IsOpen)
                    {
                        return;
                    }

                    if (GetForegroundWindow() == _hwnd)
                    {
                        // the keyboard went to another of our windows (the bar's, in the taskbar) — XAML then hides the caret
                        // though typing still arrives (user 10-09): give the keyboard back to this window
                        TaskbarHost.Log("panel: deactivated but still in front — takes the keyboard back");
                        SetFocus(_hwnd);
                        Box.Focus(FocusState.Programmatic);
                        return;
                    }

                    if (DateTimeOffset.UtcNow - _openedAt < TimeSpan.FromMilliseconds(600))
                    {
                        // just opened: the click that opened it handing the foreground to the taskbar, not a click elsewhere
                        TaskbarHost.Log("panel: lost the foreground right after opening — takes it back");
                        ForegroundHelper.Bring(_hwnd);
                        SetFocus(_hwnd);
                        Box.Focus(FocusState.Programmatic);
                        return;
                    }

                    Collapse();
                };
                timer.Start();
            }
        };
    }

    /// <summary>The mode button's look: a glyph (search) or an element (an agent's logo).</summary>
    public void SetMode(object look, string placeholder, string tip)
    {
        _mode.Content = look;
        Box.PlaceholderText = placeholder;
        ToolTipService.SetToolTip(_mode, tip);
    }

    public void Open(PanelRequest request)
    {
        _request = request;
        _closing = false;
        _openedAt = DateTimeOffset.UtcNow;
        var s = DpiSetup.Scale;
        _root.RequestedTheme = request.Dark ? ElementTheme.Dark : ElementTheme.Light;
        _fill.Color = request.Glass ? Color.FromArgb(0, 0, 0, 0) : LiftWindow.CardColor(request.Dark);
        _edge.Color = LiftWindow.EdgeColor(request.Dark);
        _caretInk.Color = request.Dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0x1B, 0x1B, 0x1B);
        _fieldBack.Fill = new SolidColorBrush(request.Dark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(178, 255, 255, 255));
        _fieldBack.Edge = new SolidColorBrush(request.Dark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0));
        List.Height = request.ListHeight;
        _pitch = Pitch();
        Box.MaxHeight = PanelMotion.BoxHeight(PanelMotion.MaxLines, _pitch);   // 4 whole lines, then it scrolls
        _lines = Lines();

        var panel = PanelMotion.Place(request.Pills, request.ScreenLeft, request.ScreenRight, request.TaskbarTop, s, Height());
        _panelOnScreen = panel;
        _window = PanelMotion.Window(panel, request.Circle, s);
        MotionBox Local(ScreenRect r, double radius) => new((r.Left - _window.Left) / s, (r.Top - _window.Top) / s, r.Width / s, r.Height / s, radius);
        var c = request.Circle;
        var size = (int)Math.Round(PanelMotion.Circle * s);
        var circle = new ScreenRect(c.Left + ((c.Width - size) / 2), c.Top + ((c.Height - size) / 2), c.Left + ((c.Width + size) / 2), c.Top + ((c.Height + size) / 2));
        _circleBox = Local(circle, PanelMotion.Circle / 2);
        _panelBox = Local(panel, PanelMotion.Radius);
        _root.Width = _window.Width / s;
        _root.Height = _window.Height / s;
        _content.Margin = new Thickness(_panelBox.X, _panelBox.Y, 0, 0);
        _content.Height = _panelBox.Height;

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(_window.Left, _window.Top, _window.Width, _window.Height));
        _live = request.Glass && Frost.Allowed() && SystemBackdrop is TransparentBackdrop backdrop && backdrop.StartGlass(_window.Width, _window.Height,
            request.Dark ? Color.FromArgb(255, 0x26, 0x26, 0x26) : Color.FromArgb(255, 0xFC, 0xFC, 0xFC), request.Dark ? 0.3f : 0f, request.Dark ? 0.96f : 0.85f);
        // no glass: the solid colour in the same system layer, so the box's own (still) caret stays hidden there too (user 10-09);
        // a XAML fill only if even that fails
        _solid = !_live && SystemBackdrop is TransparentBackdrop plain && plain.StartSolid(_window.Width, _window.Height, LiftWindow.CardColor(request.Dark));
        _fill.Color = _live || _solid ? Color.FromArgb(0, 0, 0, 0) : LiftWindow.CardColor(request.Dark);

        Draw(0, 0);
        SetWindowRgn(_hwnd, IntPtr.Zero, redraw: true);   // the whole window while it grows; just the panel once it's up (Clip)
        AppWindow.Show(activateWindow: true);
        Activate();
        ForegroundHelper.Bring(_hwnd);
        SetFocus(_hwnd);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        Box.Focus(FocusState.Programmatic);
        Box.SelectionStart = Box.Text.Length;
        TaskbarHost.Log($"panel: open {panel.Left},{panel.Top} {panel.Width}x{panel.Height}, glass {_live}, foreground {(GetForegroundWindow() == _hwnd ? "ours" : "elsewhere")}");
        _outside.Arm();
        Start();
    }

    /// <summary>Shrinks back into the circle. Nothing happens when it's not open.</summary>
    public void Collapse()
    {
        if (_request is null || _closing)
        {
            return;
        }

        _closing = true;
        ClosedAt = DateTimeOffset.UtcNow;
        _outside.Disarm();
        ShowCaret();   // IsOpen is false now: the caret goes
        SetWindowRgn(_hwnd, IntPtr.Zero, redraw: true);   // the whole window again for the shrink into the circle
        Closing?.Invoke();
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Start();
    }

    // ---------------- the input box ----------------

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            Collapse();
        }
        else if (e.Key == Windows.System.VirtualKey.Tab)
        {
            e.Handled = true;
            ModeClicked?.Invoke();
        }
        else if (e.Key == Windows.System.VirtualKey.Enter && shift)
        {
            // Shift+Enter: a new line (the box's own)
        }
        else if (Key?.Invoke(e.Key) == true)
        {
            e.Handled = true;
        }
    }

    /// <summary>The box's lines of text now (wrapping included), measured at its width.</summary>
    private int Lines()
    {
        var probe = new TextBlock { Text = Box.Text.Length == 0 ? " " : Box.Text, FontSize = Box.FontSize, TextWrapping = TextWrapping.Wrap, FontFamily = Box.FontFamily };
        var width = PanelMotion.Width - (2 * PanelMotion.Pad) - 4 - 8 - 28 - 4 - 8;   // the field's paddings, the mode button, the box's own room
        probe.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        return Math.Max(1, (int)Math.Round(probe.DesiredSize.Height / _pitch));
    }

    private double Height() => (2 * PanelMotion.Pad) + (_request?.ListHeight ?? 0) + 4 + PanelMotion.FieldHeight(_lines, _pitch);

    /// <summary>The text got a line longer or shorter: the panel grows / shrinks upwards (up to 4 lines, then the box scrolls).</summary>
    private void Regrow()
    {
        if (!IsOpen || _animating)
        {
            return;
        }

        var lines = Lines();
        if (Math.Min(lines, PanelMotion.MaxLines) == Math.Min(_lines, PanelMotion.MaxLines))
        {
            _lines = lines;
            return;
        }

        _lines = lines;
        var s = DpiSetup.Scale;
        var r = _request!;
        var panel = PanelMotion.Place(r.Pills, r.ScreenLeft, r.ScreenRight, r.TaskbarTop, s, Height());
        if (panel.Top - (int)Math.Round(PanelMotion.Room * s) < _window.Top)
        {
            // taller than the window: grow it (its top moves; everything inside moves down by as much)
            var grow = _window.Top - (panel.Top - (int)Math.Round(PanelMotion.Room * s));
            _window = _window with { Top = _window.Top - grow };
            _circleBox = _circleBox with { Y = _circleBox.Y + (grow / s) };
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(_window.Left, _window.Top, _window.Width, _window.Height));
            _root.Height = _window.Height / s;
            if (_live && SystemBackdrop is TransparentBackdrop backdrop)
            {
                backdrop.StartGlass(_window.Width, _window.Height, r.Dark ? Color.FromArgb(255, 0x26, 0x26, 0x26) : Color.FromArgb(255, 0xFC, 0xFC, 0xFC), r.Dark ? 0.3f : 0f, r.Dark ? 0.96f : 0.85f);
            }
            else if (_solid && SystemBackdrop is TransparentBackdrop plain)
            {
                plain.StartSolid(_window.Width, _window.Height, LiftWindow.CardColor(r.Dark));
            }
        }

        _panelOnScreen = panel;
        _panelBox = new MotionBox((panel.Left - _window.Left) / s, (panel.Top - _window.Top) / s, panel.Width / s, panel.Height / s, PanelMotion.Radius);
        _content.Margin = new Thickness(_panelBox.X, _panelBox.Y, 0, 0);
        _content.Height = _panelBox.Height;
        Draw(1, 1);
        Clip();
    }

    /// <summary>
    /// Once it's up, only the panel takes clicks: the window also covers the pills (the room the growth needed), and its see-through
    /// part caught the press on the circle, which then couldn't put the panel away (user 10-09).
    /// </summary>
    private void Clip()
    {
        var p = _panelOnScreen;
        SetWindowRgn(_hwnd, CreateRectRgn(p.Left - _window.Left, p.Top - _window.Top, p.Right - _window.Left, p.Bottom - _window.Top), redraw: true);
    }

    // ---------------- the caret ----------------

    /// <summary>
    /// The box's own caret doesn't reach the screen in this see-through window — none at all over the glass, a still one without
    /// (user 10-09; a screenshot, which re-reads the window, did show it; drawing a frame every vsync didn't help). So the panel
    /// draws its own: a 1-wide line where the box's caret is, blinking at Windows' rate, solid again on every change.
    /// </summary>
    private void ShowCaret()
    {
        _blink.Stop();
        if (!IsOpen || _animating || Box.FocusState == FocusState.Unfocused || Box.SelectionLength > 0 || CaretRect() is not { } at)
        {
            _caret.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(_caret, Math.Round(at.X * DpiSetup.Scale) / DpiSetup.Scale);   // on whole pixels: a 1-wide line split over two looked grey
        Canvas.SetTop(_caret, at.Y);
        _caret.Height = at.Height;
        _caret.Visibility = Visibility.Visible;
        _blink.Start();
    }

    /// <summary>Where the caret goes, in the caret layer's coordinates; null when it's scrolled out of the box.</summary>
    private Windows.Foundation.Rect? CaretRect()
    {
        // The box reports a character's place from its text's top-left — before its padding and its scrolling — and a height of
        // about two lines (log 10-09: lines at 0 / 19 / 37 / 56 / 74, heights 36–38), so the line's height is the measured pitch.
        var text = Box.Text;
        var index = Math.Clamp(Box.SelectionStart, 0, text.Length);
        double x, y;
        if (text.Length == 0)
        {
            (x, y) = (0, (Box.ActualHeight - _pitch) / 2);   // empty: at the hint's start
        }
        else
        {
            if (index < text.Length)
            {
                var r = Box.GetRectFromCharacterIndex(index, trailingEdge: false);
                (x, y) = (r.X, r.Y);
            }
            else
            {
                var r = Box.GetRectFromCharacterIndex(text.Length - 1, trailingEdge: true);
                (x, y) = text[^1] is '\r' or '\n' ? (0, r.Y + _pitch) : (r.X, r.Y);   // after a new line: the next line's start
            }

            y += Box.Padding.Top - (_scroller?.VerticalOffset ?? 0);
        }

        if (y < Box.Padding.Top - 1 || y + _pitch > Box.ActualHeight - Box.Padding.Bottom + 1)
        {
            return null;   // scrolled out of the 4 lines
        }

        var at = Box.TransformToVisual(_caretLayer).TransformPoint(new Windows.Foundation.Point(x, y));
        return new Windows.Foundation.Rect(at.X, at.Y, 1, _pitch);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetCaretBlinkTime();

    /// <summary>How far apart the box's lines are, in its font and size (two lines' height less one's).</summary>
    private double Pitch()
    {
        double Measure(string text)
        {
            var probe = new TextBlock { Text = text, FontSize = Box.FontSize, FontFamily = Box.FontFamily };
            probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Height;
        }

        var pitch = Measure("国g\n国g") - Measure("国g");
        TaskbarHost.Log($"panel: line pitch {pitch:0.##}");
        return pitch is > 10 and < 40 ? pitch : 18.5;
    }

    /// <summary>The box's own scroller (its template's "ContentElement"): its offset moves the caret, its scrolling redraws it.</summary>
    private ScrollViewer? FindScroller(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv)
            {
                return sv;
            }

            if (FindScroller(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // ---------------- animation ----------------

    private void Start()
    {
        _clock.Restart();
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private TimeSpan Length() => _closing ? LiftMotion.Close : LiftMotion.OpenLength(_request?.Bounce == true);

    private void OnFrame(object? sender, object e)
    {
        var t = Math.Min(1, _clock.Elapsed.TotalMilliseconds / Length().TotalMilliseconds);
        var p = _closing ? LiftMotion.Closing(t) : LiftMotion.Opening(t, _request?.Bounce == true);
        Draw(p, LiftMotion.GlassAt(t, _closing));
        if (!_closing)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        if (t < 1)
        {
            return;
        }

        _animating = false;
        CompositionTarget.Rendering -= OnFrame;
        if (!_closing)
        {
            Clip();
            ShowCaret();
            TaskbarHost.Log($"panel: caret {(CaretRect() is { } c ? $"at {c.X:0.#},{c.Y:0.#} h {c.Height:0.#}" : "none")}, box {Box.ActualWidth:0}x{Box.ActualHeight:0}");
            TaskbarHost.Log($"panel: opened, foreground {(GetForegroundWindow() == _hwnd ? "ours" : "elsewhere")}, box focused {Box.FocusState}");
        }

        if (_closing)
        {
            if (SystemBackdrop is TransparentBackdrop backdrop)
            {
                backdrop.HideGlass();
            }

            AppWindow.Hide();
            AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
            _request = null;
            _closing = false;
            Gone?.Invoke();
        }
    }

    /// <summary>The shape at share <paramref name="p"/> from the circle to the panel; <paramref name="glass"/> = how much glass shows through.</summary>
    private void Draw(double p, double glass)
    {
        var box = PanelMotion.Between(_circleBox, _panelBox, p);
        _frame.Margin = new Thickness(box.X, box.Y, 0, 0);
        _frame.Width = box.Width;
        _frame.Height = box.Height;
        _frame.Radius = box.Radius;
        _contentClip.Rect = new Windows.Foundation.Rect(box.X - _panelBox.X, box.Y - _panelBox.Y, box.Width, box.Height);
        _content.Opacity = LiftMotion.Window(p, LiftMotion.ContentIn, 1);
        if (_live && _request is { } r && SystemBackdrop is TransparentBackdrop backdrop)
        {
            var s = (float)DpiSetup.Scale;
            var solid = LiftWindow.CardColor(r.Dark);
            backdrop.MoveGlass(new Vector2((float)box.X * s, (float)box.Y * s), new Vector2((float)box.Width * s, (float)box.Height * s),
                (float)box.Radius * s, Matrix4x4.Identity, 1, _window.Height, Color.FromArgb((byte)Math.Round(255 * (1 - glass)), solid.R, solid.G, solid.B));
        }
        else if (_solid && SystemBackdrop is TransparentBackdrop plain)
        {
            var s = (float)DpiSetup.Scale;
            plain.MoveSolid(new Vector2((float)box.X * s, (float)box.Y * s), new Vector2((float)box.Width * s, (float)box.Height * s), (float)box.Radius * s);
        }
    }
}
