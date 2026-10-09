using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.Core.Localization;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IsleBar.App;

/// <summary>
/// The input panel (TODO 7.9 H): the circle opens it, the circle / Esc / a click elsewhere puts it away. H1a: the panel, its input
/// box and mode button; the file list comes in H1b, the draft dot and the old typing's removal in H1c.
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

    /// <summary>Files: 8 rows of 40 under the header (user 10-09; a setting later).</summary>
    private const double PanelListHeight = 31 + (8 * 40);

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
            };
            _panel.Closing = () => _ = Dim(1, LiftMotion.Close, cells: true);
            _panel.Gone = () => _panelDims = false;
            _panel.Key = key =>
            {
                if (key == Windows.System.VirtualKey.Enter)
                {
                    TaskbarHost.Log($"panel: enter in {_panelMode} ({_panel.Box.Text.Length} chars) — H1b/H3");
                    return true;
                }

                return false;
            };
            _panel.List.Children.Add(new TextBlock
            {
                Text = "文件列表下一步接入（H1b）",
                FontSize = 12,
                Opacity = 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        CloseLift();
        CloseList();
        ShowPanelMode();
        _panelDims = true;
        _ = Dim(ListMotion.Dim, LiftMotion.OpenLength(_settings.Bounce), cells: true);   // both pills grey under it, as under the ⋯ list (user 10-09)
        _panel.Open(new PanelRequest(
            circle, pills, screen.X, screen.X + screen.Width, TaskbarTop(),
            !ThemeColors.FromSystem().Light, _settings.Transparency, _settings.Bounce, PanelListHeight));
    }

    /// <summary>The mode button and the box's grey hint for the panel's mode.</summary>
    private void ShowPanelMode()
    {
        if (_panel is null)
        {
            return;
        }

        var files = _panelMode == BarMode.Files;
        var look = new FontIcon { Glyph = files ? "" : "", FontSize = 16 };   // search / chat
        // in the UI's language (user 10-09: "Search files" should read 搜索文件)
        var ask = string.Format(System.Globalization.CultureInfo.CurrentCulture, _text.AskAgentHint,
            _settings.Agent == IsleBar.Core.Launch.AgentKind.Codex ? "Codex" : "Claude");
        _panel.SetMode(look, files ? _text.SearchFilesHint : ask, files ? ask : _text.SearchFilesHint);
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
