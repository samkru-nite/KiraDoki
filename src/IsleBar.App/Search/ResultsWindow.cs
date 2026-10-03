using IsleBar.App.Interop;
using IsleBar.App.Options;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace IsleBar.App.Search;

/// <summary>
/// File search results list. Pops up right above the search box (same position and look as the Python version's <c>show_results</c>).
/// <b>Does not take focus</b> (NOACTIVATE) — typing must continue in the input box while the list is shown.
/// Selection (↑↓) and open (Enter) keys are received by the input box and forwarded to this window.
/// </summary>
internal sealed class ResultsWindow : Window
{
    private const int WidthDip = 480;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    private readonly StackPanel _list = new() { Padding = new Thickness(6), Spacing = 0 };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Grid _bar = new() { Padding = new Thickness(8, 4, 8, 6) };
    private readonly StackPanel _sorts = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Grid _root = new();
    private readonly List<Border> _rows = [];
    private IReadOnlyList<FileHit> _hits = [];
    private int _anchorX;
    private int _bandTop;

    public ResultsWindow()
    {
        SystemBackdrop = new DesktopAcrylicBackdrop();
        PopupPlacement.MakeToolPopup(this);
        // results scroll; under them a bar with the sort choice and "Open in Everything" (user 10-04)
        _scroll.Content = _list;
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(_bar, 1);
        _root.Children.Add(_scroll);
        _root.Children.Add(_bar);
        Content = _root;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var ex = NativeMethods.Unsigned32(NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE));
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, NativeMethods.Signed32(ex | WS_EX_NOACTIVATE));
    }

    /// <summary>
    /// Whether the mouse is over the list right now. A click on it still takes activation from the bar (NOACTIVATE or not), and
    /// the bar used to end typing on that — closing the list before the click arrived, so clicking a result did nothing (10-03).
    /// </summary>
    public bool UnderCursor() => PopupPlacement.UnderCursor(this);

    /// <summary>Called when a row is clicked (mouse). Argument = row index.</summary>
    public event Action<int>? RowClicked;

    /// <summary>A sort button under the list was clicked. Argument = the sort setting value.</summary>
    public event Action<string>? SortChosen;

    /// <summary>"Open in Everything" was clicked.</summary>
    public event Action? OpenInEverything;

    public int Selected { get; private set; }

    public FileHit? SelectedHit => Selected >= 0 && Selected < _hits.Count ? _hits[Selected] : null;

    public bool HasHits => _hits.Count > 0;

    /// <summary>Shows results (or a single hint message). Starts with the first row selected.</summary>
    /// <param name="sort">Current sort setting value — its button is shown as chosen.</param>
    public void Show(IReadOnlyList<FileHit> hits, string? note, int anchorX, int bandTop, string sort, Core.Localization.LanguageStrings text)
    {
        BuildBar(sort, text);
        // the list scrolls once it would reach the top of the screen (the bar under it stays)
        _scroll.MaxHeight = Math.Max(120, PopupPlacement.MaxHeightAbove(anchorX, bandTop) - 60);
        (_hits, _anchorX, _bandTop, Selected) = (hits, anchorX, bandTop, 0);
        _list.Children.Clear();
        _rows.Clear();

        for (var i = 0; i < hits.Count; i++)
        {
            _list.Children.Add(Row(hits[i], i));
        }

        if (note is not null)
        {
            _list.Children.Add(new TextBlock
            {
                Text = note,
                Opacity = 0.7,
                Margin = new Thickness(12, 10, 12, 10),
            });
        }

        Paint();
        _scroll.ChangeView(null, 0, null, disableAnimation: true);
        _root.UpdateLayout();
        PopupPlacement.SizeToContent(this, _root, WidthDip);
        PopupPlacement.PlaceAbove(this, _anchorX, _bandTop);
        AppWindow.Show(activateWindow: false);
    }

    public void Hide() => AppWindow.Hide();

    /// <summary>Moves the selected row. False if the list is empty.</summary>
    public bool Move(int delta)
    {
        if (_hits.Count == 0)
        {
            return false;
        }

        Selected = Math.Clamp(Selected + delta, 0, _hits.Count - 1);
        Paint();
        _rows[Selected].StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });   // ↑↓ past the visible rows scrolls
        return true;
    }

    private Border Row(FileHit hit, int index)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 7, 10, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new FontIcon
        {
            Glyph = hit.IsFolder ? "" : "",   // folder / document (same glyphs as the Python version)
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = hit.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = hit.Directory,
            FontSize = 11,
            Opacity = 0.65,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var row = new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(4),
            MaxWidth = WidthDip - 12,
        };
        row.PointerEntered += (_, _) =>
        {
            Selected = index;
            Paint();
        };
        row.PointerPressed += (_, _) => RowClicked?.Invoke(index);
        _rows.Add(row);
        return row;
    }

    /// <summary>Bottom bar: sort buttons (the chosen one highlighted) on the left, "Open in Everything" on the right.</summary>
    private void BuildBar(string sort, Core.Localization.LanguageStrings text)
    {
        _bar.Children.Clear();
        _sorts.Children.Clear();
        var chosen = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var (value, label) in new[]
                 {
                     (Core.Search.FileSearchQuery.SortName, text.SortName),
                     (Core.Search.FileSearchQuery.SortModified, text.SortModified),
                     (Core.Search.FileSearchQuery.SortSize, text.SortSize),
                 })
        {
            var on = value == sort;
            var button = BarButton(new TextBlock { Text = label, FontSize = 12, Opacity = on ? 1 : 0.7 });
            button.Background = on ? chosen : clear;
            button.Click += (_, _) => SortChosen?.Invoke(value);
            _sorts.Children.Add(button);
        }

        _bar.Children.Add(_sorts);

        var open = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        open.Children.Add(new FontIcon { Glyph = "", FontSize = 12 });   // open in new window
        open.Children.Add(new TextBlock { Text = text.OpenInEverything, FontSize = 12 });
        var everything = BarButton(open);
        everything.Background = clear;
        everything.Click += (_, _) => OpenInEverything?.Invoke();
        Grid.SetColumn(everything, 1);
        _bar.Children.Add(everything);
    }

    private static Button BarButton(UIElement content) => new()
    {
        Content = content,
        Padding = new Thickness(8, 3, 8, 4),
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(4),
        MinHeight = 0,
    };

    /// <summary>Only the selected row gets a light background + left accent bar (like Windows 11 lists).</summary>
    private void Paint()
    {
        var subtle = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        for (var i = 0; i < _rows.Count; i++)
        {
            var on = i == Selected;
            _rows[i].Background = on ? subtle : clear;
            _rows[i].BorderBrush = on ? accent : clear;
            _rows[i].BorderThickness = on ? new Thickness(3, 0, 0, 0) : new Thickness(0);
        }
    }
}
