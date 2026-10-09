using IsleBar.App.Interop;
using IsleBar.Core.Configuration;
using IsleBar.Core.Ui;
using IsleBar.Core.Launch;
using IsleBar.Core.Localization;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace IsleBar.App.Options;

/// <summary>
/// Settings window. The same items as the Python version's <c>open_settings</c> (language · launch options and pins · working folder · file search)
/// plus the <b>agent (Claude / Codex) picker</b> built in server step 11.
/// Saves immediately on every change (no separate "OK" button — same as the Windows Settings app).
/// </summary>
internal sealed class SettingsWindow : Window
{
    private const int WidthDip = 460;

    private readonly IsleBarSettings _settings;
    private readonly Action _changed;
    private readonly Action _languageChanged;
    private readonly Func<string> _currentLanguage;
    private readonly Func<IReadOnlyList<(string Label, string Value)>> _diagnostics;
    private readonly StackPanel _body = new() { Padding = new Thickness(20, 8, 20, 20), Spacing = 6 };
    private LanguageStrings _text;

    public SettingsWindow(IsleBarSettings settings, LanguageStrings text, Action changed, Action languageChanged, Func<string> currentLanguage,
        Func<IReadOnlyList<(string Label, string Value)>> diagnostics)
    {
        _currentLanguage = currentLanguage;
        _diagnostics = diagnostics;
        Closed += (_, _) =>
        {
            foreach (var commit in _commitOnClose.ToList())
            {
                commit();
            }
        };
        _settings = settings;
        _text = text;
        _changed = changed;
        _languageChanged = languageChanged;
        SystemBackdrop = new MicaBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Title is just "Settings", no icon or taskbar button (user feedback 09-30: drop "IsleBar", it looks cheap)
        AppWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        AppWindow.IsShownInSwitchers = false;

        // A stock ToggleSwitch puts a 10 dip empty row above and below the knob (space meant for On/Off text we don't use —
        // these are x:Double row heights in the default template, not margins). So every toggle row (island notices, appearance,
        // the "remote control" option, Tab-order switches) stood ~20 dip taller than the segmented-button rows beside it. That made
        // the launch options look lopsided — "remote control" floated with a big gap above and was crammed against the next section
        // below — and bloated the whole window past the screen height so it scrolled (user feedback 10-01). Trimming these two
        // heights to 4 dip makes toggle rows the same compact height as the button rows, so the spacing reads evenly.
        // (Applied on _body so every toggle under it inherits it; the knob keeps breathing room from the template's own 0,5 margin.)
        _body.Resources["ToggleSwitchPreContentMargin"] = 4.0;
        _body.Resources["ToggleSwitchPostContentMargin"] = 4.0;

        Content = new ScrollViewer { Content = _body };
        Build();
    }

    private int _anchorX;
    private int _bandTop;

    public void ShowAbove(int anchorX, int bandTop)
    {
        (_anchorX, _bandTop) = (anchorX, bandTop);
        PopupPlacement.ShowSized(this, _body, WidthDip, anchorX, bandTop);
    }

    /// <summary>After the item count changes (agent switch, language change), re-fit the window size and position.</summary>
    private void Refit()
    {
        _body.UpdateLayout();
        PopupPlacement.SizeToContent(this, _body, WidthDip);
        PopupPlacement.PlaceAbove(this, _anchorX, _bandTop);
    }

    /// <summary>When the language changes, fetch the strings again and redraw.</summary>
    public void Reload(LanguageStrings text)
    {
        _text = text;
        Build();
        Refit();
    }

    // the text rows of the window as built now — a rebuild (agent, mode or language switch) replaces them, so a closed window
    // commits only what is on screen (the old rows' stale values reverted saved edits — review 10-03)
    private readonly List<Action> _commitOnClose = [];

    private void Build()
    {
        _commitOnClose.Clear();
        BuildBody();
        IslandUi.AppFonts.Apply(this, _language);   // on every redraw (changing the language also changes the Han-character font)
    }

    private void BuildBody()
    {
        Title = _text.Settings;
        _body.Children.Clear();

        // Language
        Section(_text.Lang);
        var lang = new ComboBox { MinWidth = 180 };
        lang.Items.Add(new ComboBoxItem { Content = _text.Auto, Tag = LanguageCatalog.Auto });
        foreach (var info in LanguageCatalog.Languages)
        {
            lang.Items.Add(new ComboBoxItem { Content = info.NativeName, Tag = info.Code });
        }

        lang.SelectedItem = lang.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _settings.Lang)
                            ?? lang.Items[0];
        lang.SelectionChanged += (_, _) =>
        {
            if (lang.SelectedItem is ComboBoxItem { Tag: string code } && code != _settings.Lang)
            {
                _settings.Lang = code;
                _changed();
                _languageChanged();
            }
        };
        _body.Children.Add(lang);

        // Agent
        Section(_text.Agent);
        var agents = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (name, label) in new[] { (AgentKind.Claude, "Claude"), (AgentKind.Codex, "Codex") })
        {
            var button = new ToggleButton { Content = label, IsChecked = _settings.Agent == name, MinWidth = 80 };
            button.Click += (_, _) =>
            {
                if (_settings.Agent != name)
                {
                    _settings.Agent = name;
                    _changed();
                }

                Build();   // each agent shows different options and models
                Refit();
            };
            agents.Children.Add(button);
        }

        _body.Children.Add(agents);

        // Launch options
        Section(_text.Options);
        foreach (var key in LaunchOptionDefs.All.Where(k => OptionControls.IsShown(_settings, k)))
        {
            var row = OptionControls.Row(_settings, _text, key, compact: false, _changed);
            row.Margin = new Thickness(0, 2, 0, 2);
            _body.Children.Add(row);
        }

        // Working folder
        Section(_text.Folder);
        var folderRow = new Grid { ColumnSpacing = 6 };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var folderText = new TextBlock
        {
            Text = CurrentFolder(),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = 0.75,
        };
        ToolTipService.SetToolTip(folderText, CurrentFolder());
        folderRow.Children.Add(folderText);
        var change = new Button { Content = _text.FolderChange };
        change.Click += async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (await picker.PickSingleFolderAsync() is { } picked)
            {
                _settings.Folder = picked.Path;
                _settings.RecentFolders = RecentFolders.Add(_settings.RecentFolders, picked.Path);
                _changed();
                folderText.Text = CurrentFolder();
            }
        };
        Grid.SetColumn(change, 1);
        folderRow.Children.Add(change);
        var home = new Button { Content = _text.FolderHome };
        home.Click += (_, _) =>
        {
            _settings.Folder = "";
            _changed();
            folderText.Text = CurrentFolder();
        };
        Grid.SetColumn(home, 2);
        folderRow.Children.Add(home);
        _body.Children.Add(folderRow);

        DownloadFoldersRow();

        // Island notices (09-30 feature): per-kind on/off + calendar (ICS URL)
        Section(_text.NotifySection);
        ToggleRow(_text.NotifyPower, () => _settings.NotifyPower, v => _settings.NotifyPower = v);
        ToggleRow(_text.NotifyBluetooth, () => _settings.NotifyBluetooth, v => _settings.NotifyBluetooth = v);
        ToggleRow(_text.NotifyNetwork, () => _settings.NotifyNetwork, v => _settings.NotifyNetwork = v);
        ToggleRow(_text.NotifyFocus, () => _settings.NotifyFocus, v => _settings.NotifyFocus = v);
        ToggleRow(_text.NotifyClipboard, () => _settings.NotifyClipboard, v => _settings.NotifyClipboard = v);
        ToggleRow(_text.NotifyLoad, () => _settings.NotifyLoad, v => _settings.NotifyLoad = v);
        ToggleRow(_text.NotifyPrivacy, () => _settings.NotifyPrivacy, v => _settings.NotifyPrivacy = v);
        ToggleRow(_text.NotifyToasts, () => _settings.NotifyToasts, v => _settings.NotifyToasts = v);
        if (!Interop.AppPaths.IsPackaged)   // the Store build has no uninstaller that could give the banners back
        {
            ToggleRow(_text.HideToastBanners, () => _settings.HideToastBanners, v => _settings.HideToastBanners = v);   // user 10-01 — a toggle, so it can ship
        }
        TextRow(_text.CalendarIcs, _text.CalendarIcsHint, () => _settings.CalendarIcs, v => _settings.CalendarIcs = v);

        // Timer: Pomodoro lengths + quick timers in the right-click menu (user feedback 09-30: in case they want to change the durations)
        Section(_text.Timer);
        TextRow(_text.PomodoroMinutes, "25 / 5 / 15",
            () => $"{_settings.PomodoroFocus} / {_settings.PomodoroBreak} / {_settings.PomodoroLong}",
            v =>
            {
                var n = ParseMinutes(v);
                if (n.Count == 3)
                {
                    (_settings.PomodoroFocus, _settings.PomodoroBreak, _settings.PomodoroLong) = (n[0], n[1], n[2]);
                }
            });
        TextRow(_text.QuickTimersLabel, "5, 25",
            () => string.Join(", ", _settings.QuickTimers),
            v =>
            {
                var n = ParseMinutes(v);
                if (n.Count > 0)
                {
                    _settings.QuickTimers = [.. n.Distinct().Take(6)];
                }
            });

        // Appearance (preference, 09-30 design C1) — off by default
        Section(_text.StyleSection);
        ToggleRow(_text.StyleGlass, () => _settings.StyleGlass, v => _settings.StyleGlass = v);
        ToggleRow(_text.AudioBars, () => _settings.AudioBars, v => _settings.AudioBars = v);
        ToggleRow(_text.BrowserMedia, () => _settings.BrowserMedia, v => _settings.BrowserMedia = v);   // 10-04
        // where on the taskbar (10-05): left end · centre · left of the tray · manual
        ChoiceRow(_text.PositionLabel,
            [
                (BarSlot.Key(BarPosition.Left), _text.PositionLeft),
                (BarSlot.Key(BarPosition.Center), _text.PositionCenter),
                (BarSlot.Key(BarPosition.BesideTray), _text.PositionTray),
                (BarSlot.Key(BarPosition.Manual), _text.PositionManual),
            ],
            () => _settings.Position, v => _settings.Position = v);
        TextRow(_text.ManualOffsetLabel, "12",
            () => _settings.ManualOffset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            v =>
            {
                if (int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
                {
                    _settings.ManualOffset = Math.Clamp(n, 0, 10000);
                }
            });
        ChoiceRow(_text.BallSideLabel, [("right", _text.BallSideRight), ("left", _text.BallSideLeft)],
            () => _settings.BallSide, v => _settings.BallSide = v);
        ChoiceRow(_text.CornersLabel, [("round", _text.CornersRound), ("small", _text.CornersSmall)],
            () => _settings.PillCorners, v => _settings.PillCorners = v);
        ChoiceRow(_text.SwapAnimLabel, [("line", _text.SwapAnimLine), ("arc", _text.SwapAnimArc), ("flip", _text.SwapAnimFlip)],
            () => _settings.SwapAnimation, v => _settings.SwapAnimation = v);
        ChoiceRow(_text.LiftAnimLabel, [("morph", _text.LiftAnimMorph), ("slide", _text.LiftAnimSlide)],
            () => _settings.LiftAnimation, v => _settings.LiftAnimation = v);
        ChoiceRow(_text.ListStaggerLabel, [("20", "20 ms"), ("40", "40 ms")],   // try both, keep one (user 10-09)
            () => _settings.ListStagger, v => _settings.ListStagger = v);
        ToggleRow(_text.SwapBounce, () => _settings.Bounce, v => _settings.Bounce = v);   // one switch for the swap and the lift (user 10-08)
        ToggleRow(_text.Transparency, () => _settings.Transparency, v => _settings.Transparency = v);   // the lifted card's glass (丙④, user 10-10)
        ChoiceRow(_text.CardTextLabel, [("left", _text.CardTextLeft), ("group", _text.CardTextCenterGroup), ("text", _text.CardTextCenterText)],
            () => _settings.CardTextAlign, v => _settings.CardTextAlign = v);
        // Advanced: power options, grouped under their own heading but always visible (labelled, not hidden). The drop command runs
        // a program on dropped files (we use it for phone transfer); the toggle picks whether a drop attaches to the prompt or runs it (10-01).
        Section(_text.AdvancedSection);
        TextRow(_text.DropCommand, _text.DropCommandHint, () => _settings.DropCommand, v => _settings.DropCommand = v);
        ToggleRow(_text.MuteChimeFullscreen, () => _settings.MuteChimeFullscreen, v => _settings.MuteChimeFullscreen = v);   // 10-03

        // connect / disconnect Claude Code and Codex without setup (the Store build has none) — 10-01
        // the script takes seconds: the switch is held until it ends and then shows what the files really say — a quick
        // off-on, or a failed script, left it showing "on" with the hooks gone (review 10-03)
        ToggleSwitch? connect = null;
        connect = ToggleRow(_text.ConnectAgents, () => Interop.HookConnector.IsConnected, v =>
        {
            var row = connect!;
            var ui = DispatcherQueue;   // taken here, on the UI thread — not from the pool thread after the window may have closed
            row.IsEnabled = false;
            _ = Task.Run(() => Interop.HookConnector.Set(v)).ContinueWith(_ => ui.TryEnqueue(() =>
            {
                try
                {
                    row.IsOn = Interop.HookConnector.IsConnected;
                    row.IsEnabled = true;
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
                {
                    // the window was closed meanwhile
                }
            }), TaskScheduler.Default);
        });

        // Diagnostics (TODO 7.8, first version 10-05): is each part that leans on Windows working — read when the window opens
        Section(_text.DiagSection);
        foreach (var (label, value) in _diagnostics())
        {
            var line = new Grid { ColumnSpacing = 12 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
            var shown = new TextBlock { Text = value, IsTextSelectionEnabled = true, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(shown, 1);
            line.Children.Add(shown);
            _body.Children.Add(line);
        }
    }

    /// <summary>A name above a drop-down of choices; <paramref name="choices"/> = (stored value, shown name).</summary>
    private void ChoiceRow(string label, (string Value, string Name)[] choices, Func<string> get, Action<string> set)
    {
        _body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
        var box = new ComboBox { MinWidth = 220 };
        foreach (var (value, name) in choices)
        {
            box.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        }

        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == get()) ?? box.Items[0];
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: string value } && value != get())
            {
                set(value);
                _changed();
            }
        };
        _body.Children.Add(box);
    }

    /// <summary>A row with a name + an on/off toggle on the right.</summary>
    private ToggleSwitch ToggleRow(string label, Func<bool> get, Action<bool> set)
    {
        // label and switch in their own columns: long labels (the crash-report and Claude Code rows in Korean) ran under the
        // switch; now they wrap (seen 10-03)
        var line = new Grid { Margin = new Thickness(0, 1, 0, 1), ColumnSpacing = 12 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var toggle = new ToggleSwitch
        {
            IsOn = get(),
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label);   // unnamed for screen readers before (10-01)
        toggle.Toggled += (_, _) =>
        {
            if (toggle.IsOn != get())
            {
                set(toggle.IsOn);
                _changed();
            }
        };
        Grid.SetColumn(toggle, 1);
        line.Children.Add(toggle);
        _body.Children.Add(line);
        return toggle;
    }

    /// <summary>
    /// Folders watched for downloads, one per line (user 10-04). Unset shows this user's own Downloads folder — the path Windows
    /// gives for them, so no user name is ever written in — and is also the format example. Keeping just that line stores "unset",
    /// like the hidden-folders list, so the setting follows the folder if it moves and doesn't carry one PC's path to another.
    /// </summary>
    private void DownloadFoldersRow()
    {
        var system = Media.DownloadWatcher.DefaultFolder();
        _body.Children.Add(new TextBlock { Text = _text.DownloadFoldersLabel, Margin = new Thickness(0, 8, 0, 4) });
        var box = new TextBox
        {
            Text = _settings.DownloadFolders.Count == 0 ? system : string.Join("\r", _settings.DownloadFolders),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 80,
            IsSpellCheckEnabled = false,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        _body.Children.Add(box);
        _body.Children.Add(new TextBlock
        {
            Text = _text.DownloadFoldersHint,
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        var reset = new HyperlinkButton { Content = _text.ResetDefaults, Padding = new Thickness(0, 2, 0, 2), FontSize = 12 };
        _body.Children.Add(reset);

        void Commit()
        {
            var lines = box.Text.Split('\r', '\n').Select(line => line.Trim().Trim('"')).Where(line => line.Length > 0).ToList();
            if (lines.Count == 1 && string.Equals(lines[0].TrimEnd('\\', '/'), system.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                lines = [];   // just the system folder = unset
            }

            if (!lines.SequenceEqual(_settings.DownloadFolders))
            {
                _settings.DownloadFolders = lines;
                _changed();
            }
        }

        box.LostFocus += (_, _) => Commit();
        _commitOnClose.Add(Commit);
        reset.Click += (_, _) =>
        {
            box.Text = system;
            Commit();
        };
    }

    /// <summary>A name + text input box (saved on leaving the box or pressing Enter).</summary>
    private void TextRow(string label, string hint, Func<string> get, Action<string> set)
    {
        _body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
        var box = new TextBox { Text = get(), PlaceholderText = hint, TextWrapping = TextWrapping.NoWrap };
        ToolTipService.SetToolTip(box, hint);
        void Commit()
        {
            var value = box.Text.Trim();
            var before = get();
            if (value != before)
            {
                set(value);
                var after = get();
                box.Text = after;   // "30 / 5" or "5, 700" kept only what is valid — the box showed the typed text as if saved (review 10-03)
                if (after != before)
                {
                    _changed();
                }
            }
        }

        box.LostFocus += (_, _) => Commit();
        _commitOnClose.Add(Commit);   // a value typed and then the window closed with X was dropped (review 10-03)
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Commit();
            }
        };
        _body.Children.Add(box);
    }

    /// <summary>Minute numbers (1–600) from text like "25 / 5 / 15" or "5, 25".</summary>
    private static List<int> ParseMinutes(string text)
        => [.. System.Text.RegularExpressions.Regex.Matches(text, @"\d+")
            .Select(m => int.TryParse(m.Value, out var n) ? n : 0)
            .Where(n => n is >= 1 and <= 600)];

    private void Section(string title)
        => _body.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            // 10 (not 14) above: once the rows within a section were tightened, a 14 dip section gap stood out — the space under
            // the last launch option ("remote control") read as too airy before "working folder" (user feedback 10-01).
            Margin = new Thickness(0, 10, 0, 4),
        });

    /// <summary>The current UI language (if auto, the one determined from the Windows language).</summary>
    private string _language => _currentLanguage();

    private string CurrentFolder()
        => string.IsNullOrEmpty(_settings.Folder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), FolderTrust.DefaultFolderName)
            : _settings.Folder;
}
