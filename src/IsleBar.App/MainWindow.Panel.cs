using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.App.Search;
using IsleBar.Core.Localization;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IsleBar.App;

/// <summary>
/// The input panel (TODO 7.9 H): the circle opens it, the circle / Esc / a click elsewhere puts it away. H1a: the panel, its input
/// box and mode button; H1b: the file list (<see cref="FileList"/>); the draft dot, the AI shell and the old typing's removal in H1c.
/// </summary>
public sealed partial class MainWindow
{
    private InputPanel? _panel;

    /// <summary>The left button went down on the circle: the panel opens (or goes) on its release.</summary>
    private bool _ballPress;

    /// <summary>The dimming is the panel's (⋯ greys too), not the ⋯ list's.</summary>
    private bool _panelDims;

    /// <summary>
    /// The panel is up — or this very press just put it away: the system-wide outside-press watch hears a press on the pills before
    /// the bar does, and the bar then lifted the card under it too (user 10-09: grey must mean "does nothing").
    /// </summary>
    private bool PanelTakesPress => _panel is { } p && (p.IsOpen || DateTimeOffset.UtcNow - p.ClosedAt < TimeSpan.FromMilliseconds(400));

    /// <summary>The panel's mode — files first: asking AI is only its shell for now (user 10-09).</summary>
    private BarMode _panelMode = BarMode.Files;

    /// <summary>The file list in the panel (made with the panel).</summary>
    private FileList? _fileList;

    /// <summary>Asking AI: only a placeholder in the list's place for now (H1c).</summary>
    private TextBlock? _askList;

    /// <summary>The panel is up and searching files: search results are drawn (a late one after it went down is dropped).</summary>
    private bool PanelSearching => _panel?.IsOpen == true && _panelMode == BarMode.Files;

    /// <summary>What's typed in the panel's box.</summary>
    private string SearchText() => _panel?.Box.Text.Trim() ?? string.Empty;

    /// <summary>A file (or Everything) was opened from the panel: the box starts empty next time, the panel goes.</summary>
    private void ClosePanelDone()
    {
        if (_panel is null)
        {
            return;
        }

        _panel.Box.Text = string.Empty;
        _panel.Collapse();
    }

    /// <summary>
    /// The list's height: the "results to show" setting's rows (default 8, user 10-09), fewer when the screen above the taskbar
    /// can't hold that many with a 4-line box — the rest then scrolls inside.
    /// </summary>
    private double PanelListHeight(int screenTop)
    {
        var s = DpiSetup.Scale;
        var room = ((TaskbarTop() - screenTop) / s) - LiftMotion.ScreenMargin - LiftMotion.TaskbarGap - PanelMotion.Room
            - PanelMotion.Height(0, PanelMotion.FieldHeight(PanelMotion.MaxLines, 20)) - FileList.HeaderHeight;
        var rows = Math.Clamp((int)Math.Floor(room / FileList.RowPitch), 3, _settings.FileResults);
        return FileList.HeightFor(rows);
    }

    /// <summary>The circle: opens the panel, or puts it away.</summary>
    private void TogglePanel()
    {
        if (_panel?.IsOpen == true)
        {
            _panel.Collapse();
            return;
        }

        // the press on the circle took the foreground from the panel, which went down on that — this press only closes it
        if (_panel is not null && DateTimeOffset.UtcNow - _panel.ClosedAt < TimeSpan.FromMilliseconds(400))
        {
            return;
        }

        // a beat later, once this click is done with: the bar's window took the keyboard back while handling it, and the panel then
        // showed no caret (log 10-09: "deactivated, foreground ours" right after opening)
        _ui.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, OpenPanel);
    }

    private void OpenPanel()
    {
        if (!NativeMethods.GetWindowRect(_taskbar.OuterWindow, out var bar))
        {
            return;
        }

        var circle = OnScreen(BallBox, bar);
        var small = OnScreen(SmallPill, bar);
        var pills = BigPillOnScreen() is { } big
            ? new ScreenRect(Math.Min(big.Left, small.Left), small.Top, Math.Max(big.Right, small.Right), small.Bottom)
            : small;
        var screen = Microsoft.UI.Windowing.DisplayArea.GetFromRect(
            new Windows.Graphics.RectInt32(pills.Left, pills.Top, pills.Width, pills.Height),
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).OuterBounds;

        if (_panel is null)
        {
            _panel = new InputPanel();
            _panel.ModeClicked = () =>
            {
                _panelMode = _panelMode == BarMode.Files ? BarMode.Claude : BarMode.Files;
                ShowPanelMode();
                if (PanelSearching)
                {
                    RequestSearch(SearchText());
                }
            };
            _panel.Closing = () => _ = Dim(1, LiftMotion.Close, cells: true);
            _panel.Gone = () => _panelDims = false;
            _panel.Key = OnPanelKey;
            _panel.Box.TextChanged += (_, _) =>
            {
                if (PanelSearching)
                {
                    ScheduleSearch();
                }
            };

            _fileList = new FileList { FontFamily = AppFonts.For(null, _language).Normal };
            _fileList.SortChosen += ChooseFileSort;
            _fileList.OpenInEverything += OpenInEverything;
            _fileList.KeepTyping += () => _panel.TakeKeyboard();
            _fileList.RowClicked += index => OpenResult(index, IsControlDown());
            _fileList.RevealClicked += index => OpenResult(index, revealInFolder: true);
            _panel.Busy = () => _fileList.MenuOpen;
            _askList = new TextBlock
            {
                Text = "最近对话 · 以后接入（H1c）",
                FontSize = 12,
                Opacity = 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _panel.List.Children.Add(_fileList);
            _panel.List.Children.Add(_askList);
        }

        CloseLift();
        CloseList();
        ShowPanelMode();
        _panelDims = true;
        _ = Dim(ListMotion.Dim, LiftMotion.OpenLength(_settings.Bounce), cells: true);   // both pills grey under it, as under the ⋯ list (user 10-09)
        _panel.Open(new PanelRequest(
            circle, pills, screen.X, screen.X + screen.Width, TaskbarTop(),
            !ThemeColors.FromSystem().Light, _settings.Transparency, _settings.Bounce, PanelListHeight(screen.Y)));
        if (_panelMode == BarMode.Files)
        {
            RequestSearch(SearchText());   // at once: an empty box lists recent files, as the Start menu does
        }
    }

    /// <summary>Keys the box doesn't use itself: Enter opens (Ctrl+Enter shows it in Explorer), ↑↓ move through the files.</summary>
    private bool OnPanelKey(Windows.System.VirtualKey key)
    {
        if (_panelMode != BarMode.Files)
        {
            if (key == Windows.System.VirtualKey.Enter)
            {
                TaskbarHost.Log($"panel: enter in {_panelMode} ({SearchText().Length} chars) — H1c");
                return true;
            }

            return false;
        }

        switch (key)
        {
            case Windows.System.VirtualKey.Enter:
                OpenSelectedFile(IsControlDown());
                return true;
            case Windows.System.VirtualKey.Down:
                return MoveResultSelection(1);
            case Windows.System.VirtualKey.Up:
                return MoveResultSelection(-1);
            default:
                return false;
        }
    }

    /// <summary>The mode button and the box's grey hint for the panel's mode.</summary>
    private void ShowPanelMode()
    {
        if (_panel is null)
        {
            return;
        }

        var files = _panelMode == BarMode.Files;
        if (_fileList is not null && _askList is not null)
        {
            _fileList.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
            _askList.Visibility = files ? Visibility.Collapsed : Visibility.Visible;
        }

        var look = new FontIcon { Glyph = files ? "" : "", FontSize = 16 };   // search / chat
        // in the UI's language (user 10-09: "Search files" should read 搜索文件)
        var ask = string.Format(System.Globalization.CultureInfo.CurrentCulture, _text.AskAgentHint,
            _settings.Agent == IsleBar.Core.Launch.AgentKind.Codex ? "Codex" : "Claude");
        var agent = _settings.Agent == IsleBar.Core.Launch.AgentKind.Codex ? "Codex" : "Claude";
        var tip = files ? string.Format(System.Globalization.CultureInfo.CurrentCulture, _text.ModeAskTip, agent) : _text.ModeFilesTip;   // "Click to …" (user 10-09)
        _panel.SetMode(look, files ? _text.SearchFilesHint : ask, tip);
    }

    /// <summary>An element of this window on screen (pixels).</summary>
    private ScreenRect OnScreen(FrameworkElement element, NativeMethods.RECT bar)
    {
        var at = element.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, 0));
        var left = bar.Left + DpiSetup.Px(at.X);
        var top = bar.Top + DpiSetup.Px(at.Y);
        return new ScreenRect(left, top, left + DpiSetup.Px(element.ActualWidth), top + DpiSetup.Px(element.ActualHeight));
    }
}
