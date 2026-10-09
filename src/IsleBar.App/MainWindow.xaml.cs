using IsleBar.App.Engine;
using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.App.Media;
using IsleBar.App.Supervisor;
using IsleBar.App.Tracking;
using IsleBar.Core.Configuration;
using IsleBar.Core.Island;
using IsleBar.Core.Launch;
using IsleBar.Core.Localization;
using IsleBar.Core.Ui;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace IsleBar.App;

/// <summary>
/// The search bar itself. Almost all logic lives in <c>IsleBar.Core</c> (so it can be tested); this
/// class only attaches to Windows and draws.
///
/// <b>This file has never been built. Needs phase-0 verification on a PC — docs/PHASE0_CHECKLIST.md</b>
///
/// Event handlers are wired <b>only in one place</b>: <see cref="WireEvents"/>.
/// On 09-29 two bugs came from wiring the same event twice, with the second overriding the first.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Big pill 220x32 (TODO 7.9 D final sizes); the window around both pills is laid out by <see cref="PillGeometry"/>.</summary>
    private const int BaseWidth = 220;
    private const int BaseHeight = 32;

    private static readonly TimeSpan IslandPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Where the pills sit in the window and how big it is (pixels). Replaced whole, so the tracker thread may read it.</summary>
    private PillsLayout _layout = PillGeometry.Compute(DpiSetup.Scale, big: false, smallLeft: false, PillGeometry.SmallWidth(0), round: true);

    private int _width => _layout.Width;
    private int _height => _layout.Height;

    /// <summary>The new design's card rules (TODO 7.2/7.3): which card the big pill shows.</summary>
    private readonly CardStack _cards = new();

    /// <summary>The card on the big pill (its type decides the border, the icon colour and the close zone).</summary>
    private Card? _shownCard;

    private readonly ConfigStore _store = new(AppPaths.SettingsFile);
    private readonly ActivityFeed _feed;
    private readonly DispatcherQueue _ui;

    private readonly IntPtr _hwnd;
    private readonly TaskbarHost _taskbar;
    private readonly TaskbarTracker _tracker;
    private readonly IslandAnimator _animator;
    private readonly Watchdog _watchdog;

    private IsleBarSettings _settings;
    private LanguageStrings _text;
    private string _language;
    private ThemeColors _theme;
    // not "Hidden": a first placement of Hidden must still be applied (it compared equal and the window stayed where WinUI put it — review 10-03).
    // The window's top-left (the pills' spot moved up by ArcRoom), not the pills'.
    private BarPlacement _placement = new(false, int.MinValue, int.MinValue);
    private int _placedWidth;

    /// <summary>While a file is dragged over the pill (blue border).</summary>
    private bool _dropHover;
    private string? _dropNames;   // names of the files being dragged over the pill, for the drop hint
    private Options.SettingsWindow? _settingsWindow;
    /// <summary>Claude's logo in the plain icon colour (null = not on this PC); the Codex one is <c>CodexGlyph.Source</c>.</summary>
    private ImageSource? _claudeGlyph;
    private IslandSnapshot _snapshot = IslandSnapshot.Empty;
    private DispatcherQueueTimer? _islandTimer;

    public MainWindow()
    {
        InitializeComponent();
        Big.Host = this;

        _ui = DispatcherQueue.GetForCurrentThread();
        _hwnd = WindowNative.GetWindowHandle(this);
        _taskbar = new TaskbarHost(_hwnd);

        _settings = _store.Load();
        _language = LanguageResolver.Resolve(_settings.Lang, CurrentUiLanguageId());
        _text = LanguageCatalog.For(_language);
        _theme = ThemeColors.FromSystem();

        _tracker = new TaskbarTracker(() => _layout.Width, DpiSetup.Px(BaseHeight), () => (BarSlot.Parse(_settings.Position), DpiSetup.Px(_settings.ManualOffset)));
        _feed = new ActivityFeed(() => _settings, () => _text);
        _animator = new IslandAnimator([LeadSlot, IslandContent], IslandContent, GlowRing, PillBody);
        _watchdog = new Watchdog(_taskbar, () => ThemeColors.FromSystem().Light, RequestExit,
            _ => _ui.TryEnqueue(Retheme));

        ApplyFonts();
        ApplyTheme();
        LoadAgentMarks();
        WireEvents();

        // remove the title bar and border — otherwise only a "WinUI Desktop" title bar shows inside the 64px (PC 09-29)
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter frame)
        {
            frame.SetBorderAndTitleBar(false, false);
            frame.IsResizable = false;
            frame.IsMaximizable = false;
            frame.IsMinimizable = false;
        }

        PillRegion.DisableSystemFrame(_taskbar.OuterWindow);
        _taskbar.Attach();
        ApplyRegion();

        // Move the pill the instant the tracker sees the search box move (taskbar reflow), rather than waiting for the next
        // 250ms island tick — this is what makes it follow the icons naturally instead of lagging behind (user 10-01).
        _tracker.OnChanged = () => _ui.TryEnqueue(FollowTaskbar);
        _tracker.Start();
        _feed.Start();
        _feed.OfferToConnectAgents();

        _watchdog.Start();
        StartIslandPolling();
    }

    // ---------------- Event wiring (one place only) ----------------

    private void WireEvents()
    {
        HookFileDrop();
        PressFeedback.Attach(BallCell);
        PressFeedback.Enabled = () => _settings.Bounce;
        PressFeedback.Attach(PillBody, PressFeedback.PillPressed, PressFeedback.PillDamping);   // the big pill too: half on press, the rest + the card on release (user 10-08)
        Root.Loaded += (_, _) => MeasureInkAsync();   // where the card font's glyphs sit (TODO 7.9 D: centre the ink, not the line box)   // pressing makes it sink in and spring back (09-30 design M5)
        // handledEventsToo: a press a child element handled still counts
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
            {
                return;   // right click is the menu (RightTapped)
            }

            var source = e.OriginalSource as DependencyObject;
            if (_list?.IsOpen == true && (IsInside(source, SmallPill) || IsInside(source, PillBody)))
            {
                CloseList();   // with the ⋯ list up, a press on the pills only puts it away (TODO 7.9 F3)
                return;
            }

            if (IsInside(source, SmallPill) && !IsInside(source, BallBox))
            {
                _cellPress = CellTagAt(source) is { } tag && tag != DotsTag ? tag : null;   // swaps on release (below)
                _dotsPress = CellTagAt(source) == DotsTag;   // opens the list on release

                return;   // the line and the margins do nothing
            }

            if (IsInside(source, BallBox))
            {
                // the circle = the tool menu, same as a right click (10-10: the input panel is gone) — on release, like the rest
                _ballPress = true;
                return;
            }

            if (IsInside(source, CloseZone) && !BigDimmed)
            {
                CloseCard(_shownCard);   // an inform card is closed only here or from its menu (TODO 7.2)
                return;
            }

            // the rest of the big pill lifts the card, or puts it back (TODO 7.9 F) — on release: a window that pops up under the
            // pointer between press and release takes the release, and the pill then waited for it forever and ignored every
            // later press, right clicks included (10-08)
            _liftPress = IsInside(source, PillBody);
            if (_liftPress && _lift?.OpenKey is not null)
            {
                PressFeedback.Hold(PillBody);   // this click puts the card back: the pill stays down until it lands (user 10-08)
            }
        }), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.GetCurrentPoint(Root).Properties.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased)
            {
                return;
            }

            if (_cellPress is { } pressed)
            {
                _cellPress = null;
                if (CellTagAt(e.OriginalSource as DependencyObject) == pressed)
                {
                    OnTaskCellClicked(pressed);   // that card and the big pill swap places (TODO 7.3, 10-07) - on release, like the big pill
                }
            }

            if (_ballPress)
            {
                _ballPress = false;
                if (IsInside(e.OriginalSource as DependencyObject, BallBox))
                {
                    ShowToolMenu();
                }
            }

            if (_dotsPress)
            {
                _dotsPress = false;
                if (CellTagAt(e.OriginalSource as DependencyObject) == DotsTag)
                {
                    ToggleList();
                }
            }

            if (_liftPress)
            {
                _liftPress = false;
                if (IsInside(e.OriginalSource as DependencyObject, PillBody))
                {
                    ToggleLift();
                }
                else
                {
                    PressFeedback.LetGo(PillBody);   // let go off the pill: nothing happens, the pill just comes back up
                }
            }
        }), handledEventsToo: true);
        // Right click = the menu of what's under it (close a card, the tools)
        Root.AddHandler(UIElement.RightTappedEvent, new RightTappedEventHandler((_, e) =>
        {
            e.Handled = true;
            ShowZoneMenu(e.OriginalSource as DependencyObject);
        }), handledEventsToo: true);
        Root.PointerEntered += (_, _) => SetHover(true);
        Root.PointerExited += (_, _) => SetHover(false);
        CloseZone.PointerEntered += (_, _) =>
        {
            _closeHover = !BigDimmed;
            PaintFill();
        };
        CloseZone.PointerExited += (_, _) =>
        {
            _closeHover = false;
            PaintFill();
        };
        PillBody.PointerEntered += (_, _) =>
        {
            SetBigHover(true);
            TakePillShotAhead();   // for the lift (TODO 7.9 F): ready before the click comes
        };
        PillBody.PointerExited += (_, _) => SetBigHover(false);
        BallCell.PointerEntered += (_, _) => BallHover.Opacity = 1;
        BallCell.PointerExited += (_, _) => BallHover.Opacity = 0;

        Closed += (_, _) => Cleanup();
    }

    private static bool IsInside(DependencyObject? element, DependencyObject container)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node == container)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Island ----------------

    private void StartIslandPolling()
    {
        _islandTimer = _ui.CreateTimer();
        _islandTimer.Interval = IslandPollInterval;
        _islandTimer.Tick += (_, _) =>
        {
          try
          {
            RetryAttachIfNeeded();
            FollowTaskbar();
            RefreshBesideColor();
            var items = KeepLiftedCard(_feed.Collect(DateTimeOffset.UtcNow));
            EndStaleHover();

            _cards.Update(items, DateTimeOffset.UtcNow);
            LogDebutNotes();
            _shownCard = _cards.Shown;
            KeepLift();
            KeepList();
            var next =_shownCard is { } shown ? new IslandSnapshot(shown.State, _cards.Cards.Count - 1) : IslandSnapshot.Empty;
            _snapshot = next;
            LoadAlbumArt();
            if (_flying)
            {
                return;   // a transition is flying — it draws the latest state when it lands
            }

            var change = _animator.Classify(next);   // the animator decides whether it's a new item (kind/source/track)
            ComputeCells();
            if (SceneChanged())
            {
                // a card went up or down, an icon came or went: everything moves from where it was to where it goes (user 10-07)
                _ = Transition(hoverKey: null);
                return;
            }

            RenderTaskCells();
            LayoutPills();

            // Music is the long-running island and its only per-tick change is the progress bar, which now advances ~once a second
            // (the media watcher is event-driven). So skip its re-render when nothing visible changed — the title marquee keeps
            // scrolling on the compositor regardless. Timers still draw every tick for a smooth ring; a quick sig catches the rest.
            var musicPrimary = change is IslandAnimator.Change.None && !_hovering
                ? _snapshot.Primary is { Kind: ActivityKind.Music } m ? m : null
                : null;
            if (musicPrimary is not null && MusicRenderSig(musicPrimary) == _lastMusicSig)
            {
                // nothing visible changed for the music island this tick — don't re-render
            }
            else
            {
                _lastMusicSig = musicPrimary is null ? null : MusicRenderSig(musicPrimary);
                RenderIsland();
            }

            if (change is IslandAnimator.Change.Swapped)
            {
                _animator.Enter();   // the same card, a new item in it (next song): a small dip
            }
          }
          catch
          {
            // Never let one bad tick kill the island timer — a thrown tick stops the DispatcherQueueTimer for good and the
            // whole pill/card freezes on the previous frame (the Translation.Y bug did exactly this, 09-30). Swallow and retry next tick.
          }
        };
        _islandTimer.Start();
    }

    /// <summary>
    /// Follows the spot computed in the background. <b>No UIA calls here</b> — it only reads one value (so it never waits on Explorer).
    /// </summary>
    private void FollowTaskbar()
    {
        if (_flying)
        {
            return;   // a transition has the window spanning the old and the new layout; it places it when it lands
        }

        var spot = _tracker.PlaceNow(_width);
        var placement = spot.Visible ? spot with { Y = spot.Y - DpiSetup.Px(PillGeometry.ArcRoom) } : spot;
        if (placement == _placement && _width == _placedWidth)
        {
            return;
        }

        _placement = placement;
        _placedWidth = _width;
        Interop.TaskbarHost.Log($"follow: visible={placement.Visible} {placement.X},{placement.Y} size {_width}x{_height}");
        if (!placement.Visible)
        {
            // don't use AppWindow.Show/Hide on a taskbar child window (PC 09-29: Show hung and the position was never set)
            Interop.NativeMethods.ShowWindow(_taskbar.OuterWindow, Interop.NativeMethods.SW_HIDE);
            return;
        }

        Interop.NativeMethods.ShowWindow(_taskbar.OuterWindow, Interop.NativeMethods.SW_SHOWNOACTIVATE);
        Root.Background = BrushOf(TaskbarColorBeside());   // when the position changes, re-read the adjacent taskbar color too
        _taskbar.PlaceAt(placement.X, placement.Y, _width, _height);
    }

    /// <summary>The big pill shows a card or a dragged file; otherwise only the small pill (TODO 7.1).</summary>
    private bool BigVisible => _dropHover || _snapshot.IsActive;

    // ---------------- Small pill: task icons (E3) ----------------

    /// <summary>The order of the task icons (kept tick to tick, swapped on a click).</summary>
    private readonly TaskSlots _slots = new();

    /// <summary>Cells on the small pill now: task icons, then whether ⋯ shows. Their keys, left to right in <see cref="_slots"/> order.</summary>
    private readonly List<Card> _cellCards = [];
    private bool _dotsOn;

    /// <summary>What the cells were last built from; rebuilt only when it changes (null = rebuild).</summary>
    private string? _cellsSig;

    /// <summary>Each cell's colour as last drawn: a pinned card's change of colour makes its icon jump (G).</summary>
    private readonly Dictionary<string, IconTone> _cellTones = new(StringComparer.Ordinal);

    /// <summary>Task cells + ⋯, for the window width.</summary>
    private int TaskCellCount => _cellCards.Count + (_dotsOn ? 1 : 0);

    /// <summary>
    /// Every card the big pill isn't showing gets an icon on the small pill (temporary ones excepted), as many as fit before the
    /// taskbar's app icons (8 from them); the rest wait behind ⋯, which shows whenever any card is behind the big pill (TODO 7.9 D).
    /// Frozen while a menu is open (the window doesn't change size under it).
    /// </summary>
    private void UpdateTaskCells()
    {
        if (ComputeCells())
        {
            RenderTaskCells();
            LayoutPills();
        }
    }

    /// <summary>Works out which cells should show (<see cref="_cellCards"/>, <see cref="_dotsOn"/>). False while frozen.</summary>
    private bool ComputeCells()
    {
        if (_menuOpen || _flying)
        {
            return false;
        }

        _slots.Update(_cards.Cards, _shownCard?.Key, _hovering);
        var cells = _slots.Cells;
        var behind = Math.Max(0, _cards.Cards.Count - (_cards.Shown is null ? 0 : 1));   // all tucked: none on the big pill
        var (icons, dots) = TaskSlots.Fit(cells.Count, behind, RoomForCells(cells.Count + (behind > 0 ? 1 : 0)));
        _cellCards.Clear();
        _cellCards.AddRange(cells.Take(icons));
        _dotsOn = dots;
        return true;
    }

    /// <summary>The most cells (⋯ included) that fit on the taskbar at the bar's position, up to <paramref name="wanted"/>.</summary>
    private int RoomForCells(int wanted)
    {
        if (_tracker.Layout is null)
        {
            return wanted;   // the taskbar isn't read yet: nothing to measure against
        }

        var big = BigVisible;
        for (var n = wanted; n > 0; n--)
        {
            var width = PillGeometry.Compute(DpiSetup.Scale, big, false, PillGeometry.SmallWidth(n), true).Width;
            if (_tracker.PlaceNow(width).Visible)
            {
                return n;
            }
        }

        return 0;
    }

    /// <summary>Builds the cells when what they show changed: icon or cover, colour, pause bars; ⋯ last (first when the pill sits left).</summary>
    private void RenderTaskCells()
    {
        var left = _settings.BallSide == "left";
        var now = DateTimeOffset.UtcNow;
        var icons = _cellCards.Select(c => (c.Key, Icon: TaskIcons.For(c, CoverOf(c) is not null, now), Card: c)).ToList();
        // a timer's hand turns on its own; it is set again only when the timer itself changes (paused, resumed, lengthened)
        var sig = string.Join("|", icons.Select(i => $"{i.Key}:{i.Icon}" + (i.Card.State.Kind == ActivityKind.Timer
                      ? $":{i.Card.State.Due}:{i.Card.State.Total}:{i.Card.State.Left}:{i.Card.State.T0}" : string.Empty)))
                  + $"|{_dotsOn}|{left}|{_theme.Icon}|{RealSearchBoxFill()}";
        SmallRule.Visibility = TaskCellCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (sig == _cellsSig)
        {
            return;
        }

        _cellsSig = sig;
        _renderedCells.Clear();
        _renderedCells.AddRange(_cellCards);
        TaskCells.Children.Clear();
        // nothing breathes — not AI, not mic / camera (user 10-08: only what waits for you may move; it is all or none)
        var jumps = new List<UIElement>();
        foreach (var (key, icon, card) in icons)
        {
            var cell = MakeCell(key, CellContent(card, icon, now));
            TaskCells.Children.Add(cell);
            // a pinned card has no debut: when its colour changes in its cell (working → done), the icon jumps once as it takes the
            // new colour, so the change isn't missed (user 10-09, G). A cell just come (or back from the big pill) has no "before".
            if (_cellTones.TryGetValue(key, out var was) && was != icon.Tone && _cards.IsTucked(key))
            {
                jumps.Add(cell);
                TaskbarHost.Log($"cell: {key} {was} → {icon.Tone}, jumps");
            }
        }

        _cellTones.Clear();
        foreach (var (key, icon, _) in icons)
        {
            _cellTones[key] = icon.Tone;
        }

        if (jumps.Count > 0)
        {
            // after layout: the press scales about the cell's centre, which needs its size
            _ui.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => jumps.ForEach(PressFeedback.Pulse));
        }

        var more = MakeCell(DotsTag, new Microsoft.UI.Xaml.Controls.FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16, Glyph = "", Foreground = BrushOf(_theme.Icon),
        });
        if (_dotsOn)
        {
            TaskCells.Children.Insert(left ? 0 : TaskCells.Children.Count, more);
        }

        ApplyCellDim();
    }

    /// <summary>What a card's cell draws: a timer's turning dial, else its icon (<see cref="IconContent"/>).</summary>
    private UIElement CellContent(Card card, TaskIcon icon, DateTimeOffset now)
        => TimerDial.Of(card.State, card.Type == CardType.Inform, now) is { } dial
            ? DialContent(dial, ToneColor(icon.Tone), icon.Paused)
            : IconContent(icon, CoverOf(card)?.Image);

    /// <summary>
    /// A timer's small icon (user 10-07): Segoe Fluent's stopwatch (E916) rebuilt in its own proportions — ring, top bar, side
    /// button — with a hand that turns (the font draws the hand into the glyph, it can't move on its own). Measured from E916 at
    /// 160 px: ring centre (8, 9.1), outer radius 6.9, line 1; bar 5.6–10.4 on top; hand 1.2 wide up to 4.3; button a short
    /// diagonal at the top right. The compositor turns the hand (<see cref="TimerDial"/>).
    /// </summary>
    private UIElement DialContent(TimerDial dial, Windows.UI.Color color, bool paused)
    {
        var brush = BrushOf(color);
        var box = new Microsoft.UI.Xaml.Controls.Grid { Width = 16, Height = 16, IsHitTestVisible = false };
        var canvas = new Microsoft.UI.Xaml.Controls.Canvas { Width = 16, Height = 16 };
        const double cx = 8, cy = 9.1, outer = 6.9, line = 1, hand = 1.2, tip = 4.3;
        var ring = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 2 * outer, Height = 2 * outer, Stroke = brush, StrokeThickness = line };
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(ring, cx - outer);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(ring, cy - outer);
        var bar = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 4.8, Height = line, RadiusX = line / 2, RadiusY = line / 2, Fill = brush };
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(bar, cx - 2.4);
        var button = new Microsoft.UI.Xaml.Shapes.Line
        {
            X1 = 13.3, Y1 = 2.7, X2 = 14.4, Y2 = 3.9, Stroke = brush, StrokeThickness = line,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        var needle = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = hand, Height = cy - tip + (hand / 2), RadiusX = hand / 2, RadiusY = hand / 2, Fill = brush,
        };
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(needle, cx - (hand / 2));
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(needle, tip);
        canvas.Children.Add(ring);
        canvas.Children.Add(bar);
        canvas.Children.Add(button);
        canvas.Children.Add(needle);
        box.Children.Add(canvas);

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(needle);
        visual.CenterPoint = new System.Numerics.Vector3((float)(hand / 2), (float)(cy - tip), 0);
        visual.RotationAngleInDegrees = (float)dial.Angle;
        if (dial.Turn > 0 && dial.Over > TimeSpan.Zero)
        {
            var turn = visual.Compositor.CreateScalarKeyFrameAnimation();
            var linear = visual.Compositor.CreateLinearEasingFunction();
            turn.InsertKeyFrame(0f, (float)dial.Angle);
            turn.InsertKeyFrame(1f, (float)(dial.Angle + dial.Turn), linear);
            turn.Duration = dial.Over;
            turn.IterationBehavior = dial.Loop ? Microsoft.UI.Composition.AnimationIterationBehavior.Forever : Microsoft.UI.Composition.AnimationIterationBehavior.Count;
            turn.IterationCount = 1;
            visual.StartAnimation("RotationAngleInDegrees", turn);
        }

        if (paused)
        {
            AddPauseNotch(box);
        }

        return box;
    }


    /// <summary>Tag of the ⋯ cell (task cells carry their card's key).</summary>
    private const string DotsTag = "\u0001dots";

    /// <summary>A 24 cell with a 24-circle hover background (TODO 7.9 D), tagged with its card's key.</summary>
    private Microsoft.UI.Xaml.Controls.Grid MakeCell(string tag, UIElement content)
    {
        var hover = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 24, Height = 24, Fill = BrushOf(HoverColor()), Opacity = 0, IsHitTestVisible = false };
        var cell = new Microsoft.UI.Xaml.Controls.Grid { Width = PillGeometry.Cell, Height = 32, Background = BrushOf(Windows.UI.Color.FromArgb(0, 0, 0, 0)), Tag = tag };
        cell.Children.Add(hover);
        cell.Children.Add(content);
        cell.PointerEntered += (_, _) => hover.Opacity = 1;
        cell.PointerExited += (_, _) => hover.Opacity = 0;
        PressFeedback.Attach(cell);   // sinks while held, springs back and swaps on release (user 10-08)
        return cell;
    }

    /// <summary>
    /// A small icon from the table (<see cref="TaskIcons"/>): the glyph in its tone's colour, or the cover; paused = two bars — on the cover
    /// over a 58% wash of the pill colour in the text colour (follows light/dark, user 10-07), on a glyph at its lower right.
    /// </summary>
    private UIElement IconContent(TaskIcon icon, Microsoft.UI.Xaml.Media.Imaging.BitmapImage? cover)
    {
        var box = new Microsoft.UI.Xaml.Controls.Grid { Width = 16, Height = 16, IsHitTestVisible = false };
        var pill = RealSearchBoxFill();
        if (icon.Cover && cover is { } image)
        {
            box.Width = box.Height = 18;   // the album cover is 18 (user 10-08): a picture reads smaller than a line glyph of the same size
            box.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill } });
            if (icon.Paused)
            {
                box.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = BrushOf(Windows.UI.Color.FromArgb(148, pill.R, pill.G, pill.B)) });
                box.Children.Add(PauseBars(18, 2, 6.75, 6.1, 9.9));   // the 16 cover's bars × 18/16, still centred
            }

            return box;
        }

        if (icon.Logo is { } agent && AgentLogo(agent, ToneColor(icon.Tone)) is { } logo)
        {
            // the same marks as the big pill, the same padding: the spark image is 20 for a 16 icon, Codex's 19
            var size = agent == "codex" ? 19 : 20;
            box.Children.Add(new Microsoft.UI.Xaml.Controls.Image { Source = logo, Width = size, Height = size, Margin = new Thickness((16 - size) / 2.0) });
            return box;
        }

        var color = ToneColor(icon.Tone);
        box.Children.Add(new Microsoft.UI.Xaml.Controls.FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16, Glyph = icon.Glyph, Foreground = BrushOf(color),
        });
        if (icon.Paused)
        {
            AddPauseNotch(box);
        }

        return box;
    }

    /// <summary>
    /// The icon as the big pill draws it: an agent's logo in its makers' colours (as PillView.ShowAgentMark), anything else as its
    /// small icon in the plain icon colour — only small icons are orange / green / red (user 10-10; the border tells it up there).
    /// </summary>
    private UIElement BigIconContent(TaskIcon icon, Microsoft.UI.Xaml.Media.Imaging.BitmapImage? cover)
    {
        if (BrandLogo(icon) is not { } brand)
        {
            return IconContent(icon with { Tone = IconTone.Plain }, cover);
        }

        var box = new Microsoft.UI.Xaml.Controls.Grid { Width = 16, Height = 16, IsHitTestVisible = false };
        var size = icon.Logo == "codex" ? 19 : 20;   // the same padding as the big pill's images
        box.Children.Add(new Microsoft.UI.Xaml.Controls.Image { Source = brand, Width = size, Height = size, Margin = new Thickness((16 - size) / 2.0) });
        return box;
    }

    /// <summary>The big pill draws this icon otherwise than its cell: an agent's logo in its makers' colours, or a coloured icon plain.</summary>
    private bool HasBigLook(TaskIcon icon) => BrandLogo(icon) is not null || icon.Tone != IconTone.Plain;

    /// <summary>A card's icon as the big pill draws it: a timer's turning dial, else <see cref="BigIconContent"/>.</summary>
    private UIElement BigLook(Card card, DateTimeOffset now)
    {
        var cover = CoverOf(card)?.Image;
        return TimerDial.Of(card.State, card.Type == CardType.Inform, now) is { } dial
            ? DialContent(dial, _theme.Icon, card.State.IsPaused)
            : BigIconContent(TaskIcons.For(card, cover is not null, now), cover);
    }

    private ImageSource? BrandLogo(TaskIcon icon) => icon.Logo switch
    {
        "claude" => _claudeGlyph,
        "codex" => _codexBrand,
        _ => null,
    };

    /// <summary>Paused: a pill-coloured disc cuts the icon's bottom-right corner and the two bars sit in it (PillView's).</summary>
    private void AddPauseNotch(Microsoft.UI.Xaml.Controls.Grid box) => PillView.AddPauseNotch(box, BrushOf(RealSearchBoxFill()), BrushOf(_theme.Foreground));

    private FrameworkElement PauseBars(double size, double width, double height, double x1, double x2)
        => PillView.PauseBars(BrushOf(_theme.Foreground), size, width, height, x1, x2);

    /// <summary>The task cell (or ⋯) under <paramref name="source"/>, by its tag; null = not on a cell.</summary>
    private string? CellTagAt(DependencyObject? source)
    {
        for (var node = source; node is not null && node != TaskCells; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { Tag: string tag } && VisualTreeHelper.GetParent(node) == TaskCells)
            {
                return tag;
            }
        }

        return null;
    }

    /// <summary>
    /// Puts the pills side by side (small one right or left, setting), hides the big pill when there is nothing for it, and when
    /// that changes the window's size: re-clips it and moves it to its spot for the new width. Cheap when nothing changed.
    /// </summary>
    private void LayoutPills()
    {
        var big = BigVisible;
        var left = _settings.BallSide == "left";
        var round = _settings.PillCorners != "small";
        PillBody.Visibility = big ? Visibility.Visible : Visibility.Collapsed;
        GapColumn.Width = new GridLength(big ? PillGeometry.Gap : 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(PillBody, left ? 2 : 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(SmallPill, left ? 0 : 2);
        // the small pill's own order: task cells │ circle, or circle │ task cells when it sits left (the 4 margin stays on the outer end)
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(TaskCells, left ? 2 : 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(BallBox, left ? 0 : 2);
        TaskCells.Margin = TaskCellCount == 0 ? new Thickness(0) : left ? new Thickness(0, 0, PillGeometry.CellMargin, 0) : new Thickness(PillGeometry.CellMargin, 0, 0, 0);
        ApplyCorners(round);

        var layout = PillGeometry.Compute(DpiSetup.Scale, big, left, PillGeometry.SmallWidth(TaskCellCount), round);
        if (layout == _layout)
        {
            return;
        }

        _layout = layout;
        ApplyRegion();
        FollowTaskbar();
    }

    /// <summary>Half the height (like Windows' search box) or 8 (setting) — the drawn edges follow the clip.</summary>
    private void ApplyCorners(bool round)
    {
        var r = round ? BaseHeight / 2.0 : PillGeometry.SmallCorner;
        PillSurface.Radius = r;
        SmallSurface.Radius = r;
        GlassBorder.Radius = r;
        GlowRing.Radius = r;
        DimVeil.Radius = r;
        CloseChip.Radius = round ? 11 : Math.Max(0, r - 5);   // concentric with the pill's end, 5 in (2 to the zone, 3 to the chip)
    }

    private DateTimeOffset _lastAttachTry = DateTimeOffset.MinValue;

    /// <summary>The left button went down on the circle; its tool menu opens when it comes back up there.</summary>
    private bool _ballPress;

    /// <summary>The speakers' levels for the now-playing bars (setting "music bars follow the sound").</summary>
    private readonly AudioMeter _audioMeter = new();

    /// <summary>
    /// Renders a sample ("晴工Cl": a full-height CJK character, a capital, an ascender) in the card font into a bitmap and finds its
    /// first and last inked rows — where this font really draws inside the 20 line box. Then the offset that puts the ink's middle
    /// at the line box's middle, rounded to 0.5 like the preview. Descenders (g, y) are left out on purpose: they hang below the
    /// line in every font, and centring on them would push the text up. Run at start and when the font changes.
    /// </summary>
    private async void MeasureInkAsync()
    {
        try
        {
            InkProbe.FontFamily = IslandText.FontFamily;
            InkProbe.CharacterSpacing = IslandText.CharacterSpacing;
            InkProbe.Visibility = Visibility.Visible;
            InkProbe.UpdateLayout();
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(InkProbe);
            var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await bitmap.GetPixelsAsync());
            InkProbe.Visibility = Visibility.Collapsed;
            int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
            int top = -1, bottom = -1;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[(((y * width) + x) * 4) + 3] > 64)   // BGRA: alpha of a clearly inked pixel
                    {
                        top = top < 0 ? y : top;
                        bottom = y;
                        break;
                    }
                }
            }

            if (top < 0 || height == 0)
            {
                return;
            }

            var scale = height / InkProbe.ActualHeight;   // pixels per DIP in the bitmap
            var inkTop = top / scale;
            var inkBottom = (bottom + 1) / scale;
            PillView.InkOffset = Math.Round((IslandText.LineHeight / 2 - ((inkTop + inkBottom) / 2)) * 2) / 2;
            TaskbarHost.Log($"ink: {InkProbe.FontFamily.Source} glyphs {inkTop:0.##}–{inkBottom:0.##} of {InkProbe.ActualHeight:0.##} → offset {PillView.InkOffset}");
            Big.PlaceText();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // async void: nothing may escape
        {
            InkProbe.Visibility = Visibility.Collapsed;
            TaskbarHost.Log("ink: measure failed — " + ex.Message);
        }
    }


    /// <summary>
    /// When to light the border: finished items (Claude/Codex done, timer finished) = green, items needing an answer (permission check) = orange.
    /// Running timers, music and transfers get no border (always-on is distracting). User feedback 09-30: timers and the like should get a border too.
    /// </summary>
    private void UpdateGlow()
    {
        var card = _shownCard;
        var (key, color) = card switch
        {
            _ when _dropHover => ("drop", _theme.Accent),   // dragging over — "drop here to send"
            null or { Pinned: false } => ((string?)null, _theme.Ok),
            { Type: CardType.Pending } => ($"ask|{card.Key}", Attention()),
            { Type: CardType.Inform } when card.State.RunState == ActivityRunState.Error => ($"fail|{card.Key}", Alarm()),   // interrupted / stopped / failed: red, not green (user 10-07)
            { Type: CardType.Inform } => ($"inform|{card.Key}", _theme.Ok),
            { Type: CardType.Temporary } when card.State.RunState == ActivityRunState.Error => ($"warn|{card.Key}", Alarm()),
            _ => ((string?)null, _theme.Ok),
        };
        _animator.UpdateGlow(key, color);
        if (key is not null && color != _glowColor)
        {
            _glowColor = color;
            PaintGlowDim();   // a new border while dim comes in grey too
        }
    }



    private string? _lastMusicSig;

    /// <summary>The fields that decide how the music island looks; when unchanged tick-to-tick, the re-render is skipped.</summary>
    // The text slot's width is part of it: coming back from typing, the columns are still animating when the island first renders,
    // so the title clip was set to a mid-animation (too wide) width and — with re-renders skipped — the scrolling title ran under
    // the now-playing bars (found 10-01 right after sending a prompt).
    private string MusicRenderSig(IsleBar.Core.Island.ActivityState m)
        => string.Concat(m.SourcePath, "|", m.Name, "|", m.Msg, "|", m.Stage, "|", m.Done?.ToString(), "|", m.Total?.ToString(),
            "|", Math.Round(Big.AvailableTextWidth()).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "|", _snapshot.ExtraCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "|", Big.Frozen ? "frozen" : "");

    /// <summary>
    /// Taskbar color just left of the pill. The band outside the alert border is painted this color so the pill clip edge (can't be anti-aliased)
    /// matches the background — where green touched the clip edge it looked stair-stepped, and a white background showed a white line (user feedback 09-30).
    /// </summary>
    private DateTimeOffset _lastBesideCheck;
    private Windows.UI.Color? _besidePending;

    /// <summary>
    /// The taskbar colour around the pill was read only when the pill moved. Switching displays (laptop screen on/off) read it
    /// mid-switch as black, and it stayed: a black ring around the pill until a restart (found 10-03). So read it again every few
    /// seconds and take a new colour once two reads in a row agree (a single odd read — a full-screen video leaving — is ignored).
    /// </summary>
    private void RefreshBesideColor()
    {
        if (!_taskbar.IsAttached || DateTimeOffset.UtcNow - _lastBesideCheck < TimeSpan.FromSeconds(3))
        {
            return;
        }

        _lastBesideCheck = DateTimeOffset.UtcNow;
        if (SampleBeside() is not { } color)
        {
            return;
        }

        if (Root.Background is SolidColorBrush current && current.Color == color)
        {
            _besidePending = null;
        }
        else if (_besidePending == color)
        {
            _besidePending = null;
            Root.Background = BrushOf(color);
            TaskbarHost.Log($"taskbar colour beside the pill changed to {color}");
            if (PillView.UseTaskbar(color))
            {
                // the pills follow the taskbar (TODO 丙④): everything painted from their colours again
                TaskbarHost.Log($"pill colours: taskbar {color} → pill {RealSearchBoxFill()}");
                ApplyTheme();
                ApplyStyle();
                Big.RefreshFades();
                RenderIsland();
            }
        }
        else
        {
            _besidePending = color;
        }
    }

    /// <summary>Debuts queued / started / skipped and why, and the card the big pill shows then ("debut: …"; 甲②③ 10-09).</summary>
    private void LogDebutNotes()
    {
        var notes = _cards.TakeDebutNotes();
        if (notes.Count > 0)
        {
            TaskbarHost.Log($"debut: {string.Join(" | ", notes)} → shown {_cards.Shown?.Key ?? "-"}, lifted {_lift?.OpenKey is not null}, list {_list?.IsOpen == true}, hovering {_hovering}");
        }
    }

    private Windows.UI.Color TaskbarColorBeside()
        => SampleBeside() ?? (ThemeColors.FromSystem().Light
            ? Windows.UI.Color.FromArgb(255, 0xDF, 0xE6, 0xF3)
            : Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20));

    /// <summary>
    /// The screen pixel just left of the pill, or null when it can't be the taskbar: unreadable, or far from the theme's taskbar
    /// (a light taskbar read near-black while the UAC prompt dimmed the screen, and a display switch read it black — 10-03).
    /// </summary>
    private Windows.UI.Color? SampleBeside()
    {
        if (!_placement.Visible)
        {
            return null;
        }

        var dc = GetDC(IntPtr.Zero);
        try
        {
            var y = _placement.Y + (_height / 2);
            var c = GetPixel(dc, _placement.X - DpiSetup.Px(4), y);
            if (c == 0xFFFFFFFF)
            {
                return null;
            }

            var color = Windows.UI.Color.FromArgb(255, (byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
            var luma = (color.R * 0.3) + (color.G * 0.59) + (color.B * 0.11);
            // dark taskbars include a light accent colour ("show accent colour on taskbar"), so only near-white is ruled out there
            return ThemeColors.FromSystem().Light ? (luma >= 110 ? color : null) : (luma <= 210 ? color : null);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);

    /// <summary>Clip to the pill shape (flush, with no margin, when the alert border is on).</summary>
    private void ApplyRegion()
    {
        if (_flying)
        {
            return;   // a transition clips the window itself, to what is flying
        }
        else
        {
            PillRegion.Apply(_taskbar.OuterWindow, _layout);
        }
    }

    /// <summary>Orange (warning) — the Windows warning color, visible on light and dark themes.</summary>
    private static Windows.UI.Color Attention()
        => ThemeColors.FromSystem().Light
            ? Windows.UI.Color.FromArgb(255, 0xC2, 0x6A, 0x00)
            : Windows.UI.Color.FromArgb(255, 0xFC, 0xB0, 0x2F);

    /// <summary>Red (critical) — Windows' error red, for a system warning that needs action (low battery).</summary>
    private static Windows.UI.Color Alarm()
        => ThemeColors.FromSystem().Light
            ? Windows.UI.Color.FromArgb(255, 0xC4, 0x2B, 0x1C)
            : Windows.UI.Color.FromArgb(255, 0xFF, 0x5F, 0x57);   // Windows' own dark-theme critical (FF99A4) read as pink on the pill (10-01)


    /// <summary>
    /// Decoded covers by the player's art key, shared by the big pill and the small pill's music cells (one per player, user 10-07).
    /// Each keeps our own copy of the art stream alive: XAML decodes the image later, on a worker thread, straight from that stream;
    /// the player's own stream can be closed before that — by leaving the method, or on a track change — and decoding a closed stream
    /// fail-fasts the whole bar (RO_E_CLOSED 0x80000013 in Microsoft.UI.Xaml, dump 10-01 23:33).
    /// </summary>
    private readonly Dictionary<string, (Microsoft.UI.Xaml.Media.Imaging.BitmapImage Image, Microsoft.UI.Xaml.Media.Imaging.BitmapImage Large, Windows.UI.Color? Tint, Windows.Storage.Streams.IRandomAccessStream Stream)> _covers = new(StringComparer.Ordinal);

    /// <summary>Covers being read, and ones that failed (not retried every tick; the note shows instead).</summary>
    private readonly HashSet<string> _coversBusy = new(StringComparer.Ordinal);

    /// <summary>The loaded cover of a music card, or null (none, not loaded yet, failed).</summary>
    private (Microsoft.UI.Xaml.Media.Imaging.BitmapImage Image, Microsoft.UI.Xaml.Media.Imaging.BitmapImage Large, Windows.UI.Color? Tint, Windows.Storage.Streams.IRandomAccessStream Stream)? CoverOf(Card? card)
        => card is { State.Kind: ActivityKind.Music } && _feed.Media.ArtOf(card.Key) is { } art && _covers.TryGetValue(art.Key, out var cover)
            ? cover
            : null;

    /// <summary>Starts reading the cover of every music card that has a new one (every tick; cheap when nothing changed) and drops unused ones.</summary>
    private void LoadAlbumArt()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in _cards.Cards)
        {
            if (card.State.Kind == ActivityKind.Music && _feed.Media.ArtOf(card.Key) is { } art)
            {
                used.Add(art.Key);
                if (!_covers.ContainsKey(art.Key) && _coversBusy.Add(art.Key))
                {
                    LoadCover(art.Art, art.Key);
                }
            }
        }

        foreach (var gone in _covers.Keys.Where(k => !used.Contains(k)).ToList())
        {
            _covers.Remove(gone);   // the image may still be on its way out: its stream goes with it to the GC
        }

        _coversBusy.RemoveWhere(k => !used.Contains(k) && !_coversLoading.Contains(k));
    }

    private readonly HashSet<string> _coversLoading = new(StringComparer.Ordinal);

    private async void LoadCover(Windows.Storage.Streams.IRandomAccessStreamReference art, string key)
    {
        _coversLoading.Add(key);
        try
        {
            var owned = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var source = await art.OpenReadAsync())
            {
                await Windows.Storage.Streams.RandomAccessStream.CopyAsync(source, owned);
            }

            owned.Seek(0);
            var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = DpiSetup.Px(24) };
            await image.SetSourceAsync(owned);
            owned.Seek(0);
            // the lifted card's cover is up to 72 (TODO 7.9 F2): decoded at the pill's 24 it came out blurred (user 10-08)
            var large = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = DpiSetup.Px(CardSheets.CoverSize(progress: false)) };
            await large.SetSourceAsync(owned);
            var tint = await PickAlbumTintAsync(art);
            _covers[key] = (image, large, tint, owned);
            _coversBusy.Remove(key);
            _cellsSig = null;   // the small pill's music cell picks it up
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // async void: anything escaping here ends the app (a player's cover file gone — review 10-03)
        {
            // stays in _coversBusy: not retried until the player gives other art
        }
        finally
        {
            _coversLoading.Remove(key);
        }
    }


    /// <summary>Shrinks the album art to 16×16 and picks its dominant color (Core's AlbumTint). Returns null on failure.</summary>
    private async Task<Windows.UI.Color?> PickAlbumTintAsync(Windows.Storage.Streams.IRandomAccessStreamReference art)
    {
        try
        {
            using var stream = await art.OpenReadAsync();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
                new Windows.Graphics.Imaging.BitmapTransform { ScaledWidth = 16, ScaledHeight = 16 },
                Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
            var light = PillSurface.Fill is SolidColorBrush fill && ((fill.Color.R * 0.3) + (fill.Color.G * 0.59) + (fill.Color.B * 0.11)) > 128;
            return IsleBar.Core.Ui.AlbumTint.Pick(pixels.DetachPixelData(), light) is { } c
                ? Windows.UI.Color.FromArgb(255, c.R, c.G, c.B)
                : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // async void: anything escaping here ends the app (a player's cover file gone — review 10-03)
        {
            return null;
        }
    }


    /// <summary>
    /// If not attached to the taskbar, try again: every 2 s at first (on startup SetParent sometimes fails with error 87, but a retry
    /// works — measured 09-29), then every 10 s. Meanwhile the bar floats above the taskbar (<see cref="TaskbarHost.PlaceAt"/>) —
    /// the fallback of TODO 7.8, instead of staying invisible behind it.
    /// </summary>
    private void RetryAttachIfNeeded()
    {
        if (_taskbar.IsAttached)
        {
            _attachFailures = 0;   // attached by another path (end of typing, menu closed) — a later run of failures counts from zero
            return;
        }

        var wait = TimeSpan.FromSeconds(_attachFailures < AttachFailuresBeforeSlowRetry ? 2 : 10);
        if (DateTimeOffset.UtcNow - _lastAttachTry < wait)
        {
            return;
        }

        _lastAttachTry = DateTimeOffset.UtcNow;
        if (_taskbar.Attach())
        {
            if (_attachFailures > 0)
            {
                TaskbarHost.Log($"attach recovered after {_attachFailures} tries — back inside the taskbar");
            }

            _attachFailures = 0;
            PlaceOnTaskbar();
            ApplyRegion();
        }
        else
        {
            ++_attachFailures;
            PlaceOnTaskbar();   // floating: topmost at the same spot
            ApplyRegion();
            if (_attachFailures >= AttachFailuresBeforeRestart && !_menuOpen && RestartForAttachAllowed())
            {
                // Sometimes the taskbar keeps refusing this window (error 87) while it takes any other — a fresh start of the bar
                // attached every time (seen twice on 10-03). Once per 5 minutes at most, so a Windows-wide refusal can't loop.
                TaskbarHost.Log("attach keeps failing — restarting the bar");
                RequestExit(ExitCodes.RestartRequested);
            }
        }
    }

    private const int AttachFailuresBeforeRestart = 6;    // 3 × 2 s + 3 × 10 s ≈ 36 s, as before

    private static bool RestartForAttachAllowed()
    {
        var mark = Path.Combine(AppPaths.DataDirectory, "attach-restart.txt");
        try
        {
            if (File.Exists(mark) && DateTime.UtcNow - File.GetLastWriteTimeUtc(mark) < TimeSpan.FromMinutes(5))
            {
                return false;
            }

            File.WriteAllText(mark, DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private const int AttachFailuresBeforeSlowRetry = 3;
    private int _attachFailures;

    private void RenderIsland()
    {
        _bigCard = !_dropHover && _snapshot.IsActive ? _shownCard : null;
        LayoutPills();
        ShowCloseZone();
        ApplyStyle();
        Big.UpdateProgress(_snapshot.Primary);
        UpdateGlow();
        if (RenderDropHint())
        {
            return;
        }

        if (!_snapshot.IsActive)
        {
            Big.Clear();
            Big.ShowCardLead();
            return;
        }

        var state = _snapshot.Primary!;
        RenderIslandState(state);
    }

    /// <summary>
    /// While a file is dragged over the pill, show it there instead of only the blue border (user 10-01): a file icon and the file's
    /// name (whatever the drop command does, you're handing it a file — so no wording that would be wrong for someone else's
    /// command). Returns false when not dragging.
    /// </summary>
    private bool RenderDropHint()
    {
        if (!_dropHover)
        {
            return false;
        }

        RenderIslandState(new ActivityState
        {
            RawKind = ActivityState.KindNotice,
            Title = _dropNames ?? string.Empty,
            Glyph = "\uE8A5",   // document
        });
        return true;
    }

    private void RenderIslandState(ActivityState state)
    {
        Big.ShowCardLead();
        Big.Render(state, _shownCard, plainIcon: _dropHover);
    }


    // ---------------- Appearance ----------------


    // one brush per colour, shared: the pill set fresh brushes on its text, ring and lines on every draw (4 a second), and
    // their native halves were freed only when a rare GC ran — memory crept up all day (review 10-03). Nothing changes a
    // brush's colour after it is set, so sharing is safe.
    private readonly Dictionary<Windows.UI.Color, SolidColorBrush> _brushes = [];

    private SolidColorBrush BrushOf(Windows.UI.Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            if (_brushes.Count > 128)
            {
                _brushes.Clear();   // album tints and blended colours come and go
            }

            _brushes[color] = brush = new SolidColorBrush(color);
        }

        return brush;
    }


    private void ApplyFonts()
    {
        Big.UseFontsFor(string.Empty);
        if (Root.IsLoaded)
        {
            MeasureInkAsync();   // another language, another font: its glyphs sit differently
        }
    }

    /// <summary>
    /// Applies colors. <b>Text color follows the actual background brightness, not the setting</b> —
    /// during a theme switch the registry and the screen disagree, and trusting the setting makes text vanish.
    /// </summary>
    /// <summary>
    /// The agents' logos, from what is installed on this PC (nothing is shipped — user 10-01): Claude's spark from the command-line
    /// claude.exe or the Claude app's icon; Codex's mark from the VS Code / Cursor extension or the Codex app's own logo. Each is kept
    /// as a shape and painted in one colour (user 10-07): the plain icon colour while working, orange / green / red when it waits for
    /// you, is done, went wrong — the same three colours as every small icon (<see cref="TaskIcons"/>). Called again on a theme change.
    /// </summary>
    private void LoadAgentMarks()
    {
        var light = ThemeColors.FromSystem().Light;
        var cli = ClaudeExecutable.Resolve();
        _claudeMask = ClaudeGlyph.Mask(cli, DpiSetup.Px(21))
                      ?? (AgentApps.ClaudeExe() is { } app ? ClaudeGlyph.Mask(app, DpiSetup.Px(21)) : null);
        _codexSvg = null;
        _codexMask = null;
        string? blossomPath = null, pngPath = null;
        if (CodexMark.Find(CodexMark.DefaultRoots(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), light) is { } blossom)
        {
            try
            {
                _codexSvg = File.ReadAllText(blossom);
                blossomPath = blossom;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // no mark: the glyph
            }
        }
        else if (AgentApps.CodexLogo(light) is { } png)
        {
            _codexMask = ClaudeGlyph.MaskFromImage(png, DpiSetup.Px(19));
            pngPath = png;
        }

        _logos.Clear();
        // the big pill (and the lifted card) keep the makers' own colours — its ring already tells the stage (user 10-07);
        // only the small icons are one colour + orange / green / red
        _claudeGlyph = AgentLogo("claude", ClaudeOrange);
        _codexBrand = blossomPath is not null
            ? new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri(blossomPath))
            : _codexMask is not null && pngPath is not null
                ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(pngPath)) { DecodePixelWidth = DpiSetup.Px(19) }
                : null;
        CodexGlyph.Source = _codexBrand;
        TaskbarHost.Log($"agent marks: claude {(_claudeGlyph is null ? "none" : "ok")}, codex {(_codexSvg is not null ? "svg" : _codexMask is not null ? "png" : "none")}");
        _cellsSig = null;   // small icons pick the logos up
    }

    private byte[]? _claudeMask;
    private ImageSource? _codexBrand;

    /// <summary>Claude's own colour for its spark on the big pill.</summary>
    private static readonly Windows.UI.Color ClaudeOrange = Windows.UI.Color.FromArgb(255, 0xD9, 0x77, 0x57);
    private byte[]? _codexMask;
    private string? _codexSvg;
    private readonly Dictionary<(string Agent, Windows.UI.Color Color), ImageSource> _logos = [];

    /// <summary>An agent's logo ("claude" / "codex") painted in <paramref name="color"/>; null when this PC doesn't have it.</summary>
    private ImageSource? AgentLogo(string agent, Windows.UI.Color color)
    {
        if (_logos.TryGetValue((agent, color), out var known))
        {
            return known;
        }

        ImageSource? logo = null;
        if (agent == "claude" && _claudeMask is { } spark)
        {
            logo = ClaudeGlyph.Paint(spark, DpiSetup.Px(21), color);
        }
        else if (agent == "codex" && _codexMask is { } mark)
        {
            logo = ClaudeGlyph.Paint(mark, DpiSetup.Px(19), color);
        }
        else if (agent == "codex" && _codexSvg is { } svg)
        {
            // the extension's mark is one-colour vector art: every fill / stroke takes the colour
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            var painted = System.Text.RegularExpressions.Regex.Replace(svg, @"\b(fill|stroke)(=""|:\s*)(?!none)[^"";]+", m => m.Groups[1].Value + m.Groups[2].Value + hex);
            var source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource();
            _ = source.SetSourceAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(painted)).AsRandomAccessStream());
            logo = source;
        }

        if (logo is not null)
        {
            _logos[(agent, color)] = logo;
        }

        return logo;
    }

    /// <summary>The colour of a small icon's tone (also the agents' logos on the big pill).</summary>
    private Windows.UI.Color ToneColor(IconTone tone) => tone switch
    {
        IconTone.Waiting => Attention(),
        IconTone.Done => _theme.Ok,
        IconTone.Error => Alarm(),
        _ => _theme.Icon,
    };

    /// <summary>
    /// Light/dark switched while running: recolour in place. The taskbar itself often repaints a few seconds after the
    /// setting changes, so the colours sampled from it are taken again a few times while it settles.
    /// </summary>
    private void Retheme()
    {
        ApplyTheme();
        LoadAgentMarks();
        ApplyStyle();
        Big.RefreshFades();   // the edge fades of a scrolling title keep the old pill colour otherwise (grey boxes)
        RenderIsland();
        foreach (var delay in new[] { 0.8, 2.0, 4.0, 7.0 })
        {
            _ = Task.Delay(TimeSpan.FromSeconds(delay)).ContinueWith(_ => _ui.TryEnqueue(() =>
            {
                ApplyTheme();
                ApplyStyle();
                Big.RefreshFades();
                RenderIsland();
            }), TaskScheduler.Default);
        }
    }

    private void ApplyTheme()
    {
        var taskbar = TaskbarColorBeside();
        if (PillView.UseTaskbar(taskbar))
        {
            TaskbarHost.Log($"pill colours: taskbar {taskbar} → pill {RealSearchBoxFill()}");
        }

        var background = RealSearchBoxFill();
        _theme = ThemeColors.ForActualBackground(background, ThemeColors.AccentColor());
        PillSurface.Fill = BrushOf(background);
        Root.Background = BrushOf(taskbar);   // outside the pill (the stair-steps of the clip edge sit on this color and become invisible)
        LeadIcon.Foreground = BrushOf(_theme.Icon);
        BallRing.Stroke = BrushOf(_theme.Icon);
        CloseLabel.Text = _text.CloseCard;
        CloseLabel.Foreground = BrushOf(_theme.Foreground);
        SmallRule.Fill = BrushOf(ThemeColors.FromSystem().Light ? Windows.UI.Color.FromArgb(255, 0xD6, 0xD6, 0xD6) : Windows.UI.Color.FromArgb(255, 0x46, 0x46, 0x46));
        BallHover.Fill = BrushOf(HoverColor());
        PaintFill();
    }

    /// <summary>
    /// The real search box background color. To decide in phase 0: sample it from a screen capture, or
    /// use theme defaults (light: F2F6FC / dark: white at 6% alpha).
    /// The Python version captured behind the window and used the median of the text-free top row.
    /// </summary>
    private Windows.UI.Color RealSearchBoxFill() => PillView.RestFill();

    private bool _hovering;

    /// <summary>The pointer is on the close button.</summary>
    private bool _closeHover;


    /// <summary>
    /// Apply preferences: re-apply colors the moment the black background toggles (text color follows actual background brightness);
    /// the glow border uses the album color for music, otherwise a faint color matching the background. Safe to call on every draw.
    /// </summary>
    private void ApplyStyle()
    {
        var glass = _settings.StyleGlass;
        GlassEdge.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
        if (!glass)
        {
            return;
        }

        var dark = !(PillSurface.Fill is SolidColorBrush fill && ((fill.Color.R * 0.3) + (fill.Color.G * 0.59) + (fill.Color.B * 0.11)) > 128);
        var edge = _snapshot.IsActive && _snapshot.Primary?.Kind == ActivityKind.Music && Big.AlbumTint is { } tint
            ? Windows.UI.Color.FromArgb(140, tint.R, tint.G, tint.B)
            : dark ? Windows.UI.Color.FromArgb(26, 255, 255, 255) : Windows.UI.Color.FromArgb(22, 0, 0, 0);
        GlassBorder.Edge = BrushOf(edge);
        GlassShine.Visibility = dark ? Visibility.Visible : Visibility.Collapsed;   // the white shine line isn't visible on light backgrounds
    }

    /// <summary>Pill background: normal / hovered (slightly brighter — same response as the real search box, Python version's LOOK).</summary>
    private void PaintFill()
    {
        var colours = PillView.Colours;
        // on the close button only the button lights up; elsewhere on the pill only the pill, the button stays as it is (user 10-10)
        var lift = _hoverBig && !_closeHover;
        PillSurface.Fill = BrushOf(PillView.ColourOf(lift ? colours.Hover : colours.Rest));
        SmallSurface.Fill = BrushOf(PillView.ColourOf(colours.Rest));   // the small pill lifts only its circle cell (BallHover)
        // the close button: a step off the resting pill's colour, one more under the pointer (user 10-08, B5)
        CloseChip.Fill = BrushOf(PillView.ColourOf(_closeHover ? colours.ChipHover : colours.Chip));
        Big.RefreshFades();   // the scrolling text's end fades are the pill colour: repaint them when it lifts (hover) — they showed as dark blocks (user 10-07)

        PaintEdge();
    }

    /// <summary>Thin line around the pill edge (like the real search box border) — a faint line matched to background brightness.</summary>
    private void PaintEdge()
    {
        PillSurface.Edge = BrushOf(PillView.EdgeFill());
        SmallSurface.Edge = PillSurface.Edge;
    }

    /// <summary>The faint darkening / lightening under the pointer (the circle's cell, the close zone).</summary>
    private static Windows.UI.Color HoverColor() => ThemeColors.FromSystem().Light
        ? Windows.UI.Color.FromArgb(18, 0, 0, 0)
        : Windows.UI.Color.FromArgb(22, 255, 255, 255);

    private static Windows.UI.Color Blend(Windows.UI.Color a, Windows.UI.Color b, double t)
        => Windows.UI.Color.FromArgb(255,
            (byte)(a.R + ((b.R - a.R) * t)), (byte)(a.G + ((b.G - a.G) * t)), (byte)(a.B + ((b.B - a.B) * t)));

    /// <summary>
    /// The pill only hears "pointer left" from XAML, and as a child of the taskbar that event can be missed (a fast move off the
    /// pill left ⏮⏯⏭ showing with the mouse elsewhere — in-use test 10-01). So on each poll, if we think we're hovered but the
    /// cursor is outside the pill's window, end the hover.
    /// </summary>
    private void EndStaleHover()
    {
        if (!_hovering
            || !Interop.NativeMethods.GetCursorPos(out var cursor)
            || !Interop.NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var pill))
        {
            return;
        }

        if (cursor.X < pill.Left || cursor.X >= pill.Right || cursor.Y < pill.Top || cursor.Y >= pill.Bottom)
        {
            SetHover(false);
        }
    }

    private void SetHover(bool hovering)
    {
        if (hovering && !_hovering)
        {
            Big.ReplayMarquee();
        }

        _hovering = hovering;
        _cards.SetHover(hovering, DateTimeOffset.UtcNow);   // the mouse on either pill holds the shown card (TODO 7.9 D, 10-07)
        if (!hovering)
        {
            SetBigHover(false);
            BallHover.Opacity = 0;
            foreach (var cell in TaskCells.Children.OfType<Microsoft.UI.Xaml.Controls.Grid>())
            {
                cell.Children[0].Opacity = 0;   // a missed "pointer left" must not leave a cell lit
            }
        }

        Big.RefreshFades();
    }

    /// <summary>An inform card shows its close button on the right (TODO 7.3, A4; moved right 10-08); every other card doesn't.</summary>
    private void ShowCloseZone()
    {
        var show = !_dropHover && _shownCard is { Type: CardType.Inform };
        Big.ShowClose(show);
        if (!show)
        {
            _closeHover = false;
        }
    }

    /// <summary>The mouse on the big pill: it lifts slightly, like the real search box.</summary>
    private bool _hoverBig;

    private void SetBigHover(bool hovering)
    {
        if (_hoverBig != hovering)
        {
            _hoverBig = hovering;
            PaintFill();
        }
    }

    /// <summary>
    /// Horizontal centre of the pill: windows that open above it (options, settings, compose, preview, file results) are centred
    /// on it — they used to start at its left edge and hang off to the right (user 10-01).
    /// </summary>
    private int PopupAnchorX => Spot.X + (_width / 2);

    /// <summary>
    /// Where the pill is — or, while it's hidden for lack of room (search box off, icons left-aligned), the middle of the screen just
    /// above the taskbar: typing with the hotkey and message cards used (0,0) then, the top-left corner or off screen (review 10-03).
    /// </summary>
    private (int X, int Y) Spot
    {
        get
        {
            if (_placement.Visible)
            {
                return (_placement.X, _placement.Y);
            }

            var screen = Microsoft.UI.Windowing.DisplayArea.Primary.OuterBounds;
            return (screen.X + ((screen.Width - _width) / 2), TaskbarTop() - _height - DpiSetup.Px(12));
        }
    }

    /// <summary>Puts the attached pill at its spot on the taskbar, or keeps it hidden when there is none.</summary>
    private void PlaceOnTaskbar()
    {
        if (!_placement.Visible)
        {
            Interop.NativeMethods.ShowWindow(_taskbar.OuterWindow, Interop.NativeMethods.SW_HIDE);
            return;
        }

        _taskbar.PlaceAt(_placement.X, _placement.Y, _width, _height);
    }

    /// <summary>The diagnostics lines of the settings window: is the bar inside the taskbar, and where it sits.</summary>
    private IReadOnlyList<(string Label, string Value)> Diagnostics()
    {
        var embed = _taskbar.IsAttached
            ? _text.DiagOk
            : $"{_text.DiagFloating} (err {_taskbar.LastError} ×{_attachFailures})";
        var name = BarSlot.Parse(_settings.Position) switch
        {
            BarPosition.Center => _text.PositionCenter,
            BarPosition.BesideTray => _text.PositionTray,
            BarPosition.Manual => _text.PositionManual,
            _ => _text.PositionLeft,
        };
        var place = _placement.Visible ? $"{name} · x={_placement.X}" : $"{name} · {_text.DiagNoRoom}";
        return [(_text.DiagEmbed, embed), (_text.PositionLabel, place)];
    }

    /// <summary>Settings window. Brings it to the front if already open.</summary>
    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new Options.SettingsWindow(_settings, _text, OnSettingsChanged, OnLanguageChanged, () => _language, Diagnostics);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.ShowAbove(PopupAnchorX, TaskbarTop());
    }

    private void OnSettingsChanged()
    {
        _feed.ApplySettings();
        _store.Save(_settings);
        LayoutPills();   // the small pill's side and the corners are settings
        RenderIsland();  // ...and so is centring the card text
    }

    private void OnLanguageChanged()
    {
        _language = LanguageResolver.Resolve(_settings.Lang, CurrentUiLanguageId());
        _text = LanguageCatalog.For(_language);
        ApplyFonts();
        _settingsWindow?.Reload(_text);
    }

    /// <summary>Top edge of the taskbar (screen coordinates). The options window opens above it.</summary>
    private static int TaskbarTop()
    {
        if (Interop.NativeMethods.GetWindowRect(Interop.TaskbarHost.FindTaskbar(), out var rect))
        {
            return rect.Top;
        }

        var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        return area.Y + area.Height;
    }

    /// <summary>
    /// Dragging a file onto the pill runs the drop command on it (e.g. a script that sends to a phone — PhoneDrop). With no command
    /// set the pill doesn't take files (10-10: attaching them to a prompt went with the input box). Blue border while hovering; on
    /// transfer, the island shows it sending.
    /// </summary>
    private void HookFileDrop()
    {
        Root.DragOver += (_, e) =>
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
                || string.IsNullOrWhiteSpace(_settings.DropCommand))
            {
                return;
            }

            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            if (!_dropHover)
            {
                _dropHover = true;
                _dropNames = null;
                RenderIsland();
                _ = ReadDropNamesAsync(e.DataView);
            }
        };
        Root.DragLeave += (_, _) =>
        {
            _dropHover = false;
            RenderIsland();
        };
        Root.Drop += async (_, e) =>
        {
            _dropHover = false;
            RenderIsland();
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
                || string.IsNullOrWhiteSpace(_settings.DropCommand))
            {
                return;
            }

            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (!_feed.PhoneDrop.Send(paths, _settings.DropCommand))
                {
                    TaskbarHost.Log($"drop→command: nothing to run or not files ({paths.Count})");
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)   // async void: anything escaping ends the app (review 10-03)
            {
                TaskbarHost.Log("drop failed: " + ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        };
    }

    /// <summary>Fills in the dragged files' names for the drop hint (read asynchronously, so the hint shows at once and the names follow).</summary>
    private async Task ReadDropNamesAsync(Windows.ApplicationModel.DataTransfer.DataPackageView view)
    {
        try
        {
            var items = await view.GetStorageItemsAsync();
            if (items.Count == 0 || !_dropHover)
            {
                return;
            }

            _dropNames = items[0].Name + (items.Count > 1 ? $" +{items.Count - 1}" : string.Empty);
            RenderIsland();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
    }

    /// <summary>Windows display language (LANGID). The resolution rules live in Core (LanguageResolver).</summary>
    private static int CurrentUiLanguageId() => GetUserDefaultUILanguage();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    private const int MenuQuit = 1;
    private const int MenuClose = 2;   // close the card the menu is about
    private const int MenuTuck = 3;    // keep the big pill's card in the small pill (甲⑤)
    private const int MenuUntuck = 4;
    private const int MenuQuickBase = 20;   // quick timers (from settings, up to 6)
    private const int MenuPomodoro = 13;
    private const int MenuStopwatch = 14;
    private const int MenuSettings = 17;

    /// <summary>Starts a timer next to any that are running (TODO 三-④ B, 10-10: they used to replace each other after asking in the input box).</summary>
    private void StartTimer(ActivityState next) => _feed.StartTimer(next);

    /// <summary>
    /// "Close" on a card: a state file goes away (a finished task, a timer that rang, a failure, an agent's turn); a card with no file
    /// (music, a download) is hidden until it ends.
    /// </summary>
    private void CloseCard(Card? card)
    {
        if (card is null)
        {
            return;
        }

        if (card.State.SourcePath is { } path && File.Exists(path))
        {
            ActivityFeed.DeleteState(path);
        }
        else if (card.State.SourcePath is { } drop && drop.StartsWith("drop:", StringComparison.Ordinal))
        {
            _feed.PhoneDrop.Forget(drop);   // a failed send stays until closed
        }
        else
        {
            _cards.Hide(card.Key, DateTimeOffset.UtcNow);
        }

        Restack();
    }

    /// <summary>The cards changed by hand (closed, tucked): the stack again, then the next card comes up / icons move, animated.</summary>
    private void Restack()
    {
        _cards.Update(_feed.Collect(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
        LogDebutNotes();
        _shownCard = _cards.Shown;
        _snapshot = _shownCard is { } shown ? new IslandSnapshot(shown.State, _cards.Cards.Count - 1) : IslandSnapshot.Empty;
        _animator.Classify(_snapshot);
        if (ComputeCells())
        {
            _ = Transition(hoverKey: null);   // the next card comes up / icons close the gap, animated
        }
        else
        {
            RenderIsland();
        }
    }

    /// <summary>While the right-click menu is open (blocks starting to type).</summary>
    private bool _menuOpen;

    /// <summary>
    /// Right click: each place has its own menu (TODO 7.3, user 10-07) — the big pill = close its card; a task icon = that card's
    /// name and state in grey, then close; the circle = the tools. The dots, the line and the margins open nothing.
    /// </summary>
    private void ShowZoneMenu(DependencyObject? source)
    {
        if (IsInside(source, BallBox))
        {
            ShowToolMenu();
        }
        else if (IsInside(source, SmallPill))
        {
            if (CellTagAt(source) is { } tag && tag != DotsTag && _cellCards.FirstOrDefault(c => c.Key == tag) is { } card)
            {
                ShowCardMenu(card, header: true);
            }
        }
        else if (_shownCard is { } shown && !_dropHover)
        {
            ShowCardMenu(shown, header: false);
        }
    }

    private void ShowCardMenu(Card card, bool header)
    {
        var items = new List<MenuItem>();
        if (header)
        {
            items.Add(new MenuItem(0, CardMenuTitle(card), Header: true));
        }

        // "Pin" keeps the big pill's card in the small pill (user 10-10; a temporary one has no cell); a pinned card — on its icon,
        // or on the big pill while clicked up — offers "Unpin" (user 10-09)
        var tucked = _cards.IsTucked(card.Key);
        if (tucked)
        {
            items.Add(new MenuItem(MenuUntuck, _text.Unpin));
        }
        else if (!header && card.Type != CardType.Temporary)
        {
            items.Add(new MenuItem(MenuTuck, _text.Pin));
        }

        items.Add(new MenuItem(MenuClose, _text.CloseCard));
        var chosen = RunMenu(items);
        // after RunMenu has let go of the menu (one beat later): while "menu open" the cells don't change, and the card used to
        // jump to its cell instead of flying (user 10-09)
        _ui.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            switch (chosen)
            {
                case MenuClose:
                    CloseCard(_cards.Cards.FirstOrDefault(c => c.Key == card.Key));
                    break;
                case MenuTuck:
                    TaskbarHost.Log($"card: {card.Key} pinned to the small pill");
                    _cards.Tuck(card.Key, DateTimeOffset.UtcNow);
                    Restack();
                    break;
                case MenuUntuck:
                    TaskbarHost.Log($"card: {card.Key} unpinned");
                    _cards.Untuck(card.Key, DateTimeOffset.UtcNow);
                    Restack();
                    break;
            }
        });
    }

    /// <summary>"report.pdf · 12.3 MB/s" · "islebar · Claude working" · "Song · Artist" — what the card is and how it stands.</summary>
    private string CardMenuTitle(Card card)
    {
        var s = card.State;
        var now = DateTimeOffset.UtcNow;
        var (name, state) = s.Kind switch
        {
            ActivityKind.AgentWorking => (s.Name, $"{s.AgentLabel} {_text.Working}"),
            ActivityKind.AgentPermission => (s.Name, $"{s.AgentLabel} {_text.NeedsAnswer}"),
            ActivityKind.AgentDone => (s.Name, $"{s.AgentLabel} {_text.AgentDone}"),
            ActivityKind.Music => (s.Name, s.Stage == "paused" ? _text.Paused : s.Msg),
            ActivityKind.Timer => (s.Name ?? _text.Timer, Big.PrefixFor(s, now).Trim()),
            ActivityKind.Transfer => (s.Name ?? (s.Stage == "copy" ? _text.FileTransfer : _text.Downloading), Big.PrefixFor(s, now).Trim()),
            _ => (s.Title, s.Name ?? s.Msg),
        };
        var line = string.Join(" · ", new[] { name, state }.Where(t => !string.IsNullOrWhiteSpace(t)));
        return line.Length <= 48 ? line : line[..47] + "…";
    }

    /// <summary>The circle's tools: timers (a submenu), all notifications, an available update, settings, quit.</summary>
    private void ShowToolMenu()
    {
        var timers = new List<MenuItem>();
        var quick = _settings.QuickTimers.Take(6).ToList();
        for (var q = 0; q < quick.Count; q++)
        {
            timers.Add(new MenuItem(MenuQuickBase + q, string.Format(System.Globalization.CultureInfo.InvariantCulture, _text.Minutes, quick[q])));
        }

        if (timers.Count > 0)
        {
            timers.Add(MenuItem.Separator);
        }

        timers.Add(new MenuItem(MenuPomodoro, _text.Pomodoro));
        timers.Add(new MenuItem(MenuStopwatch, _text.Stopwatch));

        var items = new List<MenuItem> { new(0, _text.Timer, Sub: timers) };
        items.Add(MenuItem.Separator);
        items.Add(new MenuItem(MenuSettings, _text.Settings));
        items.Add(new MenuItem(MenuQuit, _text.Quit));

        var chosen = RunMenu(items);
        var now = DateTimeOffset.UtcNow;
        if (chosen == MenuQuit)
        {
            RequestExit(ExitCodes.UserQuit);
        }
        else if (chosen == MenuSettings)
        {
            ShowSettings();
        }
        else if (chosen >= MenuQuickBase && chosen - MenuQuickBase < quick.Count)
        {
            StartTimer(TimerParser.ToActivity(TimeSpan.FromMinutes(quick[chosen - MenuQuickBase]), now));
        }
        else if (chosen == MenuPomodoro)
        {
            StartTimer(TimerParser.ToPomodoro(1, focus: true, now, _text.Focus, _text.Break, _feed.PomodoroLengths()));
        }
        else if (chosen == MenuStopwatch)
        {
            StartTimer(TimerParser.ToStopwatch(now, _text.Stopwatch));
        }
    }

    /// <summary>
    /// Shows a menu and returns the chosen id. A window attached to the taskbar can't become the foreground window, so the menu
    /// couldn't receive clicks (measured on PC 09-29) → detach and bring it forward, only while the menu is open.
    /// Position and shape are unchanged, so it's unnoticeable.
    /// </summary>
    private int RunMenu(IReadOnlyList<MenuItem> items)
    {
        var detached = false;
        if (_taskbar.IsAttached)
        {
            _taskbar.Detach(Spot.X, Spot.Y, _width, _height);
            ApplyRegion();
            detached = true;
        }

        _menuOpen = true;
        int chosen;
        try
        {
            chosen = ContextMenu.Show(_taskbar.OuterWindow, items);
        }
        finally
        {
            // focus notifications can arrive late after the menu closes, so release one beat later
            _ui.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _menuOpen = false);
        }

        if (detached && _taskbar.Attach())
        {
            PlaceOnTaskbar();
            ApplyRegion();
        }

        return chosen;
    }

    private void RequestExit(int code)
    {
        App.ExitCode = code;

        // When Explorer restarts, the window inside the taskbar is destroyed with it, the UI thread never gets this request, and we were left as a windowless process
        // (measured on an Explorer restart 09-30: hung after "restart: window gone"). If it doesn't finish within 4s, exit here → the watchdog relaunches us.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
            Environment.Exit(code);
        });
        _ui.TryEnqueue(() =>
        {
            _watchdog.Quitting = true;
            _taskbar.ReleaseForClose();   // a re-parented window can't be closed by WinUI without a fail-fast (see ReleaseForClose)
            Close();
        });
    }

    private void Cleanup()
    {
        _watchdog.Quitting = true;
        // every window closed, so the app ends by itself instead of waiting 4 s for the forced exit (review 10-03) — first, while
        // the meter and timers still exist
        foreach (var close in new Action?[] { _settingsWindow is null ? null : _settingsWindow.Close, _lift is null ? null : _lift.Close })
        {
            try
            {
                close?.Invoke();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
            }
        }

        Big.StopMeterBars();
        _audioMeter.Dispose();
        _feed.Dispose();
        _settingsWindow?.Close();

        _islandTimer?.Stop();
        _watchdog.Dispose();
        _tracker.Dispose();
    }
}
