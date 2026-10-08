using IsleBar.Core.Ui;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace IsleBar.App.IslandUi;

/// <summary>The colours a lifted card is drawn in (of the moment: light / dark, the system accent).</summary>
internal sealed record SheetPalette(Color Card, Color Fore, Color Hint, Color Ok, Color Waiting, Color Bad, Color Accent, Color Track, bool Dark);

/// <summary>
/// Draws a <see cref="CardSheet"/> (TODO 7.9 F2): the four zones — source row, middle, progress, buttons — in the card's size, by
/// the numbers in <see cref="CardSheets"/>. Only three sizes of type (12 / 14 / 40), at most one bold thing per zone, all left-aligned.
/// Whatever only changes from second to second (a timer's number, "2 min ago", a bar) is changed in place by <see cref="Update"/>,
/// so a button under the mouse keeps its hover and the text keeps its scroll.
/// </summary>
internal sealed class SheetView
{
    private const string Mono = "Cascadia Mono, Consolas";
    private readonly SheetPalette _palette;
    private readonly string? _language;
    private readonly double _width;
    private readonly TextBlock _stateText = Note();
    private readonly ScrollViewer? _scroll;
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _fadeTop = new() { Height = CardSheets.Fade, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _fadeBottom = new() { Height = CardSheets.Fade, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
    private readonly FrameworkElement _middle;
    private readonly List<TextBlock> _texts = [];   // title, paragraphs, raw — in that order (text layout)
    private TextBlock? _number;
    private TextBlock? _barLeft;
    private TextBlock? _barRight;
    private Border? _bar;
    private Border? _art;
    private readonly List<TextBlock> _songText = [];   // title, artist
    private readonly List<Button> _buttons = [];
    private SheetAction[] _actions = [];
    private Grid? _sourceRow;
    private CardSheet _sheet;

    /// <summary>The card's contents, inset by its padding; set its size with <see cref="SetSize"/>.</summary>
    public Grid Root { get; } = new();

    /// <summary>A button was clicked.</summary>
    public Action<SheetAction>? Act { get; set; }

    public SheetView(CardSheet sheet, UIElement icon, ImageSource? cover, SheetPalette palette, string? language, double width)
    {
        _sheet = sheet;
        _palette = palette;
        _language = language;
        _width = width - (2 * CardSheets.Padding);
        Root.RequestedTheme = palette.Dark ? ElementTheme.Dark : ElementTheme.Light;
        Root.Margin = new Thickness(CardSheets.Padding);
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Root.Children.Add(SourceRow(sheet, icon));
        _middle = sheet.Layout switch
        {
            SheetLayout.Number => NumberMiddle(sheet),
            SheetLayout.Media => MediaMiddle(sheet, cover),
            _ => TextMiddle(sheet, out _scroll),
        };
        _middle.Margin = new Thickness(0, CardSheets.Gap, 0, 0);
        Grid.SetRow(_middle, 1);
        Root.Children.Add(_middle);
        if (sheet.Progress is { } progress)
        {
            var bar = ProgressZone(progress);
            Grid.SetRow(bar, 2);
            Root.Children.Add(bar);
        }

        if (sheet.Buttons.Count > 0)
        {
            var row = Buttons(sheet.Buttons);
            Grid.SetRow(row, 3);
            Root.Children.Add(row);
        }

        ShowState(sheet);
    }

    /// <summary>The card's outer size (dip): the contents take it less the padding.</summary>
    public void SetSize(double width, double height)
    {
        Root.Width = Math.Max(0, width - (2 * CardSheets.Padding));
        Root.Height = Math.Max(0, height - (2 * CardSheets.Padding));
        if (_scroll is not null)
        {
            _scroll.VerticalScrollMode = height > LiftMotion.Height ? ScrollMode.Enabled : ScrollMode.Disabled;   // 3:2 never scrolls (user 10-08)
            _scroll.VerticalScrollBarVisibility = height > LiftMotion.Height ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            _scroll.UpdateLayout();
            Fades();
        }
    }

    /// <summary>How high the middle's contents are at the card's width (dip) — for choosing 3:2 or 3:4.</summary>
    public double ContentHeight()
    {
        var inner = _scroll?.Content as FrameworkElement ?? _middle;
        inner.Measure(new Windows.Foundation.Size(_width, double.PositiveInfinity));
        return inner.DesiredSize.Height;
    }

    /// <summary>Same layout as the sheet drawn (same words, same buttons): only the moving parts change. False = build it again.</summary>
    public bool Update(CardSheet next)
    {
        if (StructureKey(next) != StructureKey(_sheet))
        {
            return false;
        }

        _sheet = next;
        ShowState(next);
        if (next.Layout == SheetLayout.Text)
        {
            // a download's amount, a longer message: the words change, the blocks stay
            var words = new List<string>();
            if (next.Title is { } title)
            {
                words.Add(title);
            }

            words.AddRange(next.Body);
            if (next.Raw is { } raw)
            {
                words.Add(raw);
            }

            for (var i = 0; i < words.Count && i < _texts.Count; i++)
            {
                if (_texts[i].Text != words[i])
                {
                    _texts[i].Text = words[i];
                }
            }
        }

        if (next.Layout == SheetLayout.Media)
        {
            // the next song on the same player: same card, new words (user 10-08: they stayed until the card was opened again)
            var lines = new List<string> { next.Title ?? string.Empty };
            lines.AddRange(next.Body);
            for (var i = 0; i < lines.Count && i < _songText.Count; i++)
            {
                if (_songText[i].Text != lines[i])
                {
                    _songText[i].Text = lines[i];
                }
            }
        }

        if (_number is not null)
        {
            SetNumber(_number, next);
        }

        // pause ↔ resume, play ↔ pause: the same button, other words / icon
        for (var i = 0; i < next.Buttons.Count && i < _buttons.Count; i++)
        {
            _actions[i] = next.Buttons[i].Action;
            SetButton(_buttons[i], next.Buttons[i]);
        }

        if (next.Progress is { } p)
        {
            if (_barLeft is not null)
            {
                _barLeft.Text = p.Left ?? string.Empty;
            }

            if (_barRight is not null)
            {
                _barRight.Text = p.Right ?? string.Empty;
            }

            if (_bar is not null)
            {
                _bar.Width = _width * Math.Clamp(p.Fraction, 0, 1);
                _bar.Background = Brush(BarColor(p.Tone));
            }
        }

        return true;
    }

    /// <summary>What can't change in place: which pieces of text there are, the buttons, which zones there are.</summary>
    private static string StructureKey(CardSheet s) => string.Join("\u0001",
        s.Layout, s.Source, s.Title is null, s.Body.Count, s.Raw is null, s.Progress is null,
        s.Progress is { Left: null, Right: null }, s.Buttons.Count);

    /// <summary>The source row's icon, new (a timer paused or resumed: its hand stops or turns again).</summary>
    public void SetIcon(UIElement icon)
    {
        if (_sourceRow is not { } row || row.Children.Count == 0)
        {
            return;
        }

        if (icon is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
        }

        row.Children[0] = icon;
    }

    /// <summary>The song's cover, new (the next song).</summary>
    public void SetCover(ImageSource? cover)
    {
        if (_art is null || ReferenceEquals((_art.Background as ImageBrush)?.ImageSource, cover) || (cover is null && _art.Child is not null))
        {
            return;
        }

        FillArt(_art, cover);
    }

    // ---------------- zones ----------------

    private FrameworkElement SourceRow(CardSheet sheet, UIElement icon)
    {
        var row = new Grid { Height = CardSheets.SourceHeight, ColumnSpacing = 8 };
        _sourceRow = row;
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (icon is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
        }

        row.Children.Add(icon);
        var source = Note();
        source.Text = sheet.Source;
        source.FontFamily = Fonts(sheet.Source).Normal;
        source.Foreground = Brush(_palette.Hint);
        source.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(source, 1);
        row.Children.Add(source);
        Grid.SetColumn(_stateText, 2);
        row.Children.Add(_stateText);
        return row;
    }

    /// <summary>"完成 · 2 分钟前": the state word bold in its colour, then the time in grey.</summary>
    private void ShowState(CardSheet sheet)
    {
        _stateText.Inlines.Clear();
        var (normal, strong) = Fonts(sheet.State + sheet.Time);
        if (sheet.State is { } state)
        {
            _stateText.Inlines.Add(new Run { Text = state, FontFamily = strong, FontWeight = FontWeights.Medium, Foreground = Brush(ToneColor(sheet.Tone)) });
        }

        if (sheet.Time is { } time)
        {
            _stateText.Inlines.Add(new Run { Text = sheet.State is null ? time : $" · {time}", FontFamily = normal, Foreground = Brush(_palette.Hint) });
        }
    }

    private FrameworkElement TextMiddle(CardSheet sheet, out ScrollViewer scroll)
    {
        var stack = new StackPanel();
        if (sheet.Title is { } title)
        {
            var t = Text(title, strong: true);
            _texts.Add(t);
            stack.Children.Add(t);
        }

        foreach (var paragraph in sheet.Body)
        {
            var p = Text(paragraph, strong: false);
            _texts.Add(p);
            p.Foreground = Brush(_palette.Hint);
            p.Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : sheet.Title is not null && stack.Children.Count == 1 ? CardSheets.TitleBodyGap : CardSheets.ParagraphGap, 0, 0);
            stack.Children.Add(p);
        }

        if (sheet.Raw is { } raw)
        {
            var code = new TextBlock
            {
                Text = raw, FontFamily = AppFonts.Family(Mono), FontSize = CardSheets.NoteSize, LineHeight = CardSheets.NoteLine,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap, Foreground = Brush(_palette.Fore),
            };
            _texts.Add(code);
            stack.Children.Add(new Border
            {
                Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : 6, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(4),
                Background = Brush(Faint(_palette.Fore, 0.06)),
                BorderBrush = Brush(Faint(_palette.Fore, 0.09)),
                BorderThickness = new Thickness(1),
                Child = code,
            });
        }

        scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroll.ViewChanged += (_, _) => Fades();
        scroll.SizeChanged += (_, _) => Fades();
        var fade = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        fade.GradientStops.Add(new GradientStop { Color = _palette.Card, Offset = 0 });
        fade.GradientStops.Add(new GradientStop { Color = Faint(_palette.Card, 0), Offset = 1 });
        var fadeUp = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 1), EndPoint = new Windows.Foundation.Point(0, 0) };
        fadeUp.GradientStops.Add(new GradientStop { Color = _palette.Card, Offset = 0 });
        fadeUp.GradientStops.Add(new GradientStop { Color = Faint(_palette.Card, 0), Offset = 1 });
        _fadeTop.Fill = fade;
        _fadeBottom.Fill = fadeUp;
        _fadeTop.Opacity = _fadeBottom.Opacity = 0;
        var box = new Grid();
        box.Children.Add(scroll);
        box.Children.Add(_fadeTop);
        box.Children.Add(_fadeBottom);
        return box;
    }

    /// <summary>The fades only on a side that still has more to scroll (user 10-08).</summary>
    private void Fades()
    {
        if (_scroll is not { } s)
        {
            return;
        }

        var scrolls = s.VerticalScrollMode == ScrollMode.Enabled;
        _fadeTop.Opacity = scrolls && s.VerticalOffset > 1 ? 1 : 0;
        _fadeBottom.Opacity = scrolls && s.VerticalOffset < s.ScrollableHeight - 1 ? 1 : 0;
    }

    /// <summary>One big number, left-aligned, the caption after it on its baseline, the pair centred top to bottom (user 10-08, B).</summary>
    private FrameworkElement NumberMiddle(CardSheet sheet)
    {
        _number = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        SetNumber(_number, sheet);
        return new Grid { Children = { _number } };
    }

    private void SetNumber(TextBlock block, CardSheet sheet)
    {
        block.Inlines.Clear();
        var (normal, strong) = Fonts(sheet.Number + sheet.NumberCaption);
        block.Inlines.Add(new Run
        {
            Text = sheet.Number ?? string.Empty, FontSize = CardSheets.NumberSize, FontFamily = strong, FontWeight = FontWeights.Medium,
            Foreground = Brush(_palette.Fore),
        });
        if (sheet.NumberCaption is { } caption)
        {
            // one text block, so the two share a baseline; the en spaces make the 8 between them at 12
            block.Inlines.Add(new Run { Text = "  " + caption, FontSize = CardSheets.NoteSize, FontFamily = normal, Foreground = Brush(_palette.Hint) });
        }
    }

    /// <summary>A song: the cover sized to the middle (56 beside a bar, 72 without), the title (two lines at most) and the artist.</summary>
    private FrameworkElement MediaMiddle(CardSheet sheet, ImageSource? cover)
    {
        var size = CardSheets.CoverSize(sheet.Progress is not null);
        var row = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var art = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(6) };
        FillArt(art, cover);
        _art = art;

        row.Children.Add(art);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text(sheet.Title ?? string.Empty, strong: true);
        title.MaxLines = 2;   // music stays 3:2: the one place a line is cut (user 10-08)
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(title);
        _songText.Add(title);
        foreach (var line in sheet.Body)
        {
            var artist = Text(line, strong: false);
            artist.Foreground = Brush(_palette.Hint);
            artist.Margin = new Thickness(0, CardSheets.TitleBodyGap, 0, 0);
            artist.MaxLines = 1;
            artist.TextTrimming = TextTrimming.CharacterEllipsis;
            words.Children.Add(artist);
            _songText.Add(artist);
        }

        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        return row;
    }

    private void FillArt(Border art, ImageSource? cover)
    {
        art.Background = cover is null ? Brush(Faint(_palette.Fore, 0.06)) : new ImageBrush { ImageSource = cover, Stretch = Stretch.UniformToFill };
        art.Child = cover is null
            ? new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), Glyph = "", FontSize = 24, Foreground = Brush(_palette.Hint) }
            : null;
    }

    private FrameworkElement ProgressZone(SheetProgress progress)
    {
        var zone = new StackPanel { Margin = new Thickness(0, CardSheets.Gap, 0, 0), Spacing = CardSheets.BarLabelGap };
        if (progress.Left is not null || progress.Right is not null)
        {
            var labels = new Grid();
            _barLeft = Note();
            _barLeft.Text = progress.Left ?? string.Empty;
            _barLeft.FontFamily = Fonts(progress.Left).Normal;
            _barLeft.Foreground = Brush(_palette.Hint);
            _barRight = Note();
            _barRight.Text = progress.Right ?? string.Empty;
            _barRight.FontFamily = Fonts(progress.Right).Normal;
            _barRight.Foreground = Brush(_palette.Hint);
            _barRight.HorizontalAlignment = HorizontalAlignment.Right;
            labels.Children.Add(_barLeft);
            labels.Children.Add(_barRight);
            zone.Children.Add(labels);
        }

        var track = new Grid { Height = CardSheets.BarHeight };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(CardSheets.BarHeight / 2), Background = Brush(_palette.Track) });
        _bar = new Border
        {
            CornerRadius = new CornerRadius(CardSheets.BarHeight / 2), HorizontalAlignment = HorizontalAlignment.Left,
            Width = _width * Math.Clamp(progress.Fraction, 0, 1), Background = Brush(BarColor(progress.Tone)),
        };
        track.Children.Add(_bar);
        zone.Children.Add(track);
        return zone;
    }

    /// <summary>Equal widths sharing the row, 8 apart; Close (or Hide) last, on the right (user 10-08).</summary>
    private FrameworkElement Buttons(IReadOnlyList<SheetButton> buttons)
    {
        var row = new Grid { Margin = new Thickness(0, CardSheets.Gap, 0, 0), ColumnSpacing = CardSheets.ButtonGap };
        _actions = buttons.Select(b => b.Action).ToArray();
        for (var i = 0; i < buttons.Count; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var button = new Button
            {
                Height = CardSheets.ButtonHeight,
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(4),
                IsTabStop = false,
            };
            SetButton(button, buttons[i]);
            var index = i;
            button.Click += (_, _) => Act?.Invoke(_actions[index]);   // read at click time: pause becomes resume in place
            Grid.SetColumn(button, i);
            row.Children.Add(button);
            _buttons.Add(button);
        }

        return row;
    }

    /// <summary>A button's words or icon (a media button: its icon, its name as a tooltip). Unchanged ones are left alone.</summary>
    private void SetButton(Button button, SheetButton b)
    {
        if (b.Glyph is { } glyph)
        {
            if (button.Content is FontIcon icon)
            {
                if (icon.Glyph != glyph)
                {
                    icon.Glyph = glyph;
                }
            }
            else
            {
                button.Content = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), Glyph = glyph, FontSize = 16 };
            }

            ToolTipService.SetToolTip(button, b.Label);
            return;
        }

        var label = b.Label ?? string.Empty;
        if (button.Content is TextBlock text)
        {
            if (text.Text != label)
            {
                text.Text = label;
            }
        }
        else
        {
            button.Content = new TextBlock { Text = label, FontFamily = Fonts(label).Normal, FontSize = CardSheets.TextSize, TextTrimming = TextTrimming.CharacterEllipsis };
        }
    }

    // ---------------- type ----------------

    /// <summary>14/20 content: the title in the strong face, the rest in the normal one.</summary>
    private TextBlock Text(string text, bool strong)
    {
        var (normal, bold) = Fonts(text);
        return new TextBlock
        {
            Text = text,
            FontFamily = strong ? bold : normal,
            FontWeight = strong ? FontWeights.Medium : FontWeights.Normal,
            FontSize = CardSheets.TextSize,
            LineHeight = CardSheets.TextLine,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(_palette.Fore),
        };
    }

    /// <summary>12/16 notes.</summary>
    private static TextBlock Note() => new()
    {
        FontSize = CardSheets.NoteSize,
        LineHeight = CardSheets.NoteLine,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
    };

    private (FontFamily Normal, FontFamily Strong) Fonts(string? text) => AppFonts.For(text, _language);

    private Color ToneColor(SheetTone tone) => tone switch
    {
        SheetTone.Ok => _palette.Ok,
        SheetTone.Waiting => _palette.Waiting,
        SheetTone.Bad => _palette.Bad,
        _ => _palette.Fore,
    };

    private Color BarColor(SheetTone tone) => tone switch
    {
        SheetTone.Plain => _palette.Accent,
        _ => ToneColor(tone),
    };

    private static Color Faint(Color c, double alpha) => Color.FromArgb((byte)Math.Round(255 * alpha), c.R, c.G, c.B);

    private static SolidColorBrush Brush(Color c) => new(c);
}