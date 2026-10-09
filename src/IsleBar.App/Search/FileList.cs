using IsleBar.App.Interop;
using IsleBar.Core.Localization;
using IsleBar.Core.Search;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace IsleBar.App.Search;

/// <summary>
/// The file search list inside the input panel (TODO 7.9 H1b, user 10-09) — the old results window's list moved in: Explorer-style
/// headers that sort when clicked, the system ListView for selection/hover/scrolling, the shell's icons, long names and paths
/// shortened in the middle, right-click for open / file location / copy / Everything. New for the 409-wide panel: the cards' type
/// (14 / 12), the time and the size side by side in their own columns, centred on the row's two lines, the time written short
/// (<see cref="FileListText.ShortTime"/>, the full one in its tip), and a fixed height of <c>rows</c> rows so the panel doesn't jump
/// as results come and go.
/// </summary>
internal sealed partial class FileList : UserControl
{
    // Type as the cards' rule (user 10-09): the name 14/20, the path, time and size 12/16, the header 12. Spacing by ink, as on a
    // screenshot: the 12 px header's ink starts 3 below its 16 line and ends 1 above it (measured 10-09) — the panel's 8 + 5 + 3 =
    // 16 from the panel's outer edge = 15 inside its 1-wide edge line; a row's ink starts about 8 below its slot (1 + 3 centring +
    // ~4 in the 20 line) — header ink → first row ink = 1 + 2 + 8 = 11 (user 10-09). The log "panel: spacing" checks both.
    private const double HeaderTop = 5;
    private const double HeaderBottom = 0;   // measured 10-09 on the 14/12 rows: 2 gave 13, not 11
    private const double HeaderText = 16;     // line height of the 12 px header labels
    public const double HeaderHeight = HeaderTop + HeaderText + HeaderBottom;
    private const double HoverPad = 4;        // hover background around a header label: the same above and below it
    private const double IconSize = 20;
    private const double ColumnGap = 12;      // the same between every column (user 10-04)

    // Across, measured on the user's screenshots (10-09, inside the panel's 1-wide edge line). A row's hover fill is drawn 4 inside
    // the row; the list reaches 1 past the panel's 8 (ListBleed), so the rows start 6 in and their hover fill 10 (user: 10, was 11).
    // The shell's icons carry ~3 of see-through edge: an icon box 12 in shows its picture from ~15 (user: icons 15 from the edge,
    // was 18). A size's ink ends ~1 past its box: the box 14 in ends the ink at 15 (user: 15, was 16).
    private const double ListBleed = 1;

    private const double ItemLeft = 7;   // user 10-09: the icons (and the names with them) 1 further in — 6 showed them at 15

    private const double ItemRight = 9;   // user 10-09: the sizes (and the times) 1 further in — 8 ended them at 15

    /// <summary>The "Size" header's ink ended 1 past the numbers' (measured 10-09): pulled in by as much.</summary>
    private const double SizeHeaderNudge = 1;

    /// <summary>The icon box → the name: the picture ends ~3.5 inside its box, so the name's ink starts 15 after it (user 10-09: as
    /// far as the icon is from the panel's edge).</summary>
    private const double IconGap = 11;

    /// <summary>The header's "Name" starts with the icons' pictures (15), not their boxes (user 10-09: the header follows the rows).</summary>
    private const double HeaderLeft = 8.5;

    /// <summary>Time and size side by side, each its own column (user 10-09): "2025/10/28" in 12 px is ~61, "238.8 MB" ~55.</summary>
    private const double TimeWidth = 64;

    private const double SizeWidth = 58;

    private const double ArrowWidth = 8;

    /// <summary>One row: its own 42 + 1 above and below (set here, so the panel's height is known before any row exists).</summary>
    public const double RowPitch = 44;

    private const double RowHeight = RowPitch - 2;

    /// <summary>Room left for the name and path: 409 − 16 + 2 − 7 − 9 − 20 − 11 − 64 − 58 − 2 × 12 = 202.</summary>
    private const double NameWidth = PanelMotion.Width - (2 * PanelMotion.Pad) + (2 * ListBleed) - ItemLeft - ItemRight - IconSize - IconGap - TimeWidth - SizeWidth - (2 * ColumnGap);

    private readonly ListView _list = new()
    {
        SelectionMode = ListViewSelectionMode.Single,
        IsItemClickEnabled = true,
        IsTabStop = false,
        AllowFocusOnInteraction = false,   // a click on a row / the scrollbar leaves the keyboard (and the caret) in the input box
        Padding = new Thickness(0),
    };

    private readonly Grid _header = new()
    {
        Padding = new Thickness(HeaderLeft, HeaderTop, ItemRight, HeaderBottom),
        ColumnSpacing = ColumnGap,
        Height = HeaderHeight,
    };

    private readonly StackPanel _empty = new() { Padding = new Thickness(16, 10, 16, 14) };
    private readonly Grid _root = new();
    private readonly MenuFlyout _menu = new();
    private readonly ShellIcons _shellIcons = new();

    // measuring text is the slow part of building a row — the same names and folders come back on every keystroke
    private readonly TextBlock _nameProbe = new() { FontSize = 14, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock _pathProbe = new() { FontSize = 12, TextWrapping = TextWrapping.NoWrap };
    private readonly Dictionary<string, string> _shortNames = [];
    private readonly Dictionary<string, string> _shortPaths = [];

    /// <summary>Icons by <see cref="ShellIcons.KeyFor"/>; the images still waiting for one.</summary>
    private readonly Dictionary<string, ImageSource> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(Image Image, UIElement Glyph)>> _waiting = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<FileHit> _hits = [];
    private LanguageStrings? _text;
    private int _menuIndex = -1;
    private bool _menuOpen;

    public FileList()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_list, 1);
        Grid.SetRow(_empty, 1);
        _root.Children.Add(_header);
        _root.Children.Add(_list);
        _root.Children.Add(_empty);
        Content = _root;
        IsTabStop = false;
        Margin = new Thickness(-ListBleed, 0, -ListBleed, 0);

        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconSize) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimeWidth) });
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
        _menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            KeepTyping?.Invoke();
        };
        Unloaded += (_, _) => _shellIcons.Dispose();
    }

    /// <summary>The right-click menu is open — a press on it (it may reach past the panel) mustn't put the panel away.</summary>
    public bool MenuOpen => _menuOpen;

    /// <summary>A row was clicked (mouse) or "Open" chosen. Argument = row index.</summary>
    public event Action<int>? RowClicked;

    /// <summary>"Open file location" from the menu. Argument = row index.</summary>
    public event Action<int>? RevealClicked;

    /// <summary>A column header was clicked. Argument = the sort setting value.</summary>
    public event Action<string>? SortChosen;

    /// <summary>"Open in Everything" (menu, or the link under an empty result).</summary>
    public event Action? OpenInEverything;

    /// <summary>Something on the list was used that leaves the search going (a copy, a sort) — the input box takes the keyboard back.</summary>
    public event Action? KeepTyping;

    public int Selected { get; private set; } = -1;

    public FileHit? SelectedHit => Selected >= 0 && Selected < _hits.Count ? _hits[Selected] : null;

    /// <summary>The top row — what Enter opens when nothing is selected (as the Start menu's search does).</summary>
    public FileHit? FirstHit => _hits.Count > 0 ? _hits[0] : null;

    /// <summary>The list's height for <paramref name="rows"/> rows under the header.</summary>
    public static double HeightFor(int rows) => HeaderHeight + (rows * RowPitch);

    /// <summary>
    /// Shows results (or a single hint line under the header). Nothing starts selected — ↓ selects the first row, Enter opens
    /// the top one (user 10-04: a pre-selected first row read as a choice already made).
    /// </summary>
    /// <param name="noteAction">What clicking the hint does (it's then drawn as a link): search in Everything, start it…</param>
    /// <param name="sort">Current sort setting value — its header shows the arrow.</param>
    public void Show(IReadOnlyList<FileHit> hits, string? note, Action? noteAction, string sort, LanguageStrings text)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        (_hits, Selected, _text) = (hits, -1, text);
        _nameProbe.FontFamily = FontFamily;   // measure in the font the list is drawn in
        _pathProbe.FontFamily = FontFamily;

        BuildHeader(sort, text);
        _waiting.Clear();
        _list.Items.Clear();
        var now = DateTime.Now;
        for (var i = 0; i < hits.Count; i++)
        {
            _list.Items.Add(Row(hits[i], i, now));
        }

        BuildEmpty(hits.Count == 0 ? note : null, noteAction);
        // the header always stays — the frame doesn't change, only what's in it (user 10-04)
        _list.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.SelectedIndex = -1;
        if (hits.Count > 0)
        {
            _list.ScrollIntoView(_list.Items[0]);
        }

        if (clock.ElapsedMilliseconds >= 30)
        {
            TaskbarHost.Log($"file search: drew {hits.Count} rows in {clock.ElapsedMilliseconds} ms");
        }
    }

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

    /// <summary>
    /// Explorer-style headers over their columns: name · time · size, each lined up with the values under it — name and time from
    /// the left, size from the right (its numbers are right-aligned, as in Explorer: user 10-09). All keep room for the arrow, so
    /// nothing moves when the sort changes; the sorted one is white and shows the arrow — on the size its arrow goes before the word,
    /// so the word's right end meets the numbers' (user 10-09).
    /// </summary>
    private void BuildHeader(string sort, LanguageStrings text)
    {
        _header.Children.Clear();
        AddHeader(0, 2, HeaderButton(FileSearchQuery.SortName, text.SortName, sort));
        AddHeader(2, 1, HeaderButton(FileSearchQuery.SortModified, text.SortTime, sort));
        AddHeader(3, 1, HeaderButton(FileSearchQuery.SortSize, text.SortSize, sort, right: true));
    }

    private void AddHeader(int column, int span, Button button)
    {
        Grid.SetColumn(button, column);
        Grid.SetColumnSpan(button, span);
        _header.Children.Add(button);
    }

    private Button HeaderButton(string value, string label, string sort, bool right = false)
    {
        const double pad = 6;
        var on = value == sort;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Brush(on ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Insert(right ? 0 : 1, new FontIcon
        {
            Glyph = "\uE70D",   // chevron down — the same mark means "sorted by this" (user 10-04)
            FontSize = 8,
            Width = ArrowWidth,
            Foreground = Brush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = on ? 1 : 0,   // there but unseen: the labels keep their places whichever is sorted (user 10-09)
        });

        var button = new Button
        {
            Content = content,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            // the hover background hugs the label with the same room on every side; the negative margins pull the button out by its
            // padding, so the label itself stays exactly where the layout puts it
            Padding = new Thickness(pad, HoverPad, pad, HoverPad),
            Margin = new Thickness(-pad, -HoverPad, -pad + (right ? SizeHeaderNudge : 0), -HoverPad),
            Height = HeaderText + (2 * HoverPad),
            MinHeight = 0,
            IsTabStop = false,
            AllowFocusOnInteraction = false,
            VerticalAlignment = VerticalAlignment.Top,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
        button.Click += (_, _) =>
        {
            SortChosen?.Invoke(value);
            KeepTyping?.Invoke();
        };
        return button;
    }

    // ---------------- rows ----------------

    private ListViewItem Row(FileHit hit, int index, DateTime now)
    {
        // icon · name over path · time · size — the time and the size centred on the row's two lines, like Explorer (user 10-09)
        var grid = new Grid { ColumnSpacing = ColumnGap, Tag = index, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconSize) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimeWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SizeWidth) });

        // icon: a plain glyph until the shell's icon arrives (usually at once — they're shared per file type)
        var iconSlot = new Grid { Width = IconSize, Height = IconSize, VerticalAlignment = VerticalAlignment.Center };
        var glyph = new FontIcon { Glyph = hit.IsFolder ? "\uE8B7" : "\uE8A5", FontSize = 16 };
        var image = new Image { Width = IconSize, Height = IconSize, Stretch = Stretch.Uniform };
        iconSlot.Children.Add(glyph);
        iconSlot.Children.Add(image);
        grid.Children.Add(iconSlot);
        ShowIcon(hit, image, glyph);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(IconGap - ColumnGap, 0, 0, 0) };
        var name = Line(Shorten(_shortNames, hit.Name, _nameProbe, FileListText.MiddleEllipsisName), 14, 20, "TextFillColorPrimaryBrush");
        if (name.Text != hit.Name)
        {
            ToolTipService.SetToolTip(name, hit.Name);
        }

        var path = Line(Shorten(_shortPaths, hit.Directory, _pathProbe, FileListText.MiddleEllipsis), 12, 16, "TextFillColorSecondaryBrush");
        if (path.Text != hit.Directory)
        {
            ToolTipService.SetToolTip(path, hit.Directory);
        }

        names.Children.Add(name);
        names.Children.Add(path);
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);

        var culture = System.Globalization.CultureInfo.CurrentCulture;
        // the time in the name's white, the size in the path's grey (user 10-09: two greys ran together)
        var time = Line(hit.Modified is { } when ? FileListText.ShortTime(when, now, _text?.Yesterday ?? "", culture) : string.Empty, 12, 16, "TextFillColorPrimaryBrush");
        if (hit.Modified is { } full)
        {
            ToolTipService.SetToolTip(time, full.ToString("g", culture));   // the full time on hover
        }

        time.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(time, 2);
        grid.Children.Add(time);
        var size = Line(hit.Size is { } bytes ? FileListText.FormatSize(bytes) : string.Empty, 12, 16, "TextFillColorSecondaryBrush");
        size.VerticalAlignment = VerticalAlignment.Center;
        size.HorizontalAlignment = HorizontalAlignment.Right;   // numbers right-aligned, as in Explorer (user 10-09)
        Grid.SetColumn(size, 3);
        grid.Children.Add(size);

        return new ListViewItem
        {
            Content = grid,
            Tag = index,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(ItemLeft, 0, ItemRight, 0),
            Margin = new Thickness(0, 1, 0, 1),
            MinHeight = 0,
            Height = RowHeight,
            ContextFlyout = _menu,
        };
    }

    /// <summary>One line of a row, in the cards' type: <paramref name="size"/> on a <paramref name="line"/> high line, left-aligned.</summary>
    private static TextBlock Line(string text, double size, double line, string brush) => new()
    {
        Text = text,
        FontSize = size,
        LineHeight = line,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,   // only if the measurement was a hair off
        Foreground = Brush(brush),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

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

        Add(_text.MenuOpen, "\uE8E5", () => RowClicked?.Invoke(index));
        Add(_text.MenuOpenFolder, "\uE838", () => RevealClicked?.Invoke(index));
        Add(_text.MenuCopyPath, "\uE8C8", () => Copy(hit.FullPath));
        Add(_text.MenuCopyName, "\uE8C8", () => Copy(hit.Name));
        _menu.Items.Add(new MenuFlyoutSeparator());
        Add(_text.OpenInEverything, "\uE721", () => OpenInEverything?.Invoke());
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
                FontSize = 14,
                Foreground = Brush("TextFillColorSecondaryBrush"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        var link = new HyperlinkButton
        {
            Content = new TextBlock { Text = note, FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
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
