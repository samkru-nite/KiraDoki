using IsleBar.App.Interop;
using IsleBar.App.Options;
using IsleBar.Core.Localization;
using IsleBar.Core.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace IsleBar.App.Search;

/// <summary>
/// File search results list. Pops up right above the search box.
/// <b>Does not take focus</b> (NOACTIVATE) — typing must continue in the input box while the list is shown.
/// Selection (↑↓) and open (Enter) keys are received by the input box and forwarded to this window.
/// <para>
/// Made to look like Windows' own lists (user 10-04): Explorer-style column headers on top that sort when clicked (on top so the
/// IME candidate bar, which opens under the input box, never covers them), the system ListView for selection/hover/scrolling,
/// the shell's file icons, and acrylic like the Start menu's search. Always the same width (the old one's); long names and paths
/// are shortened in the middle. Right-click a result for open / file location / copy / Everything.
/// </para>
/// </summary>
internal sealed class ResultsWindow : Window
{
    private const int WidthDip = 480;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>About the height 12 rows of the old list took (user 10-04: "at most about 12"); the rest scrolls.</summary>
    private const double MaxListHeight = 534;

    // One spacing rhythm, measured on the user's screenshots (10-04): window top → header text, header text → first row,
    // row → row and last row → window bottom are all ~14. Before: 11 / 21 / 14 / 16, which the first row's highlight hid.
    private const double HeaderTop = 11;      // + the window's own ~3 above = 14 to the header text
    private const double HeaderBottom = 4;    // + the first row's own ~10 above its text = 14 (1 measured 11 — "a little tight", user 10-04)
    private const double HeaderText = 16;     // line height of the 12 px header labels
    private const double HeaderHeight = HeaderTop + HeaderText + HeaderBottom;
    private const double HoverPad = 4;        // hover background around a header label: the same above and below it
    private const double IconSize = 20;
    private const double DateWidth = 100;     // "2026/10/14 18:34"
    private const double SizeWidth = 58;      // "238.8 MB"
    private const double ColumnGap = 12;      // the same between every column (user 10-04)
    private const double ItemPadding = 12;    // the ListViewItem's side padding
    private const double ListPadding = 4;
    private const double ListBottom = 2;      // + the last row's own ~12 below its text = 14 to the window edge

    /// <summary>Room left for the name and path.</summary>
    private const double NameWidth = WidthDip - (2 * ListPadding) - (2 * ItemPadding) - IconSize - DateWidth - SizeWidth - (3 * ColumnGap);

    private readonly ListView _list = new()
    {
        SelectionMode = ListViewSelectionMode.Single,
        IsItemClickEnabled = true,
        IsTabStop = false,
        Padding = new Thickness(ListPadding, 0, ListPadding, ListBottom),
    };

    private readonly Grid _header = new()
    {
        Padding = new Thickness(ListPadding + ItemPadding, HeaderTop, ListPadding + ItemPadding, HeaderBottom),
        ColumnSpacing = ColumnGap,
    };

    private readonly StackPanel _empty = new() { Padding = new Thickness(16, 10, 16, 14) };
    private readonly Grid _root = new() { Width = WidthDip };   // fixed: long names used to stretch the window (user 10-04)
    private readonly MenuFlyout _menu = new();
    private readonly ShellIcons _shellIcons = new();

    // measuring text is the slow part of building a row — the same names and folders come back on every keystroke
    private readonly TextBlock _nameProbe = new() { FontSize = 13, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock _pathProbe = new() { FontSize = 11, TextWrapping = TextWrapping.NoWrap };
    private readonly Dictionary<string, string> _shortNames = [];
    private readonly Dictionary<string, string> _shortPaths = [];

    /// <summary>Icons by <see cref="ShellIcons.KeyFor"/>; the images still waiting for one.</summary>
    private readonly Dictionary<string, ImageSource> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(Image Image, UIElement Glyph)>> _waiting = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<FileHit> _hits = [];
    private LanguageStrings? _text;
    private int _anchorX;
    private int _bandTop;
    private int _menuIndex = -1;
    private bool _menuOpen;

    public ResultsWindow()
    {
        SystemBackdrop = new ActiveAcrylic();
        PopupPlacement.MakeToolPopup(this);

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_list, 1);
        Grid.SetRow(_empty, 1);
        _root.Children.Add(_header);
        _root.Children.Add(_list);
        _root.Children.Add(_empty);
        Content = _root;

        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconSize) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DateWidth) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SizeWidth) });

        _list.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement { Tag: int index })
            {
                RowClicked?.Invoke(index);
            }
        };

        // one menu for every row (one per row made each keystroke build dozens — part of the 10-04 lag)
        _menu.Opening += (_, _) =>
        {
            _menuOpen = true;
            _menuIndex = _menu.Target is FrameworkElement { Tag: int index } ? index : -1;
            FillMenu();
            if (_menuIndex >= 0)
            {
                Selected = _menuIndex;   // the row the menu belongs to shows as chosen
                _list.SelectedIndex = _menuIndex;
            }
        };
        _menu.Closed += (_, _) => _menuOpen = false;
        Closed += (_, _) => _shellIcons.Dispose();

        Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var ex = NativeMethods.Unsigned32(NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE));
        NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, NativeMethods.Signed32(ex | WS_EX_NOACTIVATE));
    }

    /// <summary>This window's handle — the bar checks whether it's the one in front.</summary>
    public IntPtr Handle { get; }

    /// <summary>
    /// Whether the mouse is over the list right now. A click on it still takes activation from the bar (NOACTIVATE or not), and
    /// the bar used to end typing on that — closing the list before the click arrived, so clicking a result did nothing (10-03).
    /// </summary>
    public bool UnderCursor() => PopupPlacement.UnderCursor(this);

    /// <summary>The right-click menu is open — the bar must not end typing (that would close the list under it).</summary>
    public bool MenuOpen => _menuOpen;

    /// <summary>A row was clicked (mouse) or "Open" chosen. Argument = row index.</summary>
    public event Action<int>? RowClicked;

    /// <summary>"Open file location" from the menu. Argument = row index.</summary>
    public event Action<int>? RevealClicked;

    /// <summary>A column header was clicked. Argument = the sort setting value.</summary>
    public event Action<string>? SortChosen;

    /// <summary>"Open in Everything" (menu, or the link under an empty result).</summary>
    public event Action? OpenInEverything;

    /// <summary>Something on the list was used that leaves the search going (a copy) — the bar takes the keyboard back.</summary>
    public event Action? KeepTyping;

    public int Selected { get; private set; }

    public FileHit? SelectedHit => Selected >= 0 && Selected < _hits.Count ? _hits[Selected] : null;

    /// <summary>The top row — what Enter opens when nothing is selected (as the Start menu's search does).</summary>
    public FileHit? FirstHit => _hits.Count > 0 ? _hits[0] : null;

    public bool HasHits => _hits.Count > 0;

    /// <summary>
    /// Shows results (or a single hint line under the header). Nothing starts selected — ↓ selects the first row, Enter opens
    /// the top one, Esc closes (user 10-04: a pre-selected first row read as a choice already made).
    /// </summary>
    /// <param name="noteAction">What clicking the hint does (it's then drawn as a link): search in Everything, start it…</param>
    /// <param name="sort">Current sort setting value — its header shows the arrow.</param>
    public void Show(IReadOnlyList<FileHit> hits, string? note, Action? noteAction, int anchorX, int bandTop, string sort, LanguageStrings text)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        (_hits, _anchorX, _bandTop, Selected, _text) = (hits, anchorX, bandTop, -1, text);
        if ((Content as Control)?.FontFamily is { } font)
        {
            _nameProbe.FontFamily = font;   // measure in the font the list is drawn in (set per language by AppFonts)
            _pathProbe.FontFamily = font;
        }

        BuildHeader(sort, text);
        _waiting.Clear();
        _list.Items.Clear();
        for (var i = 0; i < hits.Count; i++)
        {
            _list.Items.Add(Row(hits[i], i));
        }

        BuildEmpty(hits.Count == 0 ? note : null, noteAction);
        // the header always stays — the frame doesn't change, only what's in it (user 10-04)
        _list.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // at most about 12 old rows tall, and never past the top of the screen — the rest scrolls (wheel / two-finger swipe)
        _list.MaxHeight = Math.Max(120, Math.Min(MaxListHeight, PopupPlacement.MaxHeightAbove(anchorX, bandTop) - HeaderHeight - 8));
        _list.SelectedIndex = -1;
        if (hits.Count > 0)
        {
            _list.ScrollIntoView(_list.Items[0]);
        }

        _root.UpdateLayout();

        // only whole rows: a half-cut last row made the bottom margin look larger than the top one (user 10-04)
        if (hits.Count > 0 && _list.ContainerFromIndex(0) is ListViewItem first && first.ActualHeight > 0)
        {
            var pitch = first.ActualHeight + first.Margin.Top + first.Margin.Bottom;
            var room = _list.MaxHeight - ListBottom;
            if (hits.Count * pitch > room)
            {
                _list.MaxHeight = (Math.Floor(room / pitch) * pitch) + ListBottom;
                _root.UpdateLayout();
            }
        }

        PopupPlacement.SizeToContent(this, _root, WidthDip);
        PopupPlacement.PlaceAbove(this, _anchorX, _bandTop);
        AppWindow.Show(activateWindow: false);
        if (clock.ElapsedMilliseconds >= 30)
        {
            TaskbarHost.Log($"file search: drew {hits.Count} rows in {clock.ElapsedMilliseconds} ms");
        }
    }

    public void Hide() => AppWindow.Hide();

    /// <summary>Moves the selected row. False if the list is empty.</summary>
    public bool Move(int delta)
    {
        if (_hits.Count == 0)
        {
            return false;
        }

        if (Selected < 0 && delta < 0)
        {
            return true;   // nothing selected yet: ↑ has nowhere to go
        }

        SelectAt(Selected < 0 ? 0 : Selected + delta);   // the first ↓ selects the top row
        return true;
    }

    /// <summary>Selects row <paramref name="index"/> (clamped) and scrolls it into view.</summary>
    public void SelectAt(int index)
    {
        if (_hits.Count == 0)
        {
            return;
        }

        Selected = Math.Clamp(index, 0, _hits.Count - 1);
        _list.SelectedIndex = Selected;
        _list.ScrollIntoView(_list.Items[Selected]);   // ↑↓ past the visible rows scrolls
    }

    // ---------------- header ----------------

    /// <summary>Explorer-style headers: name · date modified · size. The one sorted by shows a small arrow; a click sorts by it.</summary>
    private void BuildHeader(string sort, LanguageStrings text)
    {
        _header.Children.Clear();
        // every header and column starts at its left edge, as the name lines up with its icon (user 10-04, option A)
        AddHeader(0, 2, FileSearchQuery.SortName, text.SortName, sort);
        AddHeader(2, 1, FileSearchQuery.SortModified, text.SortModified, sort);
        AddHeader(3, 1, FileSearchQuery.SortSize, text.SortSize, sort);
    }

    private void AddHeader(int column, int span, string value, string label, string sort)
    {
        const double pad = 6;
        var on = value == sort;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var arrow = new FontIcon
        {
            Glyph = "",   // chevron down on every column — the same mark means "sorted by this" (user 10-04)
            FontSize = 8,
            Foreground = Brush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = on ? Visibility.Visible : Visibility.Collapsed,
        };
        var title = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Brush(on ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(title);
        content.Children.Add(arrow);

        var button = new Button
        {
            Content = content,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            // the hover background hugs the label with the same room above and below (it sat low before — user 10-04); the
            // negative margin pulls the button out by its padding, so the label itself stays exactly where the layout puts it
            Padding = new Thickness(pad, HoverPad, pad, HoverPad),
            Margin = new Thickness(-pad, -HoverPad, 0, -HoverPad),
            Height = HeaderText + (2 * HoverPad),
            MinHeight = 0,
            IsTabStop = false,
            VerticalAlignment = VerticalAlignment.Top,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => SortChosen?.Invoke(value);
        Grid.SetColumn(button, column);
        Grid.SetColumnSpan(button, span);
        _header.Children.Add(button);
    }

    // ---------------- rows ----------------

    private ListViewItem Row(FileHit hit, int index)
    {
        var grid = new Grid { ColumnSpacing = ColumnGap, Tag = index, Padding = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconSize) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DateWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SizeWidth) });

        // icon: a plain glyph until the shell's icon arrives (usually at once — they're shared per file type)
        var iconSlot = new Grid { Width = IconSize, Height = IconSize, VerticalAlignment = VerticalAlignment.Center };
        var glyph = new FontIcon { Glyph = hit.IsFolder ? "" : "", FontSize = 16 };
        var image = new Image { Width = IconSize, Height = IconSize, Stretch = Stretch.Uniform };
        iconSlot.Children.Add(glyph);
        iconSlot.Children.Add(image);
        grid.Children.Add(iconSlot);
        ShowIcon(hit, image, glyph);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = Shorten(_shortNames, hit.Name, _nameProbe, FileListText.MiddleEllipsisName),
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,   // only if the measurement was a hair off
        };
        if (name.Text != hit.Name)
        {
            ToolTipService.SetToolTip(name, hit.Name);
        }

        var path = new TextBlock
        {
            Text = Shorten(_shortPaths, hit.Directory, _pathProbe, FileListText.MiddleEllipsis),
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brush("TextFillColorSecondaryBrush"),
        };
        if (path.Text != hit.Directory)
        {
            ToolTipService.SetToolTip(path, hit.Directory);
        }

        names.Children.Add(name);
        names.Children.Add(path);
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);

        grid.Children.Add(Cell(2, hit.Modified is { } when ? when.ToString("g", System.Globalization.CultureInfo.CurrentCulture) : string.Empty));
        grid.Children.Add(Cell(3, hit.Size is { } bytes ? FileListText.FormatSize(bytes) : string.Empty));

        return new ListViewItem
        {
            Content = grid,
            Tag = index,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(ItemPadding, 0, ItemPadding, 0),
            ContextFlyout = _menu,
        };
    }

    /// <summary>A date or size cell: secondary colour, starting at the column's left edge like its header.</summary>
    private static TextBlock Cell(int column, string text)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = Brush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(cell, column);
        return cell;
    }

    /// <summary>Right-click: open · open file location · copy path · copy name · open in Everything — for the row clicked.</summary>
    private void FillMenu()
    {
        _menu.Items.Clear();
        if (_text is null || _menuIndex < 0 || _menuIndex >= _hits.Count)
        {
            return;
        }

        var hit = _hits[_menuIndex];
        var index = _menuIndex;
        void Add(string label, string glyph, Action act)
        {
            var entry = new MenuFlyoutItem { Text = label, Icon = new FontIcon { Glyph = glyph } };
            entry.Click += (_, _) => act();
            _menu.Items.Add(entry);
        }

        Add(_text.MenuOpen, "", () => RowClicked?.Invoke(index));
        Add(_text.MenuOpenFolder, "", () => RevealClicked?.Invoke(index));
        Add(_text.MenuCopyPath, "", () => Copy(hit.FullPath));
        Add(_text.MenuCopyName, "", () => Copy(hit.Name));
        _menu.Items.Add(new MenuFlyoutSeparator());
        Add(_text.OpenInEverything, "", () => OpenInEverything?.Invoke());
    }

    private void Copy(string value)
    {
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(value);
        try
        {
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // the clipboard is busy (another app holds it) — nothing to do
        }

        KeepTyping?.Invoke();
    }

    /// <summary>
    /// Nothing to list: one centred line under the header — a link when it leads somewhere ("No results — search in
    /// Everything", "Everything isn't running — start it"), plain text otherwise (user 10-04).
    /// </summary>
    private void BuildEmpty(string? note, Action? action)
    {
        _empty.Children.Clear();
        _empty.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
        if (note is null)
        {
            return;
        }

        if (action is null)
        {
            _empty.Children.Add(new TextBlock
            {
                Text = note,
                FontSize = 13,
                Foreground = Brush("TextFillColorSecondaryBrush"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        var link = new HyperlinkButton
        {
            Content = new TextBlock { Text = note, FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            HorizontalAlignment = HorizontalAlignment.Center,
            IsTabStop = false,
        };
        link.Click += (_, _) => action();
        _empty.Children.Add(link);
    }

    // ---------------- helpers ----------------

    /// <summary>The text as it fits the name column — measured once per distinct text, then remembered.</summary>
    private static string Shorten(Dictionary<string, string> cache, string text, TextBlock probe, Func<string, Func<string, bool>, string> shorten)
    {
        if (cache.TryGetValue(text, out var known))
        {
            return known;
        }

        if (cache.Count > 2000)
        {
            cache.Clear();
        }

        bool Fits(string candidate)
        {
            probe.Text = candidate;
            probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width <= NameWidth;
        }

        return cache[text] = shorten(text, Fits);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>The shell's icon for the row — from the cache, or asked for on the icon thread and filled in when it arrives.</summary>
    private void ShowIcon(FileHit hit, Image image, UIElement glyph)
    {
        var key = ShellIcons.KeyFor(hit);
        if (_icons.TryGetValue(key, out var known))
        {
            image.Source = known;
            glyph.Visibility = Visibility.Collapsed;
            return;
        }

        if (_waiting.TryGetValue(key, out var list))
        {
            list.Add((image, glyph));   // already asked for this type in this list
            return;
        }

        _waiting[key] = [(image, glyph)];
        _shellIcons.Request(hit, (doneKey, icon) => DispatcherQueue.TryEnqueue(() => IconArrived(doneKey, icon)));
    }

    private void IconArrived(string key, ShellIcons.Icon? icon)
    {
        if (icon is null)
        {
            _waiting.Remove(key);
            return;
        }

        if (_icons.Count > 400)
        {
            _icons.Clear();   // apps and shortcuts each have their own — don't let a long session pile them up
        }

        var bitmap = new WriteableBitmap(icon.Width, icon.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(icon.Pixels, 0, icon.Pixels.Length);
        }

        bitmap.Invalidate();
        _icons[key] = bitmap;
        if (_waiting.Remove(key, out var targets))
        {
            foreach (var (image, glyph) in targets)
            {
                image.Source = bitmap;
                glyph.Visibility = Visibility.Collapsed;
            }
        }
    }
}
