using IsleBar.App.Engine;
using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.App.Media;
using IsleBar.App.Search;
using IsleBar.App.Supervisor;
using IsleBar.App.Tracking;
using IsleBar.Core.Configuration;
using IsleBar.Core.History;
using IsleBar.Core.Island;
using IsleBar.Core.Launch;
using IsleBar.Core.Localization;
using IsleBar.Core.Models;
using IsleBar.Core.Search;
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
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(90);

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
    private readonly FileSearchRequests _fileSearch;
    private readonly Watchdog _watchdog;

    private IsleBarSettings _settings;
    private LanguageStrings _text;
    private string _language;
    private ThemeColors _theme;
    private HistoryNavigator _history;

    private BarMode _mode = BarMode.Claude;
    // not "Hidden": a first placement of Hidden must still be applied (it compared equal and the window stayed where WinUI put it — review 10-03).
    // The window's top-left (the pills' spot moved up by ArcRoom), not the pills'.
    private BarPlacement _placement = new(false, int.MinValue, int.MinValue);
    private int _placedWidth;

    /// <summary>While a file is dragged over the pill (blue border).</summary>
    private bool _dropHover;
    private string? _dropNames;   // names of the files being dragged over the pill, for the drop hint
    private GlobalHotkey? _hotkey;
    private IntPtr _previousForeground;
    private string? _historyText;
    private Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase? _entryMenu;
    private ResultsWindow? _results;
    private OpenRequest? _openWhenReady;

    // the text of the latest file search sent, and of the one the results list shows — Enter right after editing (before the
    // 90 ms debounce and Everything's answer) opened the old query's first hit, an .exe or .lnk included (review 10-03)
    private string? _shownQuery;
    private Options.SettingsWindow? _settingsWindow;
    /// <summary>Claude's logo in the plain icon colour (null = not on this PC); the Codex one is <c>CodexGlyph.Source</c>.</summary>
    private ImageSource? _claudeGlyph;

    /// <summary>Currently detaching from the taskbar (focus changes during this are not the end of typing).</summary>
    private bool _switching;
    private IslandSnapshot _snapshot = IslandSnapshot.Empty;
    private DispatcherQueueTimer? _islandTimer;
    private DispatcherQueueTimer? _searchTimer;
    private bool _typing;

    public MainWindow()
    {
        InitializeComponent();

        _ui = DispatcherQueue.GetForCurrentThread();
        _hwnd = WindowNative.GetWindowHandle(this);
        _taskbar = new TaskbarHost(_hwnd);

        _settings = _store.Load();
        _language = LanguageResolver.Resolve(_settings.Lang, CurrentUiLanguageId());
        _text = LanguageCatalog.For(_language);
        SystemWatch.Updater.Strings = () => _text;
        _theme = ThemeColors.FromSystem();
        _history = new HistoryNavigator(_settings.History);

        _tracker = new TaskbarTracker(() => _layout.Width, DpiSetup.Px(BaseHeight), () => (BarSlot.Parse(_settings.Position), DpiSetup.Px(_settings.ManualOffset)));
        _feed = new ActivityFeed(() => _settings, () => _text);
        _animator = new IslandAnimator([LeadSlot, IslandContent], IslandContent, GlowRing, PillBody);
        _fileSearch = new FileSearchRequests(OnSearchResult);
        _watchdog = new Watchdog(_taskbar, () => ThemeColors.FromSystem().Light, RequestExit,
            _ => _ui.TryEnqueue(Retheme));

        ApplyFonts();
        ApplyTheme();
        LoadAgentMarks();
        UpdateLeadIcon();
        SetMode(HomeMode());   // start in the default mode too (first in Tab order)
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

        // Ctrl+Alt+C = type from anywhere (same key as the Python version). Switches to the input box even if an island is showing.
        _hotkey = new GlobalHotkey(() => _ui.TryEnqueue(() =>
        {
            _taskbar.TakeFocus();
            BeginTyping();
            Entry.Focus(FocusState.Programmatic);
        }));
        _watchdog.Start();
        _foreground = new ForegroundEvents(window =>
        {
            StartMenu.Notice(window);   // remember Start's window when it opens — it stays up after a click on the pill
            _ui.TryEnqueue(CheckForeground);
        });
        StartIslandPolling();
        _ = UpdateModelAliasesAsync();
    }

    // ---------------- Event wiring (one place only) ----------------

    private void WireEvents()
    {
        Entry.GotFocus += (_, _) =>
        {
            // A right click on the idle pill also focuses the input box; starting to type there swapped in the box's
            // copy/paste menu, so right click showed "Paste" instead of our menu (seen 10-02). Right click never starts typing.
            if (!_menuOpen && DateTimeOffset.UtcNow - _rightPressAt > TimeSpan.FromMilliseconds(500)
                && DateTimeOffset.UtcNow - _closedByClickAt > TimeSpan.FromMilliseconds(500))   // the click that just closed the list
            {
                BeginTyping();   // when the right-click menu closed and the window came forward, the input box got focus and typing started (measured on PC 09-30)
            }
        };
        // End of typing = the input box loses focus or the window is deactivated. But ignore transient focus changes while detaching
        // (otherwise: click → detach → focus wobble → immediately reattached, measured on PC 09-29).
        // End of typing only watches for "the window was pushed away by another window". If we also watched the input box losing focus,
        // pressing ⌄ while typing (focus moves to the button) ended typing, ⌄ vanished, and on release there was nothing to click so settings never opened (user feedback 09-30).
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && !_switching)
            {
                // A click on a result / the preview is on its way — what it does ends typing itself (10-03). Not tied to the button
                // state: a touchpad tap arrives as press+release together, so the button already reads "up" here (review 10-03).
                // Alt+Tab with the mouse resting over the preview, or a click that ends up doing nothing (padding, a file that's
                // gone), used to keep typing on for good — the re-check ends it.
                if (_typing && _compose is not null)
                {
                    return;   // the compose box is taking over the question (Shift+Enter, a pasted block) — it ends typing when it closes
                }

                if (_typing && (_results?.UnderCursor() == true || _preview?.UnderCursor() == true))
                {
                    _ui.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ScheduleTypingRecheck);
                    return;
                }

                EndTyping();
            }
        };
        // the input box swallows ↑↓ and Tab first (caret/focus movement), so catch them in the preview stage
        Entry.PreviewKeyDown += OnEntryKeyDown;
        HookCompose();
        HookFileDrop();
        PressFeedback.Attach(BallCell);
        Root.Loaded += (_, _) => MeasureInkAsync();   // where the card font's glyphs sit (TODO 7.9 D: centre the ink, not the line box)   // pressing makes it sink in and spring back (09-30 design M5)
        Entry.TextChanged += (_, _) =>
        {
            UpdateTabHint();
            // When a person types, forget the history position. If the text was inserted from history, keep it — WinUI sends this notification
            // later and separately, so an "inserting" flag can't tell them apart (measured on PC 09-29) → compare against the inserted text instead.
            if (Entry.Text != _historyText)
            {
                _history.Reset();
                _historyText = null;
            }

            if (_mode == BarMode.Files)
            {
                ScheduleSearch();
            }
        };

        // a child window doesn't get focus from a normal click, so take it explicitly
        // handledEventsToo so we get it even if the input box handles the click first. After Esc the input box still holds XAML focus,
        // so GotFocus doesn't fire again; treat the click itself as the start of typing (measured on PC 09-29).
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.GetCurrentPoint(Root).Properties.IsRightButtonPressed)
            {
                _rightPressAt = DateTimeOffset.UtcNow;
            }

            if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
            {
                return;   // right click is the menu (RightTapped)
            }

            var source = e.OriginalSource as DependencyObject;
            if (IsInside(source, SmallPill) && !IsInside(source, BallBox))
            {
                if (!_typing && CellTagAt(source) is { } tag && tag != DotsTag)
                {
                    OnTaskCellClicked(tag);   // that card and the big pill's swap places (TODO 7.3, 10-07)
                }

                return;   // ⋯ opens the list in step F; the line and the margins do nothing
            }

            if (IsInside(source, BallBox))
            {
                // the circle = the way in: click to type, click again to stop (TODO 7.3). Until the new input panel (H), typing
                // happens in the big pill as before.
                if (_typing)
                {
                    _closedByClickAt = DateTimeOffset.UtcNow;
                    EndTyping();
                    return;
                }

                StartMenu.CloseIfOpen();   // Start stays up over a click on the pill and would cover the results list (user 10-04)
                _taskbar.TakeFocus();
                BeginTyping();
                Entry.Focus(FocusState.Programmatic);
                return;
            }

            if (_typing)
            {
                _taskbar.TakeFocus();   // a click in the big pill while typing only moves the caret
                Entry.Focus(FocusState.Programmatic);
                return;
            }

            if (IsInside(source, CloseZone))
            {
                CloseCard(_shownCard);   // an inform card is closed only here or from its menu (TODO 7.2)
            }

            // the rest of the big pill lifts the card (step F)
        }), handledEventsToo: true);
        // Right click = dismiss an island item (cancel a timer, etc.) · turn off the search bar
        // The input box grabs right clicks first (its own copy/paste menu), so receive them with handledEventsToo.
        // While typing, use the input box's menu as-is; otherwise use our menu — and disable the input box menu then (measured on PC 09-29).
        _entryMenu = Entry.ContextFlyout;
        Entry.ContextFlyout = null;
        Root.AddHandler(UIElement.RightTappedEvent, new RightTappedEventHandler((_, e) =>
        {
            if (_typing)
            {
                return;
            }

            e.Handled = true;
            ShowZoneMenu(e.OriginalSource as DependencyObject);
        }), handledEventsToo: true);
        Root.PointerEntered += (_, _) => SetHover(true);
        Root.PointerExited += (_, _) => SetHover(false);
        CloseZone.PointerEntered += (_, _) => CloseHover.Opacity = 1;
        CloseZone.PointerExited += (_, _) => CloseHover.Opacity = 0;
        PillBody.PointerEntered += (_, _) => SetBigHover(true);
        PillBody.PointerExited += (_, _) => SetBigHover(false);
        BallCell.PointerEntered += (_, _) => BallHover.Opacity = 1;
        BallCell.PointerExited += (_, _) => BallHover.Opacity = 0;

        Closed += (_, _) => Cleanup();

        // Remove the WinUI input box clear (X) button. Visual state changes toggle its Visibility, so pin its width to 0 instead.
        Entry.Loaded += (_, _) =>
        {
            if (FindChild(Entry, "DeleteButton") is FrameworkElement delete)
            {
                delete.MaxWidth = 0;
                delete.Margin = new Thickness(0);
            }
        };
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

    private static DependencyObject? FindChild(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } && n == name)
            {
                return child;
            }

            if (FindChild(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // ---------------- Input ----------------

    /// <summary>
    /// When typing starts, <b>detach</b> from the taskbar. Typing while attached tangles IME/focus
    /// with Explorer and can hang it (09-29 AppHangXProc).
    /// </summary>
    private void BeginTyping()
    {
        if (_typing)
        {
            return;
        }

        var before = Interop.NativeMethods.GetForegroundWindow();
        _previousForeground = before != _taskbar.OuterWindow ? before : IntPtr.Zero;   // window to give focus back to when closed with Esc
        _typing = true;
        _typingHadFront = false;
        _switching = true;
        Entry.ContextFlyout = _entryMenu;   // copy/paste menu while typing
        LayoutPills();   // typing happens in the big pill: it appears if no card was showing
        _taskbar.Detach(Spot.X, Spot.Y, _width, _height);
        // keep the pill shape after detaching — without it a square window covers the real search box and feels like "a separate window opened" (PC 09-29)
        ApplyRegion();
        IslandContent.Visibility = Visibility.Collapsed;
        Entry.Visibility = Visibility.Visible;

        RenderIsland();   // clear the island and set the left icon to the Claude/Codex icon (09-29: the timer icon was left behind)
        PaintFill();      // slightly brighter while typing

        // match the IME composition text size to the input box
        ImeFont.Apply(_hwnd, UiFonts.Entry(_language), DpiSetup.Px(14));

        // only re-enable "end of typing" detection after returning the focus shaken by detaching to the input box
        _ui.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            Entry.Focus(FocusState.Programmatic);
            _switching = false;
        });
        UpdateTabHint();
        if (_mode == BarMode.Files)
        {
            ScheduleSearch();   // file search as the first mode: the recent-files list opens right away, like the Start menu's
        }
    }

    private ForegroundEvents? _foreground;
    private DateTimeOffset _closedByClickAt;

    /// <summary>Whether one of our windows has been in front since typing began (until then, ending on "not ours" would be premature).</summary>
    private bool _typingHadFront;

    /// <summary>
    /// Typing ends as soon as a window that isn't IsleBar's comes to the front (a click on the desktop, another app, Alt+Tab).
    /// Called by Windows' foreground notification, and every tick as a backstop. When the click went to the results list, the
    /// keyboard is handed back to the input box once the click is over — so Esc and typing keep working (user 10-04).
    /// </summary>
    private void CheckForeground()
    {
        if (!_typing || _switching)
        {
            return;
        }

        var front = NativeMethods.GetForegroundWindow();
        if (ForegroundEvents.IsOurs(front))
        {
            _typingHadFront = true;
            if (_results is { } list && front == list.Handle && !list.MenuOpen && !NativeMethods.MouseButtonDown())
            {
                ReclaimTyping();
            }

            return;
        }

        if (!_typingHadFront || NativeMethods.MouseButtonDown() || _compose is not null || _menuOpen)
        {
            return;   // not ours yet / mid-click or drag (looked at again next tick) / the compose box or our menu takes over
        }

        EndTyping();
    }

    /// <summary>When typing ends, go back into the taskbar.</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _typingRecheck;

    /// <summary>After a click on a popup kept typing on: end it if the bar is no longer in front and nothing took the typing over.</summary>
    private void ScheduleTypingRecheck()
    {
        _typingRecheck ??= _ui.CreateTimer();
        _typingRecheck.Interval = TimeSpan.FromSeconds(1.5);
        _typingRecheck.IsRepeating = false;
        _typingRecheck.Tick -= OnTypingRecheck;
        _typingRecheck.Tick += OnTypingRecheck;
        _typingRecheck.Start();
    }

    private void OnTypingRecheck(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_typing && (NativeMethods.MouseButtonDown() || _results?.MenuOpen == true))
        {
            sender.Start();   // still holding the button on a popup (scrolling the preview, say) or a result's right-click menu is open — look again later
            return;
        }

        // a click on the results list (a column header, say) leaves that list in front: still typing — hand the keyboard back.
        // Ending here closed the list ~3 s after changing the sort (user 10-04).
        if (_typing && _results is { } list && NativeMethods.GetForegroundWindow() == list.Handle)
        {
            ReclaimTyping();
            sender.Start();
            return;
        }

        if (_typing && _compose is null && !_menuOpen && NativeMethods.GetForegroundWindow() != _taskbar.OuterWindow)
        {
            EndTyping();
        }
    }

    private void EndTyping()
    {
        _openWhenReady = null;   // cancelled before the results came: nothing opens later (review 10-03)
        if (!_typing)
        {
            return;
        }

        _typing = false;
        LayoutPills();
        if (_pendingTimer is not null)
        {
            _pendingTimer = null;   // Esc / clicking away = keep the running timer
            Entry.PlaceholderText = Placeholders.For(_mode, _settings.Agent);   // SetMode below skips this when the mode is unchanged (code review 10-01)
        }
        PaintFill();
        Entry.ContextFlyout = null;         // after typing, right click = our menu
        FocusSink.Focus(FocusState.Programmatic);   // move focus out of the input box so no blinking caret is left behind
        Entry.Text = string.Empty;
        _history.Reset();
        CloseResults();
        SetMode(HomeMode());   // after typing, return to the default mode (first in Tab order), same as the Python version's blur
        if (_taskbar.Attach())
        {
            PlaceOnTaskbar();
            ApplyRegion();
        }

        RenderIsland();
        UpdateTabHint();
        _preview?.Hide();
    }

    private void OnEntryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Tab:
                ToggleMode();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Enter when IsShiftDown() && _mode == BarMode.Claude:
                OpenCompose(Entry.Text + "\n");   // newline = go to the long-question compose window (09-30)
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Enter:
                OnEnter(reveal: IsControlDown());
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Escape:
                Entry.Text = string.Empty;
                EndTyping();
                if (_previousForeground != IntPtr.Zero)
                {
                    ForegroundHelper.Bring(_previousForeground);   // back to the window that was in use before pressing (same as the Python version)
                }

                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Up:
                e.Handled = MoveSelection(-1);
                break;

            case Windows.System.VirtualKey.Down:
                e.Handled = MoveSelection(+1);
                break;
        }
    }

    // ---------------- Long-question compose window ----------------

    private ComposeWindow? _compose;

    /// <summary>Text from a compose window closed with Esc — continued the next time it opens.</summary>
    private string? _composeDraft;

    /// <summary>IME composition in progress, e.g. Korean (moving the window now breaks the composed character).</summary>
    private bool _imeComposing;

    private static bool IsShiftDown()
        => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>
    /// Long questions go to the compose window (user feedback 09-30: first prompts are long but only a short slice was visible, which was painful).
    /// Opens on: Shift+Enter · pasting multiple lines · text overflowing the input box (only once composition has finished).
    /// </summary>
    private void HookCompose()
    {
        Entry.TextCompositionStarted += (_, _) => _imeComposing = true;
        Entry.TextCompositionEnded += (_, _) =>
        {
            _imeComposing = false;
            _ui.TryEnqueue(OpenComposeIfOverflowing);
        };
        Entry.TextChanged += (_, _) => OpenComposeIfOverflowing();
        Entry.SelectionChanged += (_, _) => { if (_preview is not null) { OpenComposeIfOverflowing(); } };   // move the preview's blinking caret when the input caret moves (arrows/click), not only when the text changes

        // a single-line input box keeps only the first line of a multi-line paste → paste manually, and go to the compose window if multi-line
        Entry.Paste += async (_, e) =>
        {
            if (_mode != BarMode.Claude)
            {
                return;
            }

            e.Handled = true;
            string clip;
            try
            {
                var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                {
                    return;
                }

                clip = await content.GetTextAsync();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                return;
            }

            clip = clip.TrimEnd('\r', '\n') is var trimmed && !trimmed.Contains('\n') && !trimmed.Contains('\r') ? trimmed : clip;   // one line copied with its newline
            var start = Entry.SelectionStart;
            var merged = Entry.Text[..start] + clip + Entry.Text[(start + Entry.SelectionLength)..];
            if (clip.Contains('\n') || clip.Contains('\r'))
            {
                OpenCompose(merged);
                return;
            }

            Entry.Text = merged;
            Entry.SelectionStart = start + clip.Length;
        };
    }

    private PromptPreviewWindow? _preview;

    /// <summary>
    /// A long question overflowing the input box is shown in full above it (focus stays put so typed characters aren't lost).
    /// Left alone during IME composition; checked again once composition ends.
    /// </summary>
    private void OpenComposeIfOverflowing()
    {
        var show = _mode == BarMode.Claude && _typing && _compose is null && Entry.Text.Length >= 10 && Overflows();
        if (!show)
        {
            _preview?.Hide();
            return;
        }

        if (_imeComposing && _preview is null)
        {
            return;
        }

        if (_preview is null)
        {
            _preview = new PromptPreviewWindow();
            AppFonts.Apply(_preview, _language);
            _preview.Clicked += () =>
            {
                // Clicking the preview used to end typing and lose the question (10-03). End typing first, then open the compose box:
                // ending it afterwards (the bar's own deactivation) put the bar back in the taskbar, which took focus from the new
                // compose box and closed it at once (log 10-03).
                var text = Entry.Text;
                TaskbarHost.Log($"preview clicked: typing={_typing} len={text.Length}");
                EndTyping();
                OpenCompose(text);
            };
        }

        _preview.Show(Entry.Text, Entry.SelectionStart, PopupAnchorX, TaskbarTop());
    }

    private bool Overflows()
    {
        var probe = new Microsoft.UI.Xaml.Controls.TextBlock { Text = Entry.Text, FontSize = Entry.FontSize, FontFamily = Entry.FontFamily, CharacterSpacing = Entry.CharacterSpacing };
        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width > Entry.ActualWidth - 16;
    }

    private void OpenCompose(string text)
    {
        if (_compose is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrEmpty(_composeDraft))
        {
            text = _composeDraft;
        }

        var opening = text;
        _preview?.Hide();

        _compose = new ComposeWindow(
            () =>
            {
                // the user kept typing in the input box while the window was opening — append whatever was typed after the handed-over text
                // (with a newline in between for Shift+Enter). If it differs from the input box (paste, continued draft), keep the handed-over text as-is.
                var typed = Entry.Text;
                Entry.Text = string.Empty;   // the text moved to the compose window
                var newline = opening.EndsWith('\n');
                var basis = newline ? opening[..^1] : opening;
                return basis.Length > 0 && typed.StartsWith(basis, StringComparison.Ordinal)
                    ? basis + (newline ? "\n" : string.Empty) + typed[basis.Length..]
                    : opening;
            },
            Placeholders.For(_mode, _settings.Agent),
            submit: question =>
            {
                _composeDraft = null;
                if (!Launch(question))
                {
                    _composeDraft = question;   // not started: the next compose box opens with it again
                }
            },
            cancel: draft => _composeDraft = string.IsNullOrWhiteSpace(draft) ? null : draft);
        _compose.Closed += (_, _) =>
        {
            _compose = null;
            if (_typing && !_watchdog.Quitting)   // not while exiting: re-attaching to the taskbar then is what ReleaseForClose avoids
            {
                EndTyping();   // the bar stayed in typing mode while the compose box had the question
            }

            RenderIsland();   // back to the status display
        };
        AppFonts.Apply(_compose, _language);
        _compose.ShowAbove(PopupAnchorX, TaskbarTop());
        RenderIsland();
    }

    /// <summary>In file mode, move through results; in Claude mode, scroll through previous questions.</summary>
    private bool MoveSelection(int delta)
    {
        if (_mode == BarMode.Files)
        {
            return MoveResultSelection(delta);
        }

        var text = _history.Move(delta, Entry.Text);
        if (text is null)
        {
            return true;      // no history: do nothing (just stop the caret from moving)
        }

        // inserting history is not "typing" — if Reset ran here, ↑ would never move past the most recent entry (measured on PC 09-29)
        _historyText = text;
        Entry.Text = text;
        Entry.SelectionStart = text.Length;
        return true;
    }

    private void OnEnter(bool reveal)
    {
        // "Replace the running timer?" is showing in the input box: Enter on it confirms (typing something else drops the question)
        if (_pendingTimer is { } pending)
        {
            _pendingTimer = null;
            if (Entry.Text.Trim().Length == 0)
            {
                _feed.ReplaceTimer(pending);
                EndTyping();
                return;
            }

            Entry.PlaceholderText = Placeholders.For(_mode, _settings.Agent);
        }

        if (_mode == BarMode.Files)
        {
            OpenSelectedFile(reveal);
            return;
        }

        var question = Entry.Text.Trim();

        // typing e.g. "25min" starts a timer — only when there's a unit and the whole text is a duration (so questions aren't hijacked)
        if (TimerParser.TryParse(question, out var duration))
        {
            if (StartTimer(TimerParser.ToActivity(duration, DateTimeOffset.UtcNow)))
            {
                EndTyping();
            }

            return;
        }

        if (!Launch(question))
        {
            EndTyping();   // the red notice shows on the pill (hidden while typing); ↑ brings the question back
        }
    }

    /// <summary>
    /// The agent couldn't be started: say so on the pill and leave the question where it was. It used to be replaced by the error
    /// text — the question was lost, and Enter then sent the error as a question (review 10-03).
    /// </summary>
    private void ShowLaunchFailure(string message, string question)
    {
        TaskbarHost.Log("launch failed: " + message);
        RememberQuestion(question);

        var text = _text.FormatLaunchFail(message);
        var notice = IsleBar.Core.SystemWatch.SystemNotice.Make(text, "\uE7BA", DateTimeOffset.UtcNow, urgent: true);
        notice.State = "error";   // red, like other things that need fixing
        notice.Agent = _settings.Agent;   // an agent's failure → an inform card that stays until closed (user 10-05)
        _feed.Store.Write("islebar_launch", notice);

        // The error also goes on the clipboard, so it can be pasted into a bug report (10-04) \u2014 long ones only scroll by on the pill.
        // Copied 2 s later: the clipboard's "Copied \u00B7 \u2026" notice then shows once the error has been seen, and the error comes back
        // after it. Copied at once, the two notices arrived together and the pill flipped between them (user 10-04).
        _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ => _ui.TryEnqueue(() =>
        {
            try
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // the clipboard is busy (another app holds it) \u2014 the error is still on the pill and in the log
            }
        }), TaskScheduler.Default);
    }

    /// <returns>Whether the agent was started (false: the question is kept for another try).</returns>
    private bool Launch(string question)
    {
        if (AgentLauncher.Start(_settings, question) is { } error)
        {
            ShowLaunchFailure(error, question);
            return false;
        }

        if (question.Length > 0)
        {
            RememberQuestion(question);
            // the model list check writes these to the file in the background; keep them current in memory too, or the next
            // settings save wrote the old ones back (review 10-03)
            TakeBackgroundModelLists();
        }

        EndTyping();
        return true;
    }

    /// <summary>Adds the question to the ↑ history (file and memory).</summary>
    private void RememberQuestion(string question)
    {
        if (question.Length == 0)
        {
            return;
        }

        _store.Update((IsleBarSettings s) =>
        {
            s.History = QuestionHistory.Add(s.History, question);
            return true;
        });
        // in place: an open settings window holds this same object — replacing it with a fresh load made every switch flipped
        // there afterwards change a copy nobody saved (review 10-03)
        _settings.History = QuestionHistory.Add(_settings.History, question);
        _history = new HistoryNavigator(_settings.History);
    }

    /// <summary>Tab: Claude → files → web → Claude (files skipped if file search is off).</summary>
    private void ToggleMode() => SetMode(BarModes.Next(_mode, UsableModes(), filesOn: true));

    /// <summary>Only enabled modes, in Tab order (reflects modes removed in settings and the file-search switch).</summary>
    private List<BarMode> UsableModes() => BarModes.Usable(ModeOrder(), _settings.Files, _settings.ModesOff);

    private List<BarMode> ModeOrder() => BarModes.Normalize(_settings.ModeOrder);

    /// <summary>Default mode = first in Tab order (files skipped if file search is off).</summary>
    private BarMode HomeMode() => UsableModes()[0];

    private void SetMode(BarMode mode)
    {
        _openWhenReady = null;
        if (mode == _mode)
        {
            return;
        }

        _mode = mode;
        _history.Reset();
        Entry.PlaceholderText = Placeholders.For(_mode, _settings.Agent);
        UpdateLeadIcon();
        UpdateTabHint();
        if (_mode == BarMode.Files)
        {
            ScheduleSearch();
        }
        else
        {
            CloseResults();
        }
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
            if (_typing)
            {
                CheckForeground();   // backstop for the foreground notification
            }

            FollowTaskbar();
            RefreshBesideColor();
            var items = _feed.Collect(DateTimeOffset.UtcNow);
            EndStaleHover();

            _cards.Update(items, DateTimeOffset.UtcNow);
            _shownCard = _cards.Shown;
            var next = _shownCard is { } shown ? new IslandSnapshot(shown.State, _cards.Cards.Count - 1) : IslandSnapshot.Empty;
            _snapshot = next;
            LoadAlbumArt();
            ExpandIfMessage(items);   // feed EVERY new message notice to the expanded card, not just the primary one (bursts dropped middles — user 09-30)
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
            var musicPrimary = change is IslandAnimator.Change.None && !_hovering && !_typing
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
        Interop.TaskbarHost.Log($"follow: visible={placement.Visible} {placement.X},{placement.Y} size {_width}x{_height} typing={_typing}");
        if (!placement.Visible)
        {
            // don't use AppWindow.Show/Hide on a taskbar child window (PC 09-29: Show hung and the position was never set)
            Interop.NativeMethods.ShowWindow(_taskbar.OuterWindow, Interop.NativeMethods.SW_HIDE);
            return;
        }

        Interop.NativeMethods.ShowWindow(_taskbar.OuterWindow, Interop.NativeMethods.SW_SHOWNOACTIVATE);
        Root.Background = BrushOf(TaskbarColorBeside());   // when the position changes, re-read the adjacent taskbar color too
        if (!_typing)
        {
            _taskbar.PlaceAt(placement.X, placement.Y, _width, _height);
        }
    }

    /// <summary>The big pill shows a card, typing, a dragged file or the compose box; otherwise only the small pill (TODO 7.1).</summary>
    private bool BigVisible => _typing || _compose is not null || _dropHover || _snapshot.IsActive;

    // ---------------- Small pill: task icons (E3) ----------------

    /// <summary>The order of the task icons (kept tick to tick, swapped on a click).</summary>
    private readonly TaskSlots _slots = new();

    /// <summary>Cells on the small pill now: task icons, then whether ⋯ shows. Their keys, left to right in <see cref="_slots"/> order.</summary>
    private readonly List<Card> _cellCards = [];
    private bool _dotsOn;

    /// <summary>What the cells were last built from; rebuilt only when it changes (null = rebuild).</summary>
    private string? _cellsSig;

    /// <summary>Task cells + ⋯, for the window width.</summary>
    private int TaskCellCount => _cellCards.Count + (_dotsOn ? 1 : 0);

    /// <summary>
    /// Every card the big pill isn't showing gets an icon on the small pill (temporary ones excepted), as many as fit before the
    /// taskbar's app icons (8 from them); the rest wait behind ⋯, which shows whenever any card is behind the big pill (TODO 7.9 D).
    /// Frozen while typing or a menu is open (the window doesn't change size under them).
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
        if (_typing || _menuOpen || _flying)
        {
            return false;
        }

        _slots.Update(_cards.Cards, _shownCard?.Key, _hovering);
        var cells = _slots.Cells;
        var behind = Math.Max(0, _cards.Cards.Count - 1);
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
        foreach (var (key, icon, card) in icons)
        {
            var content = CellContent(card, icon, now);
            if (_cellCards.FirstOrDefault(c => c.Key == key)?.State.Stage == "privacy")
            {
                Breathe(content);   // mic / camera in use: plainly breathing, so it isn't forgotten (user 10-07)
            }

            TaskCells.Children.Add(MakeCell(key, content));
        }

        var more = MakeCell(DotsTag, new Microsoft.UI.Xaml.Controls.FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16, Glyph = "", Foreground = BrushOf(_theme.Icon),
        });
        if (_dotsOn)
        {
            TaskCells.Children.Insert(left ? 0 : TaskCells.Children.Count, more);
        }
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

    /// <summary>
    /// The big pill's timer icon: the same turning dial as the small one (user 10-07: only the small one turned). Built again only
    /// when the timer itself changes (the pill redraws a timer every tick; the compositor turns the hand in between).
    /// </summary>
    private void ShowLeadDial(ActivityState? state, Windows.UI.Color color)
    {
        var now = DateTimeOffset.UtcNow;
        var dial = state is null ? null : TimerDial.Of(state, _shownCard?.Type == CardType.Inform, now);
        var key = dial is null || state is null ? null : $"{state.SourcePath}|{state.Due}|{state.Total}|{state.Left}|{state.T0}|{_shownCard?.Type}|{color}|{RealSearchBoxFill()}";
        if (dial is not null)
        {
            LeadIcon.Visibility = Visibility.Collapsed;
        }

        if (key == _leadDialKey)
        {
            return;
        }

        _leadDialKey = key;
        LeadDial.Children.Clear();
        if (dial is not null)
        {
            LeadDial.Children.Add(DialContent(dial, color, state!.IsPaused));
        }
    }

    private string? _leadDialKey;

    /// <summary>A slow, clear breath on the compositor: full → 30% → full every 2 s, for as long as the element lives.</summary>
    private static void Breathe(UIElement element)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
        var breath = visual.Compositor.CreateScalarKeyFrameAnimation();
        breath.InsertKeyFrame(0f, 1f);
        breath.InsertKeyFrame(0.5f, 0.3f);
        breath.InsertKeyFrame(1f, 1f);
        breath.Duration = TimeSpan.FromSeconds(2);
        breath.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", breath);
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
            box.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill } });
            if (icon.Paused)
            {
                box.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = BrushOf(Windows.UI.Color.FromArgb(148, pill.R, pill.G, pill.B)) });
                box.Children.Add(PauseBars(16, 1.8, 6, 5.4, 8.8));
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

    /// <summary>Paused: a pill-coloured disc cuts the icon's bottom-right corner and the two bars sit in it.</summary>
    private void AddPauseNotch(Microsoft.UI.Xaml.Controls.Grid box)
    {
        box.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 9, Height = 9, Fill = BrushOf(RealSearchBoxFill()), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -2, -2),
        });
        var bars = PauseBars(9, 1.5, 5, 2.2, 5.3);
        bars.HorizontalAlignment = HorizontalAlignment.Right;
        bars.VerticalAlignment = VerticalAlignment.Bottom;
        bars.Margin = new Thickness(0, 0, -2, -2);
        box.Children.Add(bars);
    }

    /// <summary>
    /// A paused song on the big pill, drawn like its small icon (user 10-07): the cover under a veil of pill colour with two bars, or
    /// without a cover a pill-coloured notch in the glyph's corner holding them.
    /// </summary>
    private void ShowLeadPause(bool on)
    {
        var cover = on && AlbumArt.Visibility == Visibility.Visible;
        var key = !on ? null : $"{cover}|{RealSearchBoxFill()}|{_theme.Foreground}";
        if (key == _leadPauseKey)
        {
            return;
        }

        _leadPauseKey = key;
        LeadPause.Children.Clear();
        if (!on)
        {
            return;
        }

        var pill = RealSearchBoxFill();
        if (cover)
        {
            // the cover is 22 centred on the 16 slot: the veil the same size, the bars scaled from the 16 icon's
            LeadPause.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 22, Height = 22, Margin = new Thickness(-3), Fill = BrushOf(Windows.UI.Color.FromArgb(148, pill.R, pill.G, pill.B)) });
            var bars = PauseBars(22, 2.4, 8.2, 7.4, 12.2);
            bars.Margin = new Thickness(-3);
            LeadPause.Children.Add(bars);
            return;
        }

        LeadPause.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 9, Height = 9, Fill = BrushOf(pill), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -2, -2),
        });
        var small = PauseBars(9, 1.5, 5, 2.2, 5.3);
        small.HorizontalAlignment = HorizontalAlignment.Right;
        small.VerticalAlignment = VerticalAlignment.Bottom;
        small.Margin = new Thickness(0, 0, -2, -2);
        LeadPause.Children.Add(small);
    }

    private string? _leadPauseKey;

    /// <summary>Two rounded bars in a <paramref name="size"/> square, at x <paramref name="x1"/> and <paramref name="x2"/>, centred vertically.</summary>
    private FrameworkElement PauseBars(double size, double width, double height, double x1, double x2)
    {
        var canvas = new Microsoft.UI.Xaml.Controls.Canvas { Width = size, Height = size, IsHitTestVisible = false };
        foreach (var x in new[] { x1, x2 })
        {
            var bar = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = width, Height = height, RadiusX = width / 2, RadiusY = width / 2, Fill = BrushOf(_theme.Foreground) };
            Microsoft.UI.Xaml.Controls.Canvas.SetLeft(bar, x);
            Microsoft.UI.Xaml.Controls.Canvas.SetTop(bar, (size - height) / 2);
            canvas.Children.Add(bar);
        }

        return canvas;
    }

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
        PillSurface.CornerRadius = new CornerRadius(r);
        SmallSurface.CornerRadius = new CornerRadius(r);
        GlassBorder.CornerRadius = new CornerRadius(r);
        GlowRing.CornerRadius = new CornerRadius(r);
        CloseHover.CornerRadius = new CornerRadius(r, 0, 0, r);   // the close zone's hover follows the pill's round left end (user 10-07)
    }

    private DateTimeOffset _lastAttachTry = DateTimeOffset.MinValue;

    /// <summary>When the right mouse button last went down on the pill (a right click must not start typing).</summary>
    private DateTimeOffset _rightPressAt = DateTimeOffset.MinValue;

    // ---------------- Scrolling title · now-playing bars ----------------

    /// <summary>Scroll speed (DIP/s) and the pause at each lap — relaxed, like music apps.</summary>
    private const double MarqueeSpeed = 30;
    private static readonly TimeSpan MarqueeHold = TimeSpan.FromSeconds(2);

    /// <summary>Gap between the two title copies (must equal IslandTextCopy's left margin in XAML so the seam is invisible).</summary>
    private const double MarqueeGap = 40;

    private string? _marqueeText;
    private bool _equalizerOn;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _meterTimer;
    private readonly AudioMeter _audioMeter = new();
    private readonly IsleBar.Core.Ui.LevelBars _levelBars = new();

    private void StopLoopBars()
    {
        _equalizerOn = false;
        foreach (var bar in new[] { Eq0, Eq1, Eq2, Eq3 })
        {
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(bar).StopAnimation("Scale");
        }
    }

    /// <summary>
    /// Bars follow the sound (setting "music bars follow the sound"): 30 times a second the four band levels from the
    /// loopback capture become heights via Core's LevelBars (fast rise, slow fall). Only runs while the music bars show.
    /// </summary>
    private void StartMeterBars()
    {
        _meterTimer = _ui.CreateTimer();
        _meterTimer.Interval = TimeSpan.FromMilliseconds(33);
        _meterTimer.Tick += (_, _) =>
        {
            var heights = _levelBars.Update(_audioMeter.Levels());
            var bars = new[] { Eq0, Eq1, Eq2, Eq3 };
            for (var i = 0; i < bars.Length; i++)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(bars[i]);
                visual.CenterPoint = new System.Numerics.Vector3((float)bars[i].Width / 2, (float)bars[i].Height / 2, 0f);
                visual.Scale = new System.Numerics.Vector3(1f, heights[i], 1f);
            }
        };
        _meterTimer.Start();
    }

    private void StopMeterBars()
    {
        _meterTimer?.Stop();
        _meterTimer = null;
    }
    private string? _equalizerTint = "none";

    /// <summary>
    /// Island text. <b>If a music title is wider than its slot, scroll it left</b> (user feedback 09-30: should loop seamlessly like Galaxy phones).
    /// Two copies of the title sit side by side and scroll endlessly by "one copy width + gap" — the second copy lands exactly where the first was,
    /// so jumping back to the start leaves no visible seam. Each lap pauses 2s when the first character is at the front.
    /// The animation runs on the compositor (Composition) — a Storyboard appeared to stop after one lap (user feedback 09-30).
    /// Everything else is truncated with "…". If the text is unchanged it doesn't restart (this is called every 250ms).
    /// </summary>
    /// <param name="suffix">Kept whole after the name, which is shortened to make room (e.g. "  +2" more downloads).</param>
    private void ShowIslandText(ActivityState state, string prefix, string name, string suffix = "")
    {
        ShowClockDigits(null);   // the timer digit slot is only for timers
        var width = AvailableTextWidth();
        ClipMarquee(width);
        var full = prefix + name;
        // Music is just playing in the background, so its title uses normal weight — only alerts (done, permission) are bold. Long all-caps titles in bold filled the slot
        // and grabbed attention (user feedback 09-30: "STUPID IN LOVE" was too loud, while the "Claude done" text weight was just right)
        _islandStrong = state.Kind != ActivityKind.Music;
        UseFontsFor(full);   // before measuring — changing the font changes the width
        var measure = IslandMeasure();

        // Long names scroll through once to show the whole thing: music titles, session names in Claude alerts.
        // (Transfers are truncated with "…" since the changing percentage would restart the scroll every time)
        var flows = state.Kind is ActivityKind.Music or ActivityKind.AgentDone or ActivityKind.AgentPermission or ActivityKind.AgentWorking or ActivityKind.Notice;
        if (flows && measure.Measure(full) > width)
        {
            _marqueePrefix = prefix;
            _marqueeName = name;
            if (_marqueeText != full)
            {
                _marqueeText = full;
                if (_flying)
                {
                    // a snapshot is about to be taken: it showed both copies unclipped, over the equalizer and past the pill
                    // (user 10-07) — rest with "…" now, the lap starts once the card has landed
                    _lapAfterFlight = true;
                    ShowMarqueeRest();
                }
                else
                {
                    StartMarquee();   // new title: one lap
                }
            }
            else if (!_marqueeRunning)
            {
                ShowMarqueeRest();   // when paused, show "…" (redo every time since color/width may have changed)
            }
        }
        else
        {
            StopMarquee();
            var (head, rest) = TextFit.FitParts(prefix, name, width - (suffix.Length > 0 ? measure.Measure(suffix) : 0), measure);
            SetStyledText(IslandText, head, rest + suffix);
        }

        IslandTextCopy.Foreground = IslandText.Foreground;

        PlaceText();
    }

    /// <summary>
    /// Up and down: the 20 line box centred in the 32 pill (6 above), moved by <see cref="_inkOffset"/> so the glyphs themselves sit
    /// in the middle — centring the line box left the text looking low (user 10-06, TODO 7.9 D).
    /// <para>Left and right (setting, user 10-07): "left" = icon at 8, text from 32. "group" = icon + 8 + text centred together on the
    /// card (the icon moves with the text). "text" = the icon stays, the text's middle on the card's middle. The card's middle is the
    /// pill's (110), or the middle of what is right of an inform card's close zone. Centring inside the text area alone (first try)
    /// put the text 10 right of the pill's middle and left the icon stranded — it didn't look centred. Text too long to fit stays
    /// left-aligned and scrolls.</para>
    /// </summary>
    private void PlaceText()
    {
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(MarqueePanel, ((BaseHeight - IslandText.LineHeight) / 2) + _inkOffset);
        double text = 0, icon = 0;
        var mode = _settings.CardTextAlign;
        if (mode is "group" or "text" && _marqueeText is null)
        {
            MarqueePanel.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = MarqueePanel.DesiredSize.Width;
            var area = AvailableTextWidth();
            var areaLeft = (CloseZone.Visibility == Visibility.Visible ? CloseZone.Width : 0) + TextStart;
            var middle = ((areaLeft - TextStart) + BaseWidth) / 2;
            var start = mode == "group"
                ? middle - ((TextStart - IconStart + width) / 2) + (TextStart - IconStart)   // where the text starts when the group is centred
                : middle - (width / 2);
            text = Math.Clamp(Math.Round((start - areaLeft) * 2) / 2, 0, Math.Max(0, area - width));   // half DIPs; never past the text area
            icon = mode == "group" ? text : 0;
        }

        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(MarqueePanel, text);
        ShiftIcon(icon);
    }

    /// <summary>Left edge of the card icon and of the card text on a plain card (TODO 7.9 D).</summary>
    private const double IconStart = 8;
    private const double TextStart = 32;

    private void ShiftIcon(double x)
    {
        if (LeadSlot.RenderTransform is not TranslateTransform move)
        {
            LeadSlot.RenderTransform = move = new TranslateTransform();
        }

        move.X = x;
    }

    /// <summary>How far to move the text so its glyphs are centred (DIP, measured by <see cref="MeasureInkAsync"/>).</summary>
    private double _inkOffset;

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
            _inkOffset = Math.Round((IslandText.LineHeight / 2 - ((inkTop + inkBottom) / 2)) * 2) / 2;
            TaskbarHost.Log($"ink: {InkProbe.FontFamily.Source} glyphs {inkTop:0.##}–{inkBottom:0.##} of {InkProbe.ActualHeight:0.##} → offset {_inkOffset}");
            PlaceText();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // async void: nothing may escape
        {
            InkProbe.Visibility = Visibility.Collapsed;
            TaskbarHost.Log("ink: measure failed — " + ex.Message);
        }
    }

    private string _marqueePrefix = string.Empty;
    private string _marqueeName = string.Empty;
    private bool _marqueeRunning;

    /// <summary>On hover, scroll the title one more lap (if it isn't already scrolling and is long enough to scroll).</summary>
    private void ReplayMarquee()
    {
        if (_marqueeText is not null && !_marqueeRunning)
        {
            StartMarquee();
        }
    }

    /// <summary>
    /// Island text is painted two ways: the key part (e.g. "42%", "Claude done", "18:42", track title) bold,
    /// the rest (e.g. "· 12s", "· session name", file name) dimmed — the same look as the desktop mockup.
    /// If the prefix contains " · ", everything before it is the key part. Without a prefix (music), the whole name is the key part.
    /// </summary>
    private void SetStyledText(Microsoft.UI.Xaml.Controls.TextBlock block, string prefix, string rest)
    {
        // "45% · 6s  " + name → bold "45%" / dimmed " · 6s · name";  "Claude done · " + name → "Claude done" / " · name"
        var head = prefix.Trim().TrimEnd('·').TrimEnd();
        string main, hint;
        if (head.Length == 0)
        {
            (main, hint) = (rest, string.Empty);   // music: the whole title is the key part
        }
        else
        {
            var dot = head.IndexOf(" · ", StringComparison.Ordinal);
            main = dot >= 0 ? head[..dot] : head;
            var parts = new List<string>();
            if (dot >= 0)
            {
                parts.Add(head[(dot + 3)..]);
            }

            if (rest.Trim().Length > 0)
            {
                parts.Add(rest.Trim());
            }

            hint = parts.Count > 0 ? " · " + string.Join(" · ", parts) : string.Empty;
        }

        block.Inlines.Clear();
        block.Inlines.Add(_islandStrong
            ? new Microsoft.UI.Xaml.Documents.Run { Text = main, FontFamily = _islandBold, FontWeight = Microsoft.UI.Text.FontWeights.Medium }
            : new Microsoft.UI.Xaml.Documents.Run { Text = main });
        if (hint.Trim().Length > 0)
        {
            block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = hint,
                Foreground = BrushOf(_theme.Hint),
            });
        }
    }

    /// <summary>
    /// Resting look: end with "…" so text isn't chopped off abruptly at the edge of the slot (user feedback 09-30: suggested using an ellipsis).
    /// </summary>
    private void ShowMarqueeRest()
    {
        var measure = IslandMeasure();
        var (head, rest) = TextFit.FitParts(_marqueePrefix, _marqueeName, AvailableTextWidth(), measure);
        SetStyledText(IslandText, head, rest);
        IslandTextCopy.Visibility = Visibility.Collapsed;
        PlaceText();
    }

    /// <summary>One lap: place two copies of the full title side by side and scroll; when done, go back to the "…" look.</summary>
    /// <summary>
    /// Timer: "Timer 18:42" — label bold, time normal weight (like "Pomodoro 18:42" in the mockup). A named timer uses its name.
    /// When finished: "Timer done".
    /// </summary>
    private void ShowTimerText(ActivityState state, DateTimeOffset now)
    {
        StopMarquee();
        ClipMarquee(AvailableTextWidth());
        var label = string.IsNullOrWhiteSpace(state.Name) ? _text.Timer : state.Name!;
        UseFontsFor(label);
        var left = TimerParser.Elapsed(state, now) ?? TimerRemaining(state, now);   // a stopwatch counts up
        var running = TimerParser.IsStopwatch(state) || (left is { } l && l > TimeSpan.Zero);
        IslandText.Inlines.Clear();
        IslandText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = label, FontFamily = _islandBold, FontWeight = Microsoft.UI.Text.FontWeights.Medium });
        if (!running)
        {
            IslandText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "  " + _text.Done });
        }
        ShowClockDigits(running ? FormatClock(left!.Value) : null);
        IslandText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        PlaceText();
    }

    private string _clockShown = string.Empty;

    /// <summary>
    /// Draws the timer digits one by one. <b>Each digit slot is fixed to the widest digit</b> — drawing them as one string
    /// made the text jiggle sideways when narrow digits like "1" appeared. <b>Only changed digits</b> roll down from above
    /// (it's a countdown, so new digits come from above). null hides them.
    /// </summary>
    private void ShowClockDigits(string? clock)
    {
        if (clock is null)
        {
            ClockDigits.Visibility = Visibility.Collapsed;
            _clockShown = string.Empty;
            return;
        }

        ClockDigits.Visibility = Visibility.Visible;
        if (clock == _clockShown && ClockDigits.Children.Count == clock.Length)
        {
            return;
        }

        var rebuild = ClockDigits.Children.Count != clock.Length;
        if (rebuild)
        {
            ClockDigits.Children.Clear();
            var digitWidth = DigitWidth();
            foreach (var c in clock)
            {
                var cell = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    FontSize = IslandText.FontSize,
                    FontFamily = IslandText.FontFamily,
                    CharacterSpacing = IslandText.CharacterSpacing,
                    LineHeight = IslandText.LineHeight,
                    LineStackingStrategy = IslandText.LineStackingStrategy,
                    Foreground = IslandText.Foreground,
                    TextAlignment = TextAlignment.Center,
                };
                if (char.IsDigit(c))
                {
                    cell.Width = digitWidth;
                }

                ClockDigits.Children.Add(cell);
            }
        }

        for (var i = 0; i < clock.Length; i++)
        {
            var cell = (Microsoft.UI.Xaml.Controls.TextBlock)ClockDigits.Children[i];
            var text = clock[i].ToString();
            cell.Foreground = IslandText.Foreground;
            if (cell.Text == text)
            {
                continue;
            }

            cell.Text = text;
            if (!rebuild)
            {
                RollIn(cell);
            }
        }

        _clockShown = clock;
    }

    /// <summary>Widest digit width (current font and size).</summary>
    private double DigitWidth()
    {
        var probe = new Microsoft.UI.Xaml.Controls.TextBlock { FontSize = IslandText.FontSize, FontFamily = IslandText.FontFamily, CharacterSpacing = IslandText.CharacterSpacing };
        double widest = 0;
        for (var d = '0'; d <= '9'; d++)
        {
            probe.Text = d.ToString();
            probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, probe.DesiredSize.Width);
        }

        return Math.Ceiling(widest);
    }

    /// <summary>One changed digit: slides down 7 from above while sharpening (0.25s).</summary>
    private static void RollIn(UIElement cell)
    {
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(cell, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(cell);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.2f, 0.8f), new System.Numerics.Vector2(0.2f, 1f));
        var move = compositor.CreateScalarKeyFrameAnimation();
        move.InsertKeyFrame(0f, -7f);
        move.InsertKeyFrame(1f, 0f, ease);
        move.Duration = TimeSpan.FromMilliseconds(250);
        visual.StartAnimation("Translation.Y", move);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(200);
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>
    /// When to light the border: finished items (Claude/Codex done, timer finished) = green, items needing an answer (permission check) = orange.
    /// Running timers, music and transfers get no border (always-on is distracting). User feedback 09-30: timers and the like should get a border too.
    /// </summary>
    private void UpdateGlow()
    {
        var card = _typing ? null : _shownCard;
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
    }

    /// <summary>The colour of a pinned card's icon: the same as its border (TODO 7.9 D).</summary>
    private Windows.UI.Color IconColorFor(Card? card) => card switch
    {
        { Pinned: true, Type: CardType.Pending } => Attention(),
        { Pinned: true, Type: CardType.Inform } when card.State.RunState == ActivityRunState.Error => Alarm(),
        { Pinned: true, Type: CardType.Inform } => _theme.Ok,
        { Pinned: true, Type: CardType.Temporary } when card.State.RunState == ActivityRunState.Error => Alarm(),
        _ => _theme.Icon,
    };


    private string? _lastMusicSig;

    /// <summary>The fields that decide how the music island looks; when unchanged tick-to-tick, the re-render is skipped.</summary>
    // The text slot's width is part of it: coming back from typing, the columns are still animating when the island first renders,
    // so the title clip was set to a mid-animation (too wide) width and — with re-renders skipped — the scrolling title ran under
    // the now-playing bars (found 10-01 right after sending a prompt).
    private string MusicRenderSig(IsleBar.Core.Island.ActivityState m)
        => string.Concat(m.SourcePath, "|", m.Name, "|", m.Msg, "|", m.Stage, "|", m.Done?.ToString(), "|", m.Total?.ToString(),
            "|", Math.Round(AvailableTextWidth()).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "|", _snapshot.ExtraCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

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
        if (_typing || !_taskbar.IsAttached || DateTimeOffset.UtcNow - _lastBesideCheck < TimeSpan.FromSeconds(3))
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
        }
        else
        {
            _besidePending = color;
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

    private void StartMarquee()
    {
        SetStyledText(IslandText, _marqueePrefix, _marqueeName);
        SetStyledText(IslandTextCopy, _marqueePrefix, _marqueeName);
        IslandTextCopy.Visibility = Visibility.Visible;
        IslandText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        RunMarquee(IslandText.DesiredSize.Width + MarqueeGap);
    }

    /// <summary>
    /// While scrolling, softly cover both ends of the text slot with the pill background (so text isn't chopped at the edge, like music apps).
    /// Off when resting, since it ends in "…" then.
    /// </summary>
    private void SetMarqueeFade(bool on)
    {
        FadeLeft.Visibility = FadeRight.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on || PillSurface.Background is not SolidColorBrush fill)
        {
            return;
        }

        var solid = fill.Color;
        var clear = Windows.UI.Color.FromArgb(0, solid.R, solid.G, solid.B);
        FadeLeft.Fill = Gradient(solid, clear);
        FadeRight.Fill = Gradient(clear, solid);
    }

    private static LinearGradientBrush Gradient(Windows.UI.Color from, Windows.UI.Color to)
    {
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0.5), EndPoint = new Windows.Foundation.Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop { Color = from, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = to, Offset = 1 });
        return brush;
    }

    private int _marqueeRun;

    private void RunMarquee(double distance)
    {
        // Number each lap — stopping an earlier lap (e.g. a scrolling music title) delivered its completion signal late
        // and stopped the new lap (a done alert) instead (measured on PC 09-30). Only react to completion of our own number.
        var myRun = ++_marqueeRun;
        _marqueeRunning = true;
        SetMarqueeFade(true);
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(MarqueePanel, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MarqueePanel);
        var compositor = visual.Compositor;
        visual.StopAnimation("Translation.X");
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);

        // One lap = pause (first character at the front) → scroll → ends when the next copy's first character reaches the front.
        // The end and start positions look identical, so the jump back is invisible and the pause follows immediately
        // (user feedback 09-30: Galaxy scrolls and stops when the first character comes back to the front).
        var run = TimeSpan.FromSeconds(distance / MarqueeSpeed);
        var hold = _snapshot.Primary?.Kind == ActivityKind.Notice ? TimeSpan.FromSeconds(1) : MarqueeHold;   // short notices can't wait 2 s
        var total = hold + run;
        var slide = compositor.CreateScalarKeyFrameAnimation();
        slide.InsertKeyFrame(0f, 0f);
        slide.InsertKeyFrame((float)(hold / total), 0f);
        slide.InsertKeyFrame(1f, (float)-distance, compositor.CreateLinearEasingFunction());
        slide.Duration = total;
        // Only one lap — scroll once when a track first appears, then stay still once the first character is back at the front (Galaxy style,
        // user feedback 09-30: asked why it kept moving). Hovering plays one more lap (ReplayMarquee).
        slide.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Count;
        slide.IterationCount = 1;
        // Left fade only while the text moves — leaving it on while still (first 2s and at the end) covered the first character
        // and made it look clipped (user feedback 09-30: the first character was still cut off when a title first appeared).
        var fadeVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(FadeLeft);
        var edge = (float)Math.Min(0.4 / total.TotalSeconds, 0.05);
        var holdEnd = (float)(hold / total);
        var fadeIn = compositor.CreateScalarKeyFrameAnimation();
        fadeIn.InsertKeyFrame(0f, 0f);
        fadeIn.InsertKeyFrame(holdEnd, 0f);
        fadeIn.InsertKeyFrame(Math.Min(holdEnd + edge, 1f - edge), 1f);
        fadeIn.InsertKeyFrame(1f - edge, 1f);
        fadeIn.InsertKeyFrame(1f, 0f);
        fadeIn.Duration = total;
        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        visual.StartAnimation("Translation.X", slide);
        fadeVisual.StartAnimation("Opacity", fadeIn);
        batch.End();
        batch.Completed += (_, _) => _ui.TryEnqueue(() =>
        {
            if (myRun != _marqueeRun || !_marqueeRunning || _marqueeText is null)
            {
                return;
            }

            _marqueeRunning = false;
            SetMarqueeFade(false);
            visual.StopAnimation("Translation.X");
            visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
            ShowMarqueeRest();   // lap finished → "…"
        });
    }

    /// <summary>A new title's lap was held back while a transition flew; it runs once the card has landed.</summary>
    private bool _lapAfterFlight;

    /// <summary>
    /// Stops a lap mid-scroll and shows the "…" look, keeping the title (a snapshot doesn't see the scroll offset nor the clip:
    /// it showed the whole unscrolled title running past the slot — user 10-07).
    /// </summary>
    private void RestMarquee()
    {
        if (!_marqueeRunning)
        {
            return;
        }

        _marqueeRun++;   // the lap's own end mustn't act later
        _marqueeRunning = false;
        SetMarqueeFade(false);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MarqueePanel);
        visual.StopAnimation("Translation.X");
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
        ShowMarqueeRest();
    }

    private void StopMarquee()
    {
        ShowClockDigits(null);
        if (_marqueeText is null)
        {
            return;
        }

        _marqueeText = null;
        _marqueeRunning = false;
        SetMarqueeFade(false);
        IslandTextCopy.Visibility = Visibility.Collapsed;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MarqueePanel);
        visual.StopAnimation("Translation.X");
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
    }

    /// <summary>Album art on the big pill (22) when the shown card is music and its cover is loaded.</summary>
    private void ShowAlbumArt(bool on)
    {
        var cover = on ? CoverOf(_shownCard) : null;
        _albumTint = cover?.Tint;
        AlbumArt.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
        if (cover is { } c)
        {
            if (!ReferenceEquals(AlbumArtBrush.ImageSource, c.Image))
            {
                AlbumArtBrush.ImageSource = c.Image;
            }

            LeadIcon.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Decoded covers by the player's art key, shared by the big pill and the small pill's music cells (one per player, user 10-07).
    /// Each keeps our own copy of the art stream alive: XAML decodes the image later, on a worker thread, straight from that stream;
    /// the player's own stream can be closed before that — by leaving the method, or on a track change — and decoding a closed stream
    /// fail-fasts the whole bar (RO_E_CLOSED 0x80000013 in Microsoft.UI.Xaml, dump 10-01 23:33).
    /// </summary>
    private readonly Dictionary<string, (Microsoft.UI.Xaml.Media.Imaging.BitmapImage Image, Windows.UI.Color? Tint, Windows.Storage.Streams.IRandomAccessStream Stream)> _covers = new(StringComparer.Ordinal);

    /// <summary>Covers being read, and ones that failed (not retried every tick; the note shows instead).</summary>
    private readonly HashSet<string> _coversBusy = new(StringComparer.Ordinal);

    /// <summary>The loaded cover of a music card, or null (none, not loaded yet, failed).</summary>
    private (Microsoft.UI.Xaml.Media.Imaging.BitmapImage Image, Windows.UI.Color? Tint, Windows.Storage.Streams.IRandomAccessStream Stream)? CoverOf(Card? card)
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
            var tint = await PickAlbumTintAsync(art);
            _covers[key] = (image, tint, owned);
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

    /// <summary>Dominant color of the shown album (null if none or grayscale → normal color).</summary>
    private Windows.UI.Color? _albumTint;

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
            var light = PillSurface.Background is SolidColorBrush fill && ((fill.Color.R * 0.3) + (fill.Color.G * 0.59) + (fill.Color.B * 0.11)) > 128;
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
    /// Claude/Codex alerts (done, permission) show that agent's mark (spark / Codex mark from the user's PC) instead of ✓/🔔 —
    /// with a small ✓ next to ⌄, two symbols side by side looked flimsy (user feedback 09-30). Done/permission is signalled by the border and text color.
    /// </summary>
    private void ShowAgentMark(ActivityState state)
    {
        if (state.Kind is not (ActivityKind.AgentDone or ActivityKind.AgentPermission or ActivityKind.AgentWorking)
            && !(state.Kind == ActivityKind.Notice && state.Agent is not null))
        {
            return;   // every stage of an agent's card shows its logo (user 10-07), its notices too
        }

        var codex = state.AgentLabel == "Codex";
        if ((codex ? _codexBrand : _claudeGlyph) is null)
        {
            return;   // no mark on this PC → keep ✓/🔔
        }

        ClaudeGlyphImage.Source = _claudeGlyph;   // the makers' colours here: the ring tells the stage (user 10-07)
        CodexGlyph.Source = _codexBrand;

        ClaudeGlyphImage.Visibility = codex ? Visibility.Collapsed : Visibility.Visible;
        CodexGlyph.Visibility = codex ? Visibility.Visible : Visibility.Collapsed;
        LeadIcon.Visibility = Visibility.Collapsed;
    }

    /// <summary>Turns the now-playing bars on/off. The animation is built once when turned on (the compositor runs it).</summary>
    private void SetEqualizer(bool on)
    {
        Equalizer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var fillKey = $"{_albumTint}|{_theme.Icon}";   // repaint only when the color changes (this is called every 250ms)
        if (on && _equalizerTint != fillKey)
        {
            // the brushes are built only here — built on every call before, 4 times a second, and thrown away (review 10-03)
            Brush brush = BrushOf(_theme.Icon);
            if (_albumTint is { } tint)
            {
                var top = Windows.UI.Color.FromArgb(255, (byte)Math.Min(255, tint.R + 40), (byte)Math.Min(255, tint.G + 40), (byte)Math.Min(255, tint.B + 40));
                var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1) };
                gradient.GradientStops.Add(new GradientStop { Color = top, Offset = 0 });
                gradient.GradientStops.Add(new GradientStop { Color = tint, Offset = 1 });
                brush = gradient;
            }

            _equalizerTint = fillKey;
            Eq0.Fill = Eq1.Fill = Eq2.Fill = Eq3.Fill = brush;
        }

        // Follow the real sound (setting) — a 30 Hz timer reads the speaker meter and sets the bar heights
        var meter = on && _settings.AudioBars;
        if (meter != (_meterTimer is not null))
        {
            if (meter)
            {
                StopLoopBars();
                StartMeterBars();
            }
            else
            {
                StopMeterBars();
            }
        }

        if (meter)
        {
            return;
        }

        if (on && !_equalizerOn)
        {
            _equalizerOn = true;

            // The compositor (Composition) animates the bar sizes — a XAML Storyboard woke the UI and render threads every frame
            // and used 3–6% CPU for four bars (measured 09-30: music island 6–9% → 3% with bars hidden). Compositor animations don't wake app code.
            var patterns = new[]
            {
                (Values: new[] { 0.35f, 1.0f, 0.5f, 0.85f, 0.3f }, Seconds: 1.1),
                (Values: new[] { 0.9f, 0.4f, 1.0f, 0.55f, 0.8f }, Seconds: 0.9),
                (Values: new[] { 0.5f, 0.8f, 0.3f, 1.0f, 0.45f }, Seconds: 1.3),
                (Values: new[] { 0.75f, 0.35f, 0.9f, 0.4f, 1.0f }, Seconds: 1.0),
            };
            var bars = new[] { Eq0, Eq1, Eq2, Eq3 };
            for (var i = 0; i < bars.Length; i++)
            {
                var bar = bars[i];
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(bar);
                var compositor = visual.Compositor;
                visual.CenterPoint = new System.Numerics.Vector3((float)bar.Width / 2, (float)bar.Height / 2, 0f);   // centered pivot (09-30 G2)
                var sine = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.37f, 0f), new System.Numerics.Vector2(0.63f, 1f));
                var animation = compositor.CreateVector3KeyFrameAnimation();
                var values = patterns[i].Values;
                for (var k = 0; k <= values.Length; k++)
                {
                    var v = values[k % values.Length];
                    animation.InsertKeyFrame((float)k / values.Length, new System.Numerics.Vector3(1f, v, 1f), sine);
                }

                animation.Duration = TimeSpan.FromSeconds(patterns[i].Seconds);
                animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
                animation.DelayTime = TimeSpan.FromMilliseconds(120 * i);   // stagger each bar — so they don't pump as one block
                visual.StartAnimation("Scale", animation);
            }
        }
        else if (!on && _equalizerOn)
        {
            StopLoopBars();
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
        if (_typing || DateTimeOffset.UtcNow - _lastAttachTry < wait)
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
            if (_attachFailures >= AttachFailuresBeforeRestart && _compose is null && !_menuOpen && RestartForAttachAllowed())
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
        _bigCard = !_typing && _compose is null && !_dropHover && _snapshot.IsActive ? _shownCard : null;
        LayoutPills();
        _animator.Breathe(_snapshot.IsActive && !_typing && _snapshot.Primary?.Kind == ActivityKind.AgentWorking);
        ShowCloseZone();
        ApplyStyle();
        UpdateProgressLine();
        GlowRing.Visibility = _typing ? Visibility.Collapsed : Visibility.Visible;   // the island is hidden while typing, so the border goes too
        UpdateGlow();
        if (RenderDropHint())
        {
            return;
        }

        // While the compose window is open the pill looks like the input box it came from — it used to snap back to an unrelated
        // status ("Claude working · …") under the window you were typing in (design check 10-01)
        if (!_snapshot.IsActive || _typing || _compose is not null)
        {
            SetEqualizer(false);
            ShowAlbumArt(false);
            ShowLeadPause(false);
            ShowLeadDial(null, _theme.Icon);
            StopMarquee();
            IslandContent.Visibility = Visibility.Collapsed;
            Entry.Visibility = Visibility.Visible;
            UpdateLeadIcon();
            ShiftIcon(0);   // typing: the icon is the agent's mark, back at its place
            return;
        }

        var state = _snapshot.Primary!;
        RenderIslandState(state);
    }

    /// <summary>
    /// While a file is dragged over the pill, show it there instead of only the blue border (user 10-01): with a drop command set,
    /// just a file icon and the file's name (whatever the command does, you're handing it a file — so no wording that would be wrong
    /// for someone else's command); with none, "Attach to prompt · name". Returns false when not dragging.
    /// </summary>
    private bool RenderDropHint()
    {
        if (!_dropHover || _typing)
        {
            return false;
        }

        var hasCommand = !string.IsNullOrWhiteSpace(_settings.DropCommand);
        RenderIslandState(new ActivityState
        {
            RawKind = ActivityState.KindNotice,
            Title = hasCommand ? _dropNames ?? string.Empty : _text.DropToAttach,
            Name = hasCommand ? null : _dropNames,
            Glyph = hasCommand ? "\uE8A5" : "\uE723",   // document / attach 📎
        });
        return true;
    }

    private void RenderIslandState(ActivityState state)
    {
        Entry.Visibility = Visibility.Collapsed;
        IslandContent.Visibility = Visibility.Visible;

        UpdateLeadIcon();
        LeadIcon.Glyph = state.IsPaused ? "\uE769" : GlyphFor(state);   // paused timer = pause icon
        LeadIcon.Foreground = BrushOf(_dropHover ? _theme.Icon : IconColorFor(_shownCard));
        ShowLeadDial(_dropHover ? null : state, IconColorFor(_shownCard));
        // music: 8 before the now-playing bars, else 12 to the pill's edge (TODO 7.9 D)
        IslandContent.Margin = new Thickness(0, 0, state.Kind == ActivityKind.Music && state.Stage != "paused" ? 8 : 12, 0);
        // compressing: the document outline gets zipper teeth (the drop hint uses the same outline without them)
        ZipTeeth.Opacity = state.Stage == "copy" && state.RunState == ActivityRunState.Running
                           && state.Glyph == Media.FileOperationWatcher.CompressGlyph ? 1 : 0;
        ShowAgentMark(state);
        ShowAlbumArt(state.Kind == ActivityKind.Music);
        ShowLeadPause(state.Kind == ActivityKind.Music && state.Stage == "paused");
        SetEqualizer(state.Kind == ActivityKind.Music && state.Stage != "paused");
        var now = DateTimeOffset.UtcNow;
        // The done-alert hook only carries the session name (no folder name — user feedback 09-30). Without a name, just "Claude done"
        var name = state.Name;
        if (state.Kind == ActivityKind.Timer)
        {
            ShowTimerText(state, now);
        }
        else if (state.Kind == ActivityKind.Notice)
        {
            // brief notice: "Charging · 78%" — title is the key part (bold), description dimmed
            var tail = string.Join(" · ", new[] { state.Msg, state.Name }.Where(t => !string.IsNullOrWhiteSpace(t)));
            ShowIslandText(state, (state.Title ?? string.Empty) + (tail.Length == 0 ? string.Empty : " · "), tail);
        }
        else if (state.Kind == ActivityKind.Music)
        {
            // music: title is the key part (bold), artist dimmed — "title · artist"
            ShowIslandText(state, state.Msg is null ? string.Empty : (name ?? string.Empty) + " · ", state.Msg ?? name ?? string.Empty);
        }
        else
        {
            // a download whose name the browser hasn't given yet ("Unconfirmed 123"), or Explorer's progress window (it names no file)
            name ??= state.Kind != ActivityKind.Transfer ? null : state.Stage == "copy" ? _text.FileTransfer : _text.Downloading;
            ShowIslandText(state, PrefixFor(state, now), name ?? string.Empty, state.MoreCount > 0 ? $"  +{state.MoreCount}" : string.Empty);
        }
        IslandText.Foreground = BrushOf(ForegroundFor(state));

    }

    /// <summary>
    /// Progress: a 2 high track on the bottom edge, the done part filled (TODO 7.9 D). Timers use it too (the ring on the right
    /// is gone: the text area runs to 208). Unknown size: a short piece flows along the track.
    /// </summary>
    private void UpdateProgressLine()
    {
        var state = _snapshot.Primary;
        var now = DateTimeOffset.UtcNow;
        double? fraction = state is null ? null
            : state.Kind == ActivityKind.Timer ? (TimerRemaining(state, now) > TimeSpan.Zero ? TimerFraction(state, now) : null)
            : state.Fraction;
        var flowing = state is { Kind: ActivityKind.Transfer, RunState: ActivityRunState.Running }
                      && (state.IsIndeterminate || (state.Stage is "download" or "copy" && state.Fraction is null));
        ProgressTrackBar.Visibility = fraction is not null || flowing ? Visibility.Visible : Visibility.Collapsed;
        if (state is null || (fraction is null && !flowing))
        {
            ProgressLine.Width = 0;
            StopFlow();
            return;
        }

        var light = ThemeColors.FromSystem().Light;
        ProgressTrackBar.Fill = BrushOf(light ? Windows.UI.Color.FromArgb(255, 0xD6, 0xD6, 0xD6) : Windows.UI.Color.FromArgb(255, 0x46, 0x46, 0x46));
        var fg = ForegroundFor(state);
        ProgressLine.Background = BrushOf(state.RunState switch
        {
            ActivityRunState.Error => _theme.Bad,
            _ => Windows.UI.Color.FromArgb(217, fg.R, fg.G, fg.B),   // the text colour at 85%
        });
        if (flowing)
        {
            ProgressLine.Width = 0;
            StartFlow(ProgressLine.Background, ProgressInset, ProgressLength);
            return;
        }

        StopFlow();
        ProgressLine.Width = Math.Max(1, ProgressLength * fraction!.Value);
    }

    private const double FlowSeconds = 1.6;   // one pass of the flowing piece across the track
    private double _flowWidth = -1;   // track width the running animation was built for; -1 = stopped

    /// <summary>
    /// The flowing bar runs on the compositor — it used to be moved from the 250ms tick, only 4 frames a second, so it stuttered.
    /// Called every tick: only the color and position are refreshed; the animation restarts only when the track width changes.
    /// </summary>
    private void StartFlow(Brush brush, double left, double track)
    {
        FlowPiece.Background = brush;
        FlowTrack.Margin = new Thickness(left, 0, 0, 0);
        FlowTrack.Visibility = Visibility.Visible;
        if (Math.Abs(track - _flowWidth) < 0.5)
        {
            return;
        }

        _flowWidth = track;
        var piece = track / 4;
        FlowTrack.Width = track;
        FlowTrack.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, track, 2) };   // the piece slides in and out at the ends
        FlowPiece.Width = piece;

        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(FlowPiece, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(FlowPiece);
        var compositor = visual.Compositor;
        var linear = compositor.CreateLinearEasingFunction();
        var slide = compositor.CreateScalarKeyFrameAnimation();
        slide.InsertKeyFrame(0f, (float)-piece, linear);
        slide.InsertKeyFrame(1f, (float)track, linear);
        slide.Duration = TimeSpan.FromSeconds(FlowSeconds);
        slide.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation("Translation.X", slide);
    }

    private void StopFlow()
    {
        if (_flowWidth < 0)
        {
            return;
        }

        _flowWidth = -1;
        FlowTrack.Visibility = Visibility.Collapsed;
        try { Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(FlowPiece).StopAnimation("Translation.X"); } catch (ArgumentException) { }
    }

    /// <summary>Prefix text. Differs by kind, and its length varies by language and progress, so it's included when truncating the name.</summary>
    private string PrefixFor(ActivityState state, DateTimeOffset now) => state.Kind switch
    {
        ActivityKind.Timer => TimerRemaining(state, now) is { } left && left > TimeSpan.Zero
            ? $"{FormatClock(left)}  "   // paused shows as the left ⏸ icon + dimmed text (no emoji, it renders as a blue box)
            : $"{_text.Done}  ",
        ActivityKind.Music => "",   // paused shows as the left ⏸ icon + dimmed text
        ActivityKind.AgentDone => string.IsNullOrEmpty(state.Name)
            ? $"{state.AgentLabel} {_text.AgentDone}"
            : $"{state.AgentLabel} {_text.AgentDone} · ",   // no emoji, to avoid clashing with the left ✓ icon
        // "Claude needs you · name" — the orange border alone carried the meaning since the left icon became the agent mark
        // (the 🔔 used to say it); working and done both name their state, so this one does too (design check 10-01)
        ActivityKind.AgentPermission => string.IsNullOrEmpty(state.Name)
            ? $"{state.AgentLabel} {_text.NeedsAnswer}"
            : $"{state.AgentLabel} {_text.NeedsAnswer} · ",
        ActivityKind.AgentWorking => string.IsNullOrEmpty(state.Name)
            ? $"{state.AgentLabel} {_text.Working}"
            : $"{state.AgentLabel} {_text.Working} · ",
        _ => state.RunState switch
        {
            ActivityRunState.Done => $"{_text.Done}  ",
            ActivityRunState.Error => $"{_text.Fail}  ",
            _ when state.IsPausedTransfer => $"{_text.Paused}  ",
            // browser download: speed (summed over all of them); the bar shows how far, when the total is known. The amount
            // received said little without a total, so the first second just shows the name
            _ when state.Stage is "download" or "copy" => state.Speed is { } speed ? $"{FormatBytes((long)speed)}/s  " : "",
            _ when state.IsIndeterminate => $"{(state.IsFinishing ? _text.Finishing : _text.Prep)}  ",
            _ => $"{state.Fraction * 100:0}%{TransferEta(state, now)}  ",
        },
    };

    /// <summary>"820 KB" · "12.3 MB" · "1.24 GB".</summary>
    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
    };

    private static TimeSpan? TimerRemaining(ActivityState state, DateTimeOffset now) => TimerParser.Remaining(state, now);

    private string? _speedPath;
    private readonly List<(DateTimeOffset At, long Done)> _speedSamples = [];

    /// <summary>
    /// Transfer remaining time " · 1m 20s". Speed is measured from the amount moved in the last 5 seconds (same as the Python version).
    /// Empty text if the speed isn't known yet.
    /// </summary>
    private string TransferEta(ActivityState state, DateTimeOffset now)
    {
        if (state.Done is not { } done || state.Total is not > 0)
        {
            return string.Empty;
        }

        if (state.SourcePath != _speedPath)
        {
            _speedPath = state.SourcePath;
            _speedSamples.Clear();
        }

        if (_speedSamples.Count == 0 || _speedSamples[^1].Done != done || now - _speedSamples[^1].At > TimeSpan.FromSeconds(1))
        {
            _speedSamples.Add((now, done));
        }

        _speedSamples.RemoveAll(sample => now - sample.At > TimeSpan.FromSeconds(5));
        if (_speedSamples.Count < 2)
        {
            return string.Empty;
        }

        var seconds = (_speedSamples[^1].At - _speedSamples[0].At).TotalSeconds;
        var speed = seconds > 0 ? (_speedSamples[^1].Done - _speedSamples[0].Done) / seconds : 0;
        return speed > 0 ? $" · {_text.FormatDuration((state.Total.Value - done) / speed)}" : string.Empty;
    }

    /// <summary>Timer bar: fraction of time elapsed (stops in place when paused).</summary>
    private static double? TimerFraction(ActivityState state, DateTimeOffset now) => TimerParser.Fraction(state, now);

    /// <summary>Remaining time "12:34" or "1:02:03".</summary>
    private static string FormatClock(TimeSpan left)
    {
        var total = (int)Math.Ceiling(left.TotalSeconds);
        return total >= 3600
            ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
    }

    /// <summary>Left icon. The text uses no emoji (blue boxes, overlaps), so the icon conveys kind, direction and result.</summary>
    private static string GlyphFor(ActivityState state) => state.Kind switch
    {
        ActivityKind.AgentPermission => "\uEA8F",                       // bell
        ActivityKind.Error => "\uEA39",                                 // error
        ActivityKind.Transfer => state.RunState switch
        {
            ActivityRunState.Done => "\uE73E",                          // ✓
            ActivityRunState.Error => "\uEA39",
            _ when state.IsPausedTransfer => "\uE769",                  // pause (download stopped growing)
            _ when state.Stage == "copy" => state.Glyph ?? "\uE8C8",     // Explorer: copy / unzip / compress, chosen by the watcher
            _ => state.Direction == TransferDirection.Incoming ? "\uE896" : "\uE898",   // receive ↓ / send ↑
        },
        ActivityKind.AgentDone => "\uE73E",
        ActivityKind.Timer => "\uE916",                                 // stopwatch
        ActivityKind.Notice => state.Glyph ?? "\uE7E7",                   // icon chosen by the notice (bell if none)
        ActivityKind.Music => state.Stage == "paused" ? "\uE769" : "\uE8D6",
        ActivityKind.AgentWorking => TaskIcons.Chat,   // same as its small icon (it was the magnifier \u2014 a card flying down changed face)
        _ => "\uE721",
    };

    /// <summary>Card text: the normal colour, dimmed when paused — the card's type shows in its icon and border (TODO 7.9 D).</summary>
    private Windows.UI.Color ForegroundFor(ActivityState state)
        => state.IsPaused || state.IsPausedTransfer || (state.Kind == ActivityKind.Music && state.Stage == "paused") ? _theme.Hint : _theme.Foreground;

    /// <summary>Width of the card text (DIP): the text column, 176 on a plain card (108 beside the close zone).</summary>
    private double AvailableTextWidth()
        => IslandContent.ActualWidth > 0 ? IslandContent.ActualWidth : CardTextWidth;

    /// <summary>Text from x 32 to 208 on a plain card (TODO 7.9 D).</summary>
    private const double CardTextWidth = 176;

    /// <summary>The progress track: 16 in from each end of the big pill, 188 long (TODO 7.9 D).</summary>
    private const double ProgressInset = 16;
    private const double ProgressLength = BaseWidth - (2 * ProgressInset);

    // ---------------- Appearance ----------------

    /// <summary>Font for the bold part of the island text (track title, "42%", etc.) — set by UseFontsFor.</summary>
    private FontFamily _islandBold = new("Segoe UI");

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

    /// <summary>Clips the text area to <paramref name="width"/> — reusing the clip (a new geometry on every draw before).</summary>
    private void ClipMarquee(double width)
    {
        var rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, width), Math.Max(1, MarqueeHost.ActualHeight));
        if (MarqueeHost.Clip is RectangleGeometry clip)
        {
            if (clip.Rect != rect)
            {
                clip.Rect = rect;
            }
        }
        else
        {
            MarqueeHost.Clip = new RectangleGeometry { Rect = rect };
        }
    }

    private TextBlockMeasure? _measure;
    private FontFamily? _measureBold;

    /// <summary>The pill text's measurer, kept while the bold font stays the same (one per draw before — review 10-03).</summary>
    private TextBlockMeasure IslandMeasure()
    {
        var bold = _islandStrong ? _islandBold : null;
        if (_measure is null || !ReferenceEquals(_measureBold, bold))
        {
            (_measure, _measureBold) = (new TextBlockMeasure(IslandText, bold), bold);
        }

        return _measure;
    }

    /// <summary>Whether to bold the key part of the island text (alerts, progress) — music uses normal weight.</summary>
    private bool _islandStrong = true;

    /// <summary>Island text font, matched to the characters in the text (Latin, Hangul, kana) — UiFonts.Stack.</summary>
    private void UseFontsFor(string text)
    {
        (var family, _islandBold) = AppFonts.For(text, _language);
        if (!ReferenceEquals(IslandText.FontFamily, family))
        {
            IslandText.FontFamily = IslandTextCopy.FontFamily = family;   // WinUI's Grid has no FontFamily — set it on the text elements directly
        }
    }

    private void ApplyFonts()
    {
        UseFontsFor(string.Empty);
        // the input box uses Pretendard too (ImeFont handles the IME composition text separately)
        Entry.FontFamily = AppFonts.For(null, _language).Normal;
        if (_results is not null)
        {
            AppFonts.Apply(_results, _language);
        }

        if (_preview is not null)
        {
            AppFonts.Apply(_preview, _language);   // Korean / Japanese / Chinese glyph shapes differ (review 10-03)
        }

        Entry.PlaceholderText = Placeholders.For(_mode, _settings.Agent);
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
        var cli = ClaudeExecutable.Resolve(AgentProfiles.Get(AgentKind.Claude));
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
        SetMarqueeFade(_marqueeRunning);   // the edge fades of a scrolling title keep the old pill colour otherwise (grey boxes)
        UpdateLeadIcon();
        RenderIsland();
        foreach (var delay in new[] { 0.8, 2.0, 4.0, 7.0 })
        {
            _ = Task.Delay(TimeSpan.FromSeconds(delay)).ContinueWith(_ => _ui.TryEnqueue(() =>
            {
                ApplyTheme();
                ApplyStyle();
                SetMarqueeFade(_marqueeRunning);
                RenderIsland();
            }), TaskScheduler.Default);
        }
    }

    private void ApplyTheme()
    {
        var background = RealSearchBoxFill();
        _theme = ThemeColors.ForActualBackground(background, ThemeColors.AccentColor());
        PillSurface.Background = BrushOf(background);
        Root.Background = BrushOf(TaskbarColorBeside());   // outside the pill (the stair-steps of the clip edge sit on this color and become invisible)
        LeadIcon.Foreground = BrushOf(_theme.Icon);
        Entry.Foreground = BrushOf(_theme.Foreground);
        BallRing.Stroke = BrushOf(_theme.Icon);
        CloseLabel.Text = _text.CloseCard;
        CloseLabel.Foreground = BrushOf(_theme.Foreground);
        CloseRule.Fill = BrushOf(ThemeColors.FromSystem().Light ? Windows.UI.Color.FromArgb(255, 0xD6, 0xD6, 0xD6) : Windows.UI.Color.FromArgb(255, 0x46, 0x46, 0x46));
        SmallRule.Fill = CloseRule.Fill;
        BallHover.Fill = BrushOf(HoverColor());
        CloseHover.Background = BrushOf(HoverColor());
        PaintFill();
    }

    /// <summary>
    /// The real search box background color. To decide in phase 0: sample it from a screen capture, or
    /// use theme defaults (light: F2F6FC / dark: white at 6% alpha).
    /// The Python version captured behind the window and used the median of the text-free top row.
    /// </summary>
    private Windows.UI.Color RealSearchBoxFill()
        => ThemeColors.FromSystem().Light
            ? Windows.UI.Color.FromArgb(255, 242, 246, 252)
            : Windows.UI.Color.FromArgb(255, 44, 44, 44);

    private bool _hovering;


    /// <summary>
    /// Apply preferences: re-apply colors the moment the black background toggles (text color follows actual background brightness);
    /// the glow border uses the album color for music, otherwise a faint color matching the background. Safe to call on every draw.
    /// </summary>
    private void ApplyStyle()
    {
        var glass = _settings.StyleGlass && !_typing;
        GlassEdge.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
        if (!glass)
        {
            return;
        }

        var dark = !(PillSurface.Background is SolidColorBrush fill && ((fill.Color.R * 0.3) + (fill.Color.G * 0.59) + (fill.Color.B * 0.11)) > 128);
        var edge = _snapshot.IsActive && _snapshot.Primary?.Kind == ActivityKind.Music && _albumTint is { } tint
            ? Windows.UI.Color.FromArgb(140, tint.R, tint.G, tint.B)
            : dark ? Windows.UI.Color.FromArgb(26, 255, 255, 255) : Windows.UI.Color.FromArgb(22, 0, 0, 0);
        GlassBorder.BorderBrush = BrushOf(edge);
        GlassShine.Visibility = dark ? Visibility.Visible : Visibility.Collapsed;   // the white shine line isn't visible on light backgrounds
    }

    /// <summary>Pill background: normal / hovered or typing (slightly brighter — same response as the real search box, Python version's LOOK).</summary>
    private void PaintFill()
    {
        var rest = RealSearchBoxFill();
        var lift = _hoverBig || _typing;
        var light = ThemeColors.FromSystem().Light;
        PillSurface.Background = BrushOf(!lift ? rest
            : light ? Blend(rest, Windows.UI.Color.FromArgb(255, 255, 255, 255), 0.55)
            : Blend(rest, Windows.UI.Color.FromArgb(255, 255, 255, 255), 0.05));
        SmallSurface.Background = BrushOf(rest);   // the small pill lifts only its circle cell (BallHover)
        if (_marqueeRunning)
        {
            SetMarqueeFade(true);   // the scrolling text's end fades are the pill colour: repaint them when it lifts (hover) — they showed as dark blocks (user 10-07)
        }

        PaintEdge();
    }

    /// <summary>Thin line around the pill edge (like the real search box border) — a faint line matched to background brightness.</summary>
    private void PaintEdge()
    {
        var light = PillSurface.Background is SolidColorBrush fill && ((fill.Color.R * 0.3) + (fill.Color.G * 0.59) + (fill.Color.B * 0.11)) > 128;
        PillSurface.BorderBrush = BrushOf(light
            ? Windows.UI.Color.FromArgb(34, 0, 0, 40)
            : Windows.UI.Color.FromArgb(30, 255, 255, 255));
        SmallSurface.BorderBrush = PillSurface.BorderBrush;
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
            ReplayMarquee();
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

        SetMarqueeFade(_marqueeRunning);
    }

    /// <summary>An inform card shows its close zone on the left (TODO 7.3, A4); every other card and typing don't.</summary>
    private void ShowCloseZone()
    {
        var show = !_typing && !_dropHover && _compose is null && _shownCard is { Type: CardType.Inform };
        CloseZone.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            CloseHover.Opacity = 0;
        }
        else if (_closeInkFor != CloseLabel.Text)
        {
            _closeInkFor = CloseLabel.Text;
            CentreCloseInk();
        }
    }

    private string? _closeInkFor;

    /// <summary>
    /// "Close" centred by its glyphs, not its line box (user 10-07: it sat off-centre up and down): its own inked rows are found in
    /// a snapshot and the label is moved so their middle is the zone's middle, rounded to 0.5. Again when the text changes.
    /// </summary>
    private async void CentreCloseInk()
    {
        try
        {
            CloseLabel.UpdateLayout();
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(CloseLabel);
            var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await bitmap.GetPixelsAsync());
            int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
            int top = -1, bottom = -1;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[(((y * width) + x) * 4) + 3] > 64)
                    {
                        top = top < 0 ? y : top;
                        bottom = y;
                        break;
                    }
                }
            }

            if (top < 0 || CloseLabel.ActualHeight <= 0)
            {
                return;
            }

            var scale = height / CloseLabel.ActualHeight;
            var inkMiddle = (top + bottom + 1) / 2.0 / scale;
            CloseShift.Y = Math.Round(((CloseLabel.ActualHeight / 2) - inkMiddle) * 2) / 2;
            TaskbarHost.Log($"close ink: rows {top}–{bottom} of {height} px → y {CloseShift.Y}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // async void: nothing may escape
        {
            TaskbarHost.Log("close ink: measure failed — " + ex.Message);
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
        var embed = _taskbar.IsAttached || _typing
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

    /// <summary>
    /// The model list check writes the lists to the file in the background; keep them current in memory too, or the next save
    /// (a launch, a settings change) wrote the old ones back (review 10-03).
    /// </summary>
    private void TakeBackgroundModelLists()
    {
        if (_store.TryLoad() is { } fresh)   // not when the file couldn't be read: defaults would be saved over the lists
        {
            _settings.Models = fresh.Models;
            _settings.CodexModels = fresh.CodexModels;
            _settings.ModelsChecked = fresh.ModelsChecked;
        }
    }

    private void OnSettingsChanged()
    {
        _feed.ApplySettings();
        TakeBackgroundModelLists();
        _store.Save(_settings);
        LayoutPills();   // the small pill's side and the corners are settings
        RenderIsland();  // ...and so is centring the card text
        if (!_typing || !UsableModes().Contains(_mode))
        {
            SetMode(HomeMode());   // if the order changed or file search was turned off, go back to the default mode
        }

        UpdateLeadIcon();
        Entry.PlaceholderText = Placeholders.For(_mode, _settings.Agent);
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
    /// Left icon: the spark extracted from claude.exe in Claude mode (same as the Python version), a magnifier in file mode.
    /// When an island (progress) is showing, the island decides the icon.
    /// </summary>
    private void UpdateLeadIcon()
    {
        var island = _snapshot.IsActive && !_typing && _compose is null;   // composing counts as typing (agent mark, not the island icon)
        var agentIcon = !island && _mode == BarMode.Claude;
        var spark = agentIcon && _settings.Agent == AgentKind.Claude && _claudeGlyph is not null;
        var blossom = agentIcon && _settings.Agent == AgentKind.Codex && _codexBrand is not null;
        ClaudeGlyphImage.Source = _claudeGlyph;
        CodexGlyph.Source = _codexBrand;
        ClaudeGlyphImage.Visibility = spark ? Visibility.Visible : Visibility.Collapsed;
        CodexGlyph.Visibility = blossom ? Visibility.Visible : Visibility.Collapsed;
        LeadIcon.Visibility = spark || blossom ? Visibility.Collapsed : Visibility.Visible;
        if (!island)
        {
            LeadIcon.Glyph = ModeGlyph(_mode);
            ZipTeeth.Opacity = 0;
        }
    }

    /// <summary>
    /// Dragging a file onto the pill runs the drop command on it when one is set (e.g. a script that sends to a phone — PhoneDrop),
    /// otherwise attaches it to the prompt. Blue border while hovering; on transfer, the island shows it sending.
    /// </summary>
    private void HookFileDrop()
    {
        Root.DragOver += (_, e) =>
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
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
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                return;
            }

            // The drop command decides what a drop does: if one is set (e.g. a script that sends to a phone),
            // run it with the dropped files; if it's empty, attach the files to the prompt instead (the default for everyone). (user 10-01)
            var hasCommand = !string.IsNullOrWhiteSpace(_settings.DropCommand);

            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (hasCommand)
                {
                    if (!_feed.PhoneDrop.Send(paths, _settings.DropCommand))
                    {
                        TaskbarHost.Log($"drop→command: nothing to run or not files ({paths.Count})");
                    }
                }
                else
                {
                    AttachToPrompt(paths);
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

    /// <summary>
    /// Attach dropped files to the prompt: put their paths into the input box (quoted when they contain spaces) and start typing,
    /// so the person can add their message and press Enter. The agent reads the referenced paths. (10-01)
    /// </summary>
    private void AttachToPrompt(IReadOnlyList<string> paths)
    {
        var files = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (files.Count == 0)
        {
            return;
        }

        if (_mode != BarMode.Claude && UsableModes().Contains(BarMode.Claude))
        {
            SetMode(BarMode.Claude);
        }

        var refs = string.Join(" ", files.Select(p => p.Contains(' ', StringComparison.Ordinal) ? $"\"{p}\"" : p));
        var gap = Entry.Text.Length > 0 && !Entry.Text.EndsWith(' ') ? " " : string.Empty;
        Entry.Text += gap + refs + " ";
        BeginTyping();
        Entry.Focus(FocusState.Programmatic);
        Entry.SelectionStart = Entry.Text.Length;
    }

    private ExpandedNotice? _expanded;
    private DateTimeOffset _lastExpandedAt = DateTimeOffset.MinValue;   // high-water mark so an older notice resurfacing (e.g. after X dismisses a newer one) doesn't re-open the card

    /// <summary>
    /// A message (Windows notification, KakaoTalk) grows out of the pill as a card, then shrinks back; the pill keeps the
    /// compact line for the rest of the notice's time. Not while typing (the card would cover what you are writing).
    /// </summary>
    /// <summary>
    /// Grows the expanded card for every message notice we haven't shown yet — fed the whole activity list, not just the
    /// primary one, so a burst of messages all reach the card and stack (relying on the single primary dropped the middle
    /// ones — user 09-30). The high-water mark on UpdatedAt means each message opens once, and an older one resurfacing
    /// (e.g. after the pill's X removes a newer one) does not re-open it.
    /// </summary>
    private void ExpandIfMessage(IReadOnlyList<ActivityState> items)
    {
        if (_typing)
        {
            return;
        }

        var fresh = items
            .Where(s => s is { Kind: ActivityKind.Notice, Stage: "expand" } && s.UpdatedAt > _lastExpandedAt)
            .OrderBy(s => s.UpdatedAt)
            .ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        // Mark these as shown NOW so the next tick doesn't re-feed them, but show the card OUTSIDE this tick: showing a card
        // creates/moves windows and runs composition, which pumps the message loop and stalled the island timer (the pill and
        // card then froze after a couple of messages — root cause found 09-30). Decoupling it keeps the tick fast and alive.
        _lastExpandedAt = fresh[^1].UpdatedAt;
        if (!_settings.ToastCard)
        {
            // card off (default, user 10-04): Windows messages stay as text on the pill. IsleBar's own cards (connect agents,
            // update available) still open — clicking the card is how you act on them.
            fresh.RemoveAll(s => s.Name != "IsleBar");
            if (fresh.Count == 0)
            {
                return;
            }
        }

        _ui.TryEnqueue(() =>
        {
            try
            {
                ExpandCards(fresh);
            }
            catch
            {
                // showing the card must never take the island timer down with it
            }
        });
    }

    private void ExpandCards(List<ActivityState> fresh)
    {
        if (_expanded is null)
        {
            _expanded = new ExpandedNotice();
            AppFonts.Apply(_expanded, _language);
        }

        foreach (var state in fresh)
        {
            _expanded.Show(
                new Windows.Graphics.RectInt32(Spot.X, Spot.Y, _width, _height),
                state.Glyph ?? "",
                state.Title ?? string.Empty,
                state.Msg,
                state.Name,
                state.Open,
                state.AppId);
            _lastExpandedAt = state.UpdatedAt;
        }
    }

    /// <summary>Mode icon (Segoe Fluent Icons) — shared by the left icon and the Tab hint.</summary>
    private static string ModeGlyph(BarMode mode) => mode switch
    {
        BarMode.Files => "",   // folder — file search (Everything). User feedback 09-30: icons must not be confusing
        _ => "",               // chat (when there's no Codex icon)
    };

    /// <summary>
    /// Tab hint (09-30 design decision B): <b>only when the input box is empty</b>, a dim [Tab] key + the next mode's icon on the right.
    /// Shows that Tab switches function, but disappears once you start typing so it doesn't get in the way (user asked for a hint that Tab changes the function).
    /// </summary>
    private void UpdateTabHint()
    {
        var next = BarModes.Next(_mode, UsableModes(), filesOn: true);
        var show = _typing && Entry.Text.Length == 0 && next != _mode && _pendingTimer is null;   // the "replace timer?" question needs the whole width
        TabHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            return;
        }

        var hint = BrushOf(_theme.Hint);
        TabKey.BorderBrush = hint;
        TabKeyText.Foreground = hint;
        TabNextIcon.Foreground = hint;
        TabNextIcon.Glyph = ModeGlyph(next);

        // if next is Claude/Codex, use the same mark as the left icon (from the user's PC; glyph when there is none)
        var mark = next == BarMode.Claude
            ? (_settings.Agent == AgentKind.Codex ? CodexGlyph.Source : _claudeGlyph)
            : null;
        TabNextMark.Source = mark;
        TabNextMark.Visibility = mark is null ? Visibility.Collapsed : Visibility.Visible;
        TabNextIcon.Visibility = mark is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Closes the results list window (recreated on the next search).</summary>
    private void CloseResults()
    {
        _results?.Close();
        _results = null;
    }

    private bool MoveResultSelection(int delta) => _results?.Move(delta) ?? true;

    /// <summary>Opens the selected result. Ctrl+Enter selects the file in Explorer instead.</summary>
    private void OpenSelectedFile(bool revealInFolder)
    {
        // nothing selected: the top row, as the Start menu's search opens its best match on Enter (user 10-04)
        if ((_results?.SelectedHit ?? _results?.FirstHit) is not { } hit || _shownQuery != Entry.Text.Trim())
        {
            // if the list hasn't caught up with the current input, search with the current input now and open the first item (same as the Python version)
            _openWhenReady = revealInFolder ? OpenRequest.Reveal : OpenRequest.Open;
            RequestSearch(Entry.Text.Trim());
            return;
        }

        OpenHit(hit, revealInFolder);
    }

    private void OpenHit(FileHit hit, bool revealInFolder)
    {
        if (!FileActions.Open(hit.FullPath, revealInFolder))
        {
            if (NativeMethods.GetForegroundWindow() != _taskbar.OuterWindow)
            {
                EndTyping();   // clicked with the mouse: the bar is no longer in front, so typing would never end (review 10-03)
            }

            return;
        }

        Entry.Text = string.Empty;
        EndTyping();
    }

    private static bool IsControlDown()
        => Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void ScheduleSearch()
    {
        if (_searchTimer is null)
        {
            // attach the handler only once (attaching on every call fires multiple searches)
            _searchTimer = _ui.CreateTimer();
            _searchTimer.Interval = SearchDebounce;
            _searchTimer.IsRepeating = false;
            _searchTimer.Tick += (_, _) =>
            {
                if (_typing)
                {
                    RequestSearch(Entry.Text.Trim());
                }
            };
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void RequestSearch(string query) => _fileSearch.Request(query, _settings);

    /// <summary>A sort button under the results: remember it and search again in that order, keeping typing in the input box.</summary>
    private void ChooseFileSort(string sort)
    {
        if (sort != _settings.FileSort)
        {
            _settings.FileSort = sort;
            _store.Update((IsleBarSettings s) =>
            {
                s.FileSort = sort;
                return true;
            });
        }

        ReclaimTyping();
        _ui.TryEnqueue(DispatcherQueuePriority.Low, ReclaimTyping);   // again once the click is fully handled — the list can take focus back on release
        RequestSearch(Entry.Text.Trim());
    }

    /// <summary>A click on the results list activated it — hand the keyboard back to the input box, or typing would end.</summary>
    private void ReclaimTyping()
    {
        if (!_typing)
        {
            return;
        }

        ForegroundHelper.Bring(_taskbar.OuterWindow);
        Entry.Focus(FocusState.Programmatic);
    }

    /// <summary>Row <paramref name="index"/> of the results: open it, or show it in Explorer.</summary>
    private void OpenResult(int index, bool revealInFolder)
    {
        _results?.SelectAt(index);
        if (_results?.SelectedHit is { } hit)
        {
            OpenHit(hit, revealInFolder);
        }
    }

    /// <summary>"Open in Everything" under the results: Everything's own window with the same words (all results, every column).</summary>
    private void OpenInEverything()
    {
        if (FileActions.EverythingExe() is not { } exe)
        {
            OpenEverythingDownload();
            return;
        }

        if (!FileActions.SearchInEverything(exe, Entry.Text.Trim()))
        {
            return;
        }

        Entry.Text = string.Empty;
        EndTyping();
    }

    /// <summary>
    /// "Everything isn't running — start it": starts it in the background (no window) and searches again once it has its index
    /// up — typing carries on. Not installed → its download page.
    /// </summary>
    private void StartEverything()
    {
        ReclaimTyping();   // the click on the link activated the list
        if (FileActions.EverythingExe() is not { } exe)
        {
            OpenEverythingDownload();
            return;
        }

        if (!FileActions.StartEverything(exe))
        {
            return;
        }

        var retry = _ui.CreateTimer();
        retry.Interval = TimeSpan.FromSeconds(1.5);
        retry.IsRepeating = false;
        retry.Tick += (_, _) =>
        {
            if (_typing && _mode == BarMode.Files)
            {
                RequestSearch(Entry.Text.Trim());
            }
        };
        retry.Start();
    }

    /// <summary>Everything isn't installed: its official download page in the browser.</summary>
    private void OpenEverythingDownload()
    {
        if (FileActions.OpenEverythingDownload())
        {
            EndTyping();
        }
    }

    /// <summary>Called from the search thread → <b>marshals to the UI thread</b> to draw. Results for stale input are dropped.</summary>
    private void OnSearchResult(long seq, IReadOnlyList<FileHit> hits, SearchProblem problem)
        => _ui.TryEnqueue(() =>
        {
            // not typing (Esc / a click away just ended it): clearing the box then asked for the recent list, which popped up
            // after typing had ended — and nothing closed it any more (user 10-04)
            if (seq != _fileSearch.LatestSeq || _mode != BarMode.Files || !_typing)
            {
                return;
            }

            if (_openWhenReady is { } open)
            {
                _openWhenReady = null;
                if (hits.Count > 0)
                {
                    OpenHit(hits[0], open == OpenRequest.Reveal);
                    return;
                }
            }

            // An empty box shows recent files, so the list is there as soon as file search opens (user 10-04). Nothing to show
            // → one line under the header, a link where it leads somewhere.
            (string? note, Action? noteAction) = problem switch
            {
                SearchProblem.SdkMissing => (_text.NoSdk, null),
                SearchProblem.ServiceOff => (_text.EsOffLink, StartEverything),
                SearchProblem.Failed => (_text.EsFail, null),
                _ => hits.Count == 0 ? (_text.NoResultLink, (Action)OpenInEverything) : (null, null),
            };

            if (_results is null)
            {
                _results = new ResultsWindow();
                AppFonts.Apply(_results, _language);
                _results.SortChosen += ChooseFileSort;
                _results.OpenInEverything += OpenInEverything;
                _results.KeepTyping += ReclaimTyping;
                _results.RowClicked += index => OpenResult(index, IsControlDown());
                _results.RevealClicked += index => OpenResult(index, revealInFolder: true);
            }

            _results.Show(hits, note, noteAction, PopupAnchorX, TaskbarTop(), _settings.FileSort, _text);
            _shownQuery = _fileSearch.RequestedQuery;   // the latest request's results (older ones were dropped above)
        });

    /// <summary>Windows display language (LANGID). The resolution rules live in Core (LanguageResolver).</summary>
    private static int CurrentUiLanguageId() => GetUserDefaultUILanguage();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    private enum OpenRequest { Open, Reveal }

    private async Task UpdateModelAliasesAsync()
    {
        using var source = new HttpModelAliasSource();
        var update = await new ModelAliasUpdater(_store, source).RunOnceAsync(DateTimeOffset.UtcNow);
        if (update.Changed)
        {
            RequestExit(ExitCodes.RestartRequested);   // relaunch with the new buttons
        }
    }

    private const int MenuQuit = 1;
    private const int MenuClose = 2;   // close the card the menu is about
    private const int MenuQuickBase = 20;   // quick timers (from settings, up to 6)
    private const int MenuPomodoro = 13;
    private const int MenuStopwatch = 14;
    private const int MenuUpdate = 15;   // "Download update (x.y.z)" while a newer release is out
    private const int MenuNotifications = 16;   // opens the Windows notification centre
    private const int MenuSettings = 17;

    /// <summary>A new timer waiting for "Replace the running timer?" to be answered (Enter = replace, Esc = keep).</summary>
    private ActivityState? _pendingTimer;

    /// <summary>
    /// Starts a timer — unless one is already running (or paused), in which case the input box asks first instead of silently
    /// throwing it away: starting a stopwatch used to wipe a running Pomodoro without a word (user chose "ask once", 10-01).
    /// True if it started now; false if it's waiting for the answer.
    /// </summary>
    private bool StartTimer(ActivityState next)
    {
        if (!_feed.TimerRunning(DateTimeOffset.UtcNow))
        {
            _feed.ReplaceTimer(next);
            return true;
        }

        _pendingTimer = next;
        if (!_typing)
        {
            _taskbar.TakeFocus();
            BeginTyping();
        }

        Entry.Text = string.Empty;
        Entry.PlaceholderText = _text.ReplaceTimerAsk;
        UpdateTabHint();
        return false;
    }

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
        else
        {
            _cards.Hide(card.Key, DateTimeOffset.UtcNow);
        }

        _cards.Update(_feed.Collect(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
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
        else if (_shownCard is { } shown && !_dropHover && _compose is null)
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

        items.Add(new MenuItem(MenuClose, _text.CloseCard));
        if (RunMenu(items) == MenuClose)
        {
            CloseCard(_cards.Cards.FirstOrDefault(c => c.Key == card.Key));
        }
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
            ActivityKind.Timer => (s.Name ?? _text.Timer, PrefixFor(s, now).Trim()),
            ActivityKind.Transfer => (s.Name ?? (s.Stage == "copy" ? _text.FileTransfer : _text.Downloading), PrefixFor(s, now).Trim()),
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

        var items = new List<MenuItem> { new(0, _text.Timer, Sub: timers), new(MenuNotifications, _text.AllNotifications) };
        var update = _settings.CheckUpdates ? SystemWatch.UpdateWatcher.Available : null;
        if (update is { } u)
        {
            items.Add(new MenuItem(MenuUpdate, $"{_text.UpdateMenu} ({u.Version.ToString(3)})"));   // a newer release is out (10-01)
        }

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
        else if (chosen == MenuNotifications)
        {
            // the full list is Windows' own notification centre (user 10-03: a list of our own would only duplicate it).
            // Win+N opens it; the "ms-actioncenter:" link did nothing on Windows 11 (measured 10-03).
            NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, 0, IntPtr.Zero);
            NativeMethods.keybd_event((byte)'N', 0, 0, IntPtr.Zero);
            NativeMethods.keybd_event((byte)'N', 0, NativeMethods.KEYEVENTF_KEYUP, IntPtr.Zero);
            NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, IntPtr.Zero);
        }
        else if (chosen == MenuUpdate && update is not null)
        {
            SystemWatch.Updater.Start();   // one-click update: download, check, install, restart
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
    /// couldn't receive clicks (measured on PC 09-29) → detach and bring it forward, as when typing, only while the menu is open.
    /// Position and shape are unchanged, so it's unnoticeable.
    /// </summary>
    private int RunMenu(IReadOnlyList<MenuItem> items)
    {
        var detached = false;
        if (!_typing && _taskbar.IsAttached)
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
            _ui.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!_typing)
                {
                    FocusSink.Focus(FocusState.Programmatic);   // so the input box isn't left with just a blinking caret
                }

                _menuOpen = false;
            });
        }

        if (detached && !_typing && _taskbar.Attach())
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
        _foreground?.Dispose();
        // every window closed, so the app ends by itself instead of waiting 4 s for the forced exit (review 10-03) — first, while
        // the meter and timers still exist (closing compose re-renders the pill)
        foreach (var close in new Action?[] { _settingsWindow is null ? null : _settingsWindow.Close, _expanded is null ? null : _expanded.CloseAll, _preview is null ? null : _preview.Close, _compose is null ? null : _compose.Close })
        {
            try
            {
                close?.Invoke();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
            }
        }

        _hotkey?.Dispose();
        StopMeterBars();
        _audioMeter.Dispose();
        _feed.Dispose();
        CloseResults();
        _settingsWindow?.Close();

        _islandTimer?.Stop();
        _searchTimer?.Stop();
        _watchdog.Dispose();
        _tracker.Dispose();
        _fileSearch.Dispose();
    }
}
