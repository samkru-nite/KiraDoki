using System.Globalization;
using System.Reflection;
using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.App.SystemWatch;
using IsleBar.Core.Configuration;
using IsleBar.Core.Island;
using IsleBar.Core.Localization;
using IsleBar.Core.Ui;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace IsleBar.App.Options;

/// <summary>
/// The settings window (rewritten 10-12, user): Windows 11's own look — a column of pages on the left (General, Reminders,
/// Calendar, Music, Timer, About; Quit at the bottom with the user's mascot), the page's cards on the right (icon, name, a grey
/// line, the control). Opens in the middle of the screen the bar is on. Every change is saved at once (no OK button, as in
/// Windows' Settings); text boxes save when left or when the window closes.
/// Icons are the island's own font (<see cref="Icons"/>): 16 in the column, 20 on the cards. The window's own content runs up
/// under the title bar (as Windows' Settings; the system's title bar was a black strip over the Mica in dark mode, user 10-12),
/// and the text is Windows' UI font — Segoe UI, Han characters and full-width marks in the language's own font.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private const int WidthDip = 680;
    private const int HeightDip = 560;
    private const double CardMinHeight = 68;
    private const double CardGap = 4;
    private const double NumberWidth = 132;   // a number box with its arrows inside: room for "600" and the clear button
    private const double RowIndent = 52;   // a group's rows start where the header's text does (16 + icon 20 + 16)

    private enum Page { General, Reminders, Files, Calendar, Music, Timer, About }

    private readonly IsleBarSettings _settings;
    private readonly Action _changed;
    private readonly Action _languageChanged;
    private readonly Func<string> _currentLanguage;
    private readonly Func<IReadOnlyList<(string Label, string Value)>> _diagnostics;
    private readonly Func<string, CalendarStatus?> _calendarStatus;
    private readonly Action _quit;
    private readonly NavigationView _nav = new()
    {
        PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
        IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        IsSettingsVisible = false,
        IsPaneToggleButtonVisible = false,
        IsTitleBarAutoPaddingEnabled = false,
        OpenPaneLength = 176,
    };

    private readonly Grid _root = new();
    private readonly TextBlock _caption = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    private readonly ScrollViewer _scroll = new();
    private readonly StackPanel _page = new() { Padding = new Thickness(24, 12, 24, 24), Spacing = CardGap };
    private readonly DispatcherTimer _calendarTick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<Action> _calendarRefresh = [];

    // text boxes on the page shown: a page switch or a closed window commits what is on screen
    private readonly List<Action> _commitOnLeave = [];
    private LanguageStrings _text;
    private Page _shown = Page.General;
    private NavigationViewItem? _quitItem;

    public SettingsWindow(IsleBarSettings settings, LanguageStrings text, Action changed, Action languageChanged, Func<string> currentLanguage,
        Func<IReadOnlyList<(string Label, string Value)>> diagnostics, Func<string, CalendarStatus?> calendarStatus, Action quit)
    {
        _settings = settings;
        _text = text;
        _changed = changed;
        _languageChanged = languageChanged;
        _currentLanguage = currentLanguage;
        _diagnostics = diagnostics;
        _calendarStatus = calendarStatus;
        _quit = quit;
        SystemBackdrop = new MicaBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        AppWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;   // just "Settings" (user 09-30)

        // the content goes up under the title bar; our own strip (the caption, draggable) sits where the system's was
        ExtendsContentIntoTitleBar = true;
        var bar = new Grid { Height = 32, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        bar.Children.Add(_caption);
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(bar);
        Grid.SetRow(_nav, 1);
        _root.Children.Add(_nav);
        SetTitleBar(bar);

        _scroll.Content = _page;
        _nav.Content = _scroll;
        _nav.ItemInvoked += OnInvoked;
        Content = _root;

        _calendarTick.Tick += (_, _) =>
        {
            foreach (var refresh in _calendarRefresh)
            {
                refresh();
            }
        };
        Closed += (_, _) =>
        {
            _calendarTick.Stop();
            CommitTexts();
        };
        Build();
    }

    /// <summary>Opens in the middle of the work area of the screen the bar is on (user 10-12).</summary>
    public void ShowCentered(int anchorX, int bandTop)
    {
        var area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(anchorX, bandTop - 1), DisplayAreaFallback.Primary).WorkArea;
        var width = DpiSetup.Px(WidthDip);
        var height = Math.Min(DpiSetup.Px(HeightDip), area.Height);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        AppWindow.Move(new Windows.Graphics.PointInt32(area.X + ((area.Width - width) / 2), area.Y + ((area.Height - height) / 2)));
        Activate();
    }

    /// <summary>When the language changes, fetch the strings again and redraw.</summary>
    public void Reload(LanguageStrings text)
    {
        _text = text;
        Build();
    }

    private void Build()
    {
        Title = _text.Settings;
        _caption.Text = _text.Settings;
        _nav.FontFamily = _caption.FontFamily = new FontFamily(UiFonts.Stack(null, _currentLanguage(), "Segoe UI Variable Text"));   // Windows' own UI font; Han and full-width marks fall to the language's
        _nav.MenuItems.Clear();
        _nav.FooterMenuItems.Clear();
        NavigationViewItem? selected = null;
        foreach (var (page, glyph, name) in new (Page, string, string)[]
                 {
                     (Page.General, Icons.Settings, _text.NavGeneral),
                     (Page.Reminders, Icons.Alert, _text.NotifySection),
                     (Page.Files, Icons.Download, _text.NavFiles),
                     (Page.Calendar, Icons.Calendar, _text.CardCalendar),
                     (Page.Music, Icons.Music, _text.CardMusic),
                     (Page.Timer, Icons.Timer, _text.Timer),
                     (Page.About, Icons.Info, _text.NavAbout),
                 })
        {
            var item = new NavigationViewItem { Content = name, Icon = Glyph(glyph, 16), Tag = page };
            _nav.MenuItems.Add(item);
            if (page == _shown)
            {
                selected = item;
            }
        }

        // Quit, apart at the bottom: the user's mascot instead of the app's name (user 10-12), a little bigger than the icons
        var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(-4, 0, 0, 0) };
        face.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/mascot.png")),
            Width = 24,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
        });
        face.Children.Add(new TextBlock { Text = _text.Quit, VerticalAlignment = VerticalAlignment.Center });
        _quitItem = new NavigationViewItem { Content = face, SelectsOnInvoked = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_quitItem, _text.Quit);
        _nav.FooterMenuItems.Add(_quitItem);

        _nav.SelectedItem = selected;
        ShowPage(_shown);
    }

    private void OnInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer == _quitItem)
        {
            CommitTexts();
            _quit();
            return;
        }

        if (args.InvokedItemContainer?.Tag is Page page && page != _shown)
        {
            ShowPage(page);
        }
    }

    private void ShowPage(Page page)
    {
        CommitTexts();
        _commitOnLeave.Clear();
        _calendarRefresh.Clear();
        _calendarTick.Stop();
        _shown = page;
        _page.Children.Clear();
        switch (page)
        {
            case Page.General:
                GeneralPage();
                break;
            case Page.Reminders:
                RemindersPage();
                break;
            case Page.Files:
                FilesPage();
                break;
            case Page.Calendar:
                CalendarPage();
                break;
            case Page.Music:
                MusicPage();
                break;
            case Page.Timer:
                TimerPage();
                break;
            case Page.About:
                AboutPage();
                break;
        }

        _scroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void CommitTexts()
    {
        foreach (var commit in _commitOnLeave.ToList())
        {
            commit();
        }
    }

    // ---------------------------------------------------------------- pages

    private void GeneralPage()
    {
        PageTitle(_text.NavGeneral);

        var lang = new ComboBox { MinWidth = 180 };
        lang.Items.Add(new ComboBoxItem { Content = _text.Auto, Tag = LanguageCatalog.Auto });
        foreach (var info in LanguageCatalog.Languages)
        {
            lang.Items.Add(new ComboBoxItem { Content = info.NativeName, Tag = info.Code });
        }

        lang.SelectedItem = lang.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _settings.Lang) ?? lang.Items[0];
        lang.SelectionChanged += (_, _) =>
        {
            if (lang.SelectedItem is ComboBoxItem { Tag: string code } && code != _settings.Lang)
            {
                _settings.Lang = code;
                _changed();
                _languageChanged();
            }
        };
        _page.Children.Add(Card(Icons.CardLanguage, _text.Lang, null, lang));

        // where on the taskbar (10-05): left end · centre · manual — the distance only for manual
        var place = new ComboBox { MinWidth = 180 };
        foreach (var (value, name) in new[]
                 {
                     (BarSlot.Key(BarPosition.Left), _text.PositionLeft),
                     (BarSlot.Key(BarPosition.Center), _text.PositionCenter),
                     (BarSlot.Key(BarPosition.Manual), _text.PositionManual),
                 })
        {
            place.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        }

        place.SelectedItem = place.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _settings.Position) ?? place.Items[0];
        var manual = _settings.Position == BarSlot.Key(BarPosition.Manual);
        var offset = Number(_settings.ManualOffset, 0, 10000, v => _settings.ManualOffset = v);
        var rows = new List<FrameworkElement> { Row(_text.ManualOffsetLabel, null, offset) };
        var group = Group(Icons.CardPosition, _text.PositionLabel, null, place, manual ? rows : []);
        place.SelectionChanged += (_, _) =>
        {
            if (place.SelectedItem is ComboBoxItem { Tag: string value } && value != _settings.Position)
            {
                _settings.Position = value;
                _changed();
                ShowPage(Page.General);   // the distance row comes or goes
            }
        };
        _page.Children.Add(group);

        // connect / disconnect Claude Code's hooks (10-01). The script takes seconds: the switch is held until it ends and then
        // shows what the files really say (a quick off-on, or a failed script, left it "on" with the hooks gone — review 10-03)
        ToggleSwitch? connect = null;
        connect = Toggle(() => Interop.HookConnector.IsConnected, v =>
        {
            var row = connect!;
            var ui = DispatcherQueue;
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
        }, saves: false);

        // AI (user 10-12): connecting, then a switch for each agent's cards
        SectionTitle(_text.SectionAi);
        _page.Children.Add(Card(Icons.CardConnect, _text.ConnectAgents, _text.ConnectAgentsDesc, connect));
        ToggleCard(Icons.CardClaude, "Claude", () => _settings.ShowClaude, v => _settings.ShowClaude = v, _text.SwitchClaudeDesc);
        ToggleCard(Icons.CardChatGpt, "ChatGPT", () => _settings.ShowChatGpt, v => _settings.ShowChatGpt = v, _text.SwitchChatGptDesc);
    }

    /// <summary>
    /// Downloads and files (user 10-12): a switch for each feature, grouped; the download folders moved here from 通用.
    /// </summary>
    private void FilesPage()
    {
        PageTitle(_text.NavFiles);
        SectionTitle(_text.SectionDownloads, first: true);
        ToggleCard(Icons.CardBrowserDownload, _text.SwitchBrowserDownloads, () => _settings.ShowBrowserDownloads, v => _settings.ShowBrowserDownloads = v, _text.SwitchBrowserDownloadsDesc);
        ToggleCard(Icons.CardDownloader, _text.SwitchDownloaders, () => _settings.ShowDownloaders, v => _settings.ShowDownloaders = v, _text.SwitchDownloadersDesc);
        ToggleCard(Icons.CardSteam, "Steam", () => _settings.ShowSteam, v => _settings.ShowSteam = v, _text.SwitchSteamDesc);
        DownloadFolders();

        SectionTitle(_text.SectionFiles);
        ToggleCard(Icons.CardFileOps, _text.SwitchFileOps, () => _settings.ShowFileOps, v => _settings.ShowFileOps = v, _text.SwitchFileOpsDesc);
        ToggleCard(Icons.CardArchive, _text.SwitchArchivers, () => _settings.ShowArchivers, v => _settings.ShowArchivers = v, _text.SwitchArchiversDesc);
        ToggleCard(Icons.CardExport, _text.SwitchExports, () => _settings.ShowExports, v => _settings.ShowExports = v, _text.SwitchExportsDesc);
        ToggleCard(Icons.CardPrinter, _text.SwitchPrinter, () => _settings.ShowPrinter, v => _settings.ShowPrinter = v, _text.SwitchPrinterDesc);
        ToggleCard(Icons.CardUsb, _text.SwitchDrives, () => _settings.ShowDrives, v => _settings.ShowDrives = v, _text.SwitchDrivesDesc);
        ToggleCard(Icons.CardTransit, _text.NoticeTransit, () => _settings.ShowTransit, v => _settings.ShowTransit = v, _text.SwitchTransitDesc);
    }

    private void RemindersPage()
    {
        PageTitle(_text.NotifySection);
        SectionTitle(_text.SectionSystem, first: true);
        ToggleCard(Icons.CardCharging, _text.NotifyPower, () => _settings.NotifyPower, v => _settings.NotifyPower = v);
        ToggleCard(Icons.CardBluetooth, _text.NotifyBluetooth, () => _settings.NotifyBluetooth, v => _settings.NotifyBluetooth = v);
        ToggleCard(Icons.CardWifi, _text.NotifyNetwork, () => _settings.NotifyNetwork, v => _settings.NotifyNetwork = v);
        ToggleCard(Icons.CardFocus, _text.NotifyFocus, () => _settings.NotifyFocus, v => _settings.NotifyFocus = v);
        ToggleCard(Icons.CardClipboard, _text.NotifyClipboard, () => _settings.NotifyClipboard, v => _settings.NotifyClipboard = v);
        ToggleCard(Icons.CardLoad, _text.NotifyLoad, () => _settings.NotifyLoad, v => _settings.NotifyLoad = v);
        ToggleCard(Icons.CardMic, _text.NotifyPrivacy, () => _settings.NotifyPrivacy, v => _settings.NotifyPrivacy = v);

        // Windows' notifications, and under them "only on the bar" — which only means something while they are read
        ToggleSwitch? banners = null;
        var toasts = Toggle(() => _settings.NotifyToasts, v =>
        {
            _settings.NotifyToasts = v;
            if (banners is not null)
            {
                banners.IsEnabled = v;
            }
        });
        var rows = new List<FrameworkElement>();
        if (!Interop.AppPaths.IsPackaged)   // the Store build has no uninstaller that could give the banners back
        {
            banners = Toggle(() => _settings.HideToastBanners, v => _settings.HideToastBanners = v);
            banners.IsEnabled = _settings.NotifyToasts;
            rows.Add(Row(_text.HideToastBanners, _text.HideToastBannersDesc, banners));
        }

        SectionTitle(_text.SectionChat);
        _page.Children.Add(Group(Icons.CardAlert, _text.NotifyToasts, _text.NotifyToastsDesc, toasts, rows));
        ToggleCard(Icons.CardWeChat, _text.SwitchWeChat, () => _settings.NotifyWeChat, v => _settings.NotifyWeChat = v, _text.SwitchWeChatDesc);
    }

    /// <summary>
    /// The calendars (user 10-12): an address is pasted in once and then shown by the calendar's own name with how its last fetch
    /// went — the address itself holds a secret and is never shown again (nor logged).
    /// </summary>
    private void CalendarPage()
    {
        PageTitle(_text.CardCalendar);

        var address = new TextBox { PlaceholderText = "https://calendar.google.com/…/basic.ics", IsSpellCheckEnabled = false };
        var add = new Button { Content = _text.Add, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var wrong = Hint(_text.CalendarNotCalendar);
        wrong.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        wrong.Visibility = Visibility.Collapsed;
        void Add()
        {
            var url = CalendarWatcher.NormalizeUrl(address.Text);
            if (url.Length == 0)
            {
                wrong.Visibility = Visibility.Visible;
                return;
            }

            if (!_settings.Calendars.Contains(url, StringComparer.Ordinal))
            {
                _settings.Calendars = [.. _settings.Calendars, url];
                _changed();
            }

            ShowPage(Page.Calendar);
        }

        add.Click += (_, _) => Add();
        address.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Add();
            }
        };
        address.TextChanged += (_, _) => wrong.Visibility = Visibility.Collapsed;
        var line = new Grid { ColumnSpacing = 8, Margin = new Thickness(RowIndent - 16, 12, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(address);
        Grid.SetColumn(add, 1);
        line.Children.Add(add);
        wrong.Margin = new Thickness(RowIndent - 16, 4, 0, 0);
        _page.Children.Add(Tall(Card(Icons.CardCalendarAdd, _text.AddCalendar, _text.CalendarAddHint, null), line, wrong));

        if (_settings.Calendars.Count == 0)
        {
            return;
        }

        SectionTitle(_text.CalendarsAdded);
        foreach (var url in _settings.Calendars)
        {
            var remove = new Button { Content = _text.Remove };
            remove.Click += (_, _) =>
            {
                _settings.Calendars = [.. _settings.Calendars.Where(u => u != url)];
                _changed();
                ShowPage(Page.Calendar);
            };
            var card = Card(Icons.CardCalendar, _text.CardCalendar, _text.CalendarLoading, remove);
            var (name, state) = CardTexts(card);
            void Refresh()
            {
                var status = _calendarStatus(url);
                name.Text = status?.Name is { Length: > 0 } own ? own : _text.CardCalendar;
                state.Text = status switch
                {
                    null => _text.CalendarLoading,
                    { Problem: CalendarProblem.Offline } => _text.CalendarOffline,
                    { Problem: CalendarProblem.NotCalendar } => _text.CalendarNotCalendar,
                    _ => string.Format(CultureInfo.CurrentCulture, _text.CalendarSynced,
                        status.Synced?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), status.Events),
                };
                state.Foreground = (Brush)Application.Current.Resources[status is { Problem: not CalendarProblem.None }
                    ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush"];
            }

            Refresh();
            _calendarRefresh.Add(Refresh);
            _page.Children.Add(card);
        }

        _calendarTick.Start();   // a calendar just added is read in the background: its line follows
    }

    private void MusicPage()
    {
        PageTitle(_text.CardMusic);
        ToggleCard(Icons.CardGlobe, _text.BrowserMedia, () => _settings.BrowserMedia, v => _settings.BrowserMedia = v);
        ToggleCard(Icons.CardSound, _text.AudioBars, () => _settings.AudioBars, v => _settings.AudioBars = v);
    }

    private void TimerPage()
    {
        PageTitle(_text.Timer);

        // Pomodoro: three lengths and the rounds before the long break (user 10-12), number boxes instead of "25 / 5 / 15"
        var pomodoro = Group(Icons.CardTimer, _text.Pomodoro, Rounds(), null,
        [
            Row(_text.Focus, null, Minutes(_settings.PomodoroFocus, v => _settings.PomodoroFocus = v)),
            Row(_text.Break, null, Minutes(_settings.PomodoroBreak, v => _settings.PomodoroBreak = v)),
            Row(_text.LongBreak, null, Minutes(_settings.PomodoroLong, v => _settings.PomodoroLong = v)),
            Row(_text.PomodoroRoundsLabel, null, WithUnit(Number(_settings.PomodoroRounds, 1, TimerParser.MaxPomodoroRounds, v =>
            {
                _settings.PomodoroRounds = v;
                ShowPage(Page.Timer);   // the header says how many
            }), _text.RoundsUnit)),
        ]);
        _page.Children.Add(pomodoro);

        // quick timers: one row each with Delete, then a box to add one (at most 6 — the menu stays short)
        var rows = new List<FrameworkElement>();
        foreach (var minutes in _settings.QuickTimers)
        {
            var delete = new Button { Content = _text.Delete };
            delete.Click += (_, _) =>
            {
                _settings.QuickTimers = [.. _settings.QuickTimers.Where(m => m != minutes)];
                _changed();
                ShowPage(Page.Timer);
            };
            rows.Add(Row(string.Format(CultureInfo.InvariantCulture, _text.Minutes, minutes), null, delete));
        }

        if (_settings.QuickTimers.Count < 6)
        {
            var box = new NumberBox { Value = 10, Minimum = 1, Maximum = 600, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MinWidth = NumberWidth };
            var add = new Button { Content = _text.Add };
            add.Click += (_, _) =>
            {
                var minutes = double.IsNaN(box.Value) ? 0 : (int)Math.Round(box.Value);
                if (minutes is >= 1 and <= 600 && !_settings.QuickTimers.Contains(minutes))
                {
                    _settings.QuickTimers = [.. _settings.QuickTimers, minutes];
                    _changed();
                }

                ShowPage(Page.Timer);
            };
            // on the right like every row's control: [10 ⌃⌄] 分钟 [添加]
            var adder = WithUnit(box, _text.MinutesUnit);
            adder.Children.Add(add);
            rows.Add(Row(string.Empty, null, adder));
        }

        _page.Children.Add(Group(Icons.CardAlarm, _text.QuickTimers, _text.QuickTimersDesc, null, rows));
        ToggleCard(Icons.CardMute, _text.MuteChimeFullscreen, () => _settings.MuteChimeFullscreen, v => _settings.MuteChimeFullscreen = v);

        string Rounds() => string.Format(CultureInfo.InvariantCulture, _text.PomodoroDesc, _settings.PomodoroRounds);
    }

    private void AboutPage()
    {
        PageTitle(_text.NavAbout);
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        _page.Children.Add(Card(Icons.CardInfo, _text.Version, null, Selectable(version)));

        // diagnostics (TODO 7.8): is each part that leans on Windows working — read when the page opens
        var rows = _diagnostics().Select(d => (FrameworkElement)Row(d.Label, null, Selectable(d.Value))).ToList();
        _page.Children.Add(Group(Icons.CardDiagnose, _text.DiagSection, null, null, rows));

        // who made it (user 10-12: both of us, side by side), and what it grew from — the original isn't an author of this one
        var names = _currentLanguage() is "zh" or "zht" or "ja" ? "无敌企鹅大王、Claude" : "无敌企鹅大王, Claude";
        _page.Children.Add(Card(Icons.CardPeople, _text.Author, null, Selectable(names)));
        // a link in the words, no button box around it, right edge with the other values (user 10-12)
        var link = new Microsoft.UI.Xaml.Documents.Hyperlink
        {
            NavigateUri = new Uri("https://github.com/ds3owl/islebar"),
            UnderlineStyle = Microsoft.UI.Xaml.Documents.UnderlineStyle.None,
        };
        link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = _text.BasedOnValue });
        var original = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        original.Inlines.Add(link);
        _page.Children.Add(Card(Icons.CardFork, _text.BasedOn, null, original));

        var notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        Button? open = null;
        if (File.Exists(notices))
        {
            open = new Button { Content = _text.MenuOpen };
            open.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", '"' + notices + '"') { UseShellExecute = false });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // no Notepad: nothing to open it with
                }
            };
        }

        _page.Children.Add(Card(Icons.CardLicense, _text.Licenses, _text.LicensesDesc, open));
    }

    /// <summary>
    /// Folders watched for downloads (10-04), added one by one like the calendars (user 10-12): Downloads first, always watched
    /// and not removable; each added folder a row with Remove; "Add a folder" opens Windows' folder picker. Browsers, Thunder and
    /// Quark are found on their own (10-11), so this is for other download programs.
    /// </summary>
    private void DownloadFolders()
    {
        var system = Media.DownloadWatcher.DefaultFolder();
        static string Bare(string folder) => folder.TrimEnd('\\', '/');
        var rows = new List<FrameworkElement> { Row(system, null, null) };
        foreach (var folder in _settings.DownloadFolders
                     .Where(f => !string.Equals(Bare(Environment.ExpandEnvironmentVariables(f)), Bare(system), StringComparison.OrdinalIgnoreCase)))
        {
            var remove = new Button { Content = _text.Remove };
            remove.Click += (_, _) =>
            {
                _settings.DownloadFolders = [.. _settings.DownloadFolders.Where(f => f != folder)];
                _changed();
                ShowPage(Page.Files);
            };
            rows.Add(Row(folder, null, remove));
        }

        var add = new Button { Content = _text.AddFolder };
        add.Click += async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (await picker.PickSingleFolderAsync() is not { } chosen || chosen.Path.Length == 0)
            {
                return;
            }

            var path = Bare(chosen.Path);
            if (!string.Equals(path, Bare(system), StringComparison.OrdinalIgnoreCase)
                && !_settings.DownloadFolders.Any(f => string.Equals(Bare(f), path, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.DownloadFolders = [.. _settings.DownloadFolders, path];
                _changed();
            }

            ShowPage(Page.Files);
        };
        rows.Add(Row(string.Empty, null, add));
        _page.Children.Add(Group(Icons.CardDownloads, _text.DownloadFoldersLabel, _text.DownloadFoldersHint, null, rows));
    }

    /// <summary>A card with more under its header line (a text box and its button), padded all round.</summary>
    private static Border Tall(Border card, params UIElement[] below)
    {
        var head = (Grid)card.Child;
        head.MinHeight = 0;
        ((FrameworkElement)head.Children.OfType<StackPanel>().First()).Margin = new Thickness(0);
        var inside = new StackPanel();
        card.Child = null;
        inside.Children.Add(head);
        foreach (var part in below)
        {
            inside.Children.Add(part);
        }

        card.Child = inside;
        card.Padding = new Thickness(16);
        return card;
    }

    // ---------------------------------------------------------------- parts

    private void PageTitle(string text)
        => _page.Children.Add(new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 16),
        });

    /// <param name="first">Right under the page title: no room above (the title keeps its own 16).</param>
    private void SectionTitle(string text, bool first = false)
        => _page.Children.Add(new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(1, first ? 0 : 24, 0, 4),
        });

    private static FontIcon Glyph(string glyph, double size)
        => new() { FontFamily = AppFonts.IconFamily, Glyph = glyph, FontSize = size };

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private static TextBlock Hint(string text)
        => new() { Text = text, FontSize = 12, Foreground = Res("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap };

    /// <summary>A card: icon 20, the name and a grey line under it, the control on the right — Windows 11's settings card.</summary>
    private static Border Card(string glyph, string title, string? description, UIElement? control)
        => new()
        {
            Background = Res("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Res("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 0, 16, 0),
            MinHeight = CardMinHeight,
            Child = Line(Glyph(glyph, 20), title, description, control),
        };

    /// <summary>The name and grey line of a card made by <see cref="Card"/> (to change them later).</summary>
    private static (TextBlock Name, TextBlock Line) CardTexts(Border card)
    {
        var words = (StackPanel)((Grid)card.Child).Children.OfType<StackPanel>().First();
        return ((TextBlock)words.Children[0], (TextBlock)words.Children[1]);
    }

    private static Grid Line(FontIcon? icon, string title, string? description, UIElement? control)
    {
        var grid = new Grid { ColumnSpacing = 16, MinHeight = CardMinHeight - 2 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (icon is not null)
        {
            icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);
        }

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
        words.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        var line = Hint(description ?? string.Empty);
        line.Visibility = description is null ? Visibility.Collapsed : Visibility.Visible;
        words.Children.Add(line);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        if (control is FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(element, 2);
            grid.Children.Add(element);
        }

        return grid;
    }

    /// <summary>
    /// A card with rows under it (as Windows' expanded settings group): the header card, then each row on the same card colour
    /// behind a divider, its name where the header's name starts.
    /// </summary>
    private static StackPanel Group(string glyph, string title, string? description, UIElement? control, IReadOnlyList<FrameworkElement> rows)
    {
        var group = new StackPanel();
        var head = Card(glyph, title, description, control);
        if (rows.Count > 0)
        {
            head.CornerRadius = new CornerRadius(4, 4, 0, 0);
        }

        group.Children.Add(head);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = (Border)rows[i];
            row.BorderThickness = new Thickness(1, 0, 1, 1);
            row.CornerRadius = i == rows.Count - 1 ? new CornerRadius(0, 0, 4, 4) : new CornerRadius(0);
            group.Children.Add(row);
        }

        return group;
    }

    /// <summary>One row of a <see cref="Group"/>.</summary>
    private static Border Row(string title, string? description, UIElement? control)
    {
        var line = Line(null, title, description, control);
        line.MinHeight = 48;
        line.ColumnSpacing = 0;
        ((FrameworkElement)line.Children[0]).Margin = new Thickness(0, 8, 16, 8);
        return new Border
        {
            Background = Res("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Res("CardStrokeColorDefaultBrush"),
            Padding = new Thickness(RowIndent, 0, 16, 0),
            Child = line,
        };
    }

    private void ToggleCard(string glyph, string title, Func<bool> get, Action<bool> set, string? description = null)
        => _page.Children.Add(Card(glyph, title, description, Toggle(get, set)));

    /// <summary>An on/off switch without its "On / Off" words (they took room the cards don't have; Windows' own cards show them
    /// on the left, the switch reads the same without).</summary>
    private ToggleSwitch Toggle(Func<bool> get, Action<bool> set, bool saves = true)
    {
        var toggle = new ToggleSwitch { IsOn = get(), OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 };
        toggle.Resources["ToggleSwitchPreContentMargin"] = 0.0;
        toggle.Resources["ToggleSwitchPostContentMargin"] = 0.0;
        toggle.Toggled += (_, _) =>
        {
            if (toggle.IsOn != get())
            {
                set(toggle.IsOn);
                if (saves)
                {
                    _changed();
                }
            }
        };
        return toggle;
    }

    /// <summary>A whole number box with up / down arrows; saved when it changes (typed and left, Enter, or an arrow).</summary>
    private NumberBox Number(int value, int min, int max, Action<int> set)
    {
        var box = new NumberBox
        {
            Value = value,
            Minimum = min,
            Maximum = max,
            SmallChange = 1,
            // the arrows inside the box: the compact ones popped out over the next words (user 10-12: it covered "分钟")
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            MinWidth = NumberWidth,
        };
        box.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue))
            {
                box.Value = e.OldValue;   // emptied: back to what it was
                return;
            }

            var n = (int)Math.Round(Math.Clamp(e.NewValue, min, max));
            set(n);
            _changed();
        };
        return box;
    }

    private StackPanel Minutes(int value, Action<int> set) => WithUnit(Number(value, 1, 600, set), _text.MinutesUnit);

    private static StackPanel WithUnit(FrameworkElement box, string unit)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, MinWidth = 32 });
        return panel;
    }

    private static TextBlock Selectable(string text)
        => new() { Text = text, IsTextSelectionEnabled = true, Foreground = Res("TextFillColorSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center };
}
