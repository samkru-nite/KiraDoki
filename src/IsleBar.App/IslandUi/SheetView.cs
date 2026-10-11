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
    // The scrolling text fades out at a side with more to scroll (user 10-08). The card is glass now (丙④): a band of the card's
    // colour over the text showed as a dark strip (user 10-10), so the text itself fades — its picture drawn through a gradient mask
    // (WinUI 3 has no opacity mask), the real text kept under it for the mouse, made invisible on its own composition visual only —
    // hiding it in XAML (Opacity 0 on a parent) left nothing to draw (user 10-10: the text was gone). CommunityToolkit's
    // OpacityMaskView does the same.
    private Microsoft.UI.Composition.CompositionLinearGradientBrush? _fadeMask;
    private Microsoft.UI.Composition.CompositionColorGradientStop[] _fadeStops = [];
    private Microsoft.UI.Composition.CompositionVisualSurface? _fadeSurface;
    private Microsoft.UI.Composition.SpriteVisual? _fadeSprite;
    private bool _fadesTop, _fadesBottom;
    private readonly FrameworkElement _middle;
    private readonly List<TextBlock> _texts = [];   // title, paragraphs, raw — in that order (text layout)
    private TextBlock? _footnote;
    private TextBlock? _number;
    private TextBlock? _barLeft;
    private TextBlock? _barRight;
    private Border? _bar;
    private Border? _art;
    private readonly List<TextBlock> _songText = [];   // title, artist
    private readonly List<Border> _buttons = [];
    private SheetAction[] _actions = [];
    private Grid? _sourceRow;
    private CardSheet _sheet;

    /// <summary>The card's contents, inset by its padding; set its size with <see cref="SetSize"/>.</summary>
    public Grid Root { get; } = new();

    /// <summary>A button was clicked.</summary>
    public Action<SheetAction>? Act { get; set; }

    /// <summary>A file list's ✕ (the transit station): the file's path.</summary>
    public Action<string>? RemoveFile { get; set; }

    /// <summary>A file list's row was dragged out and dropped somewhere: the file's path.</summary>
    public Action<string>? FileDragged { get; set; }

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
            SheetLayout.Files => FilesMiddle(sheet, out _scroll),
            _ => TextMiddle(sheet, out _scroll),
        };
        // a file list reaches 8 into the card's padding on both sides: a row's hover fill sits there and its icon lines up with the
        // source row's (user 10-10)
        var inset = sheet.Layout == SheetLayout.Files ? -FileInset : 0;
        _middle.Margin = new Thickness(inset, CardSheets.Gap, inset, 0);
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

    /// <summary>How high the middle's contents are at the card's width (dip) — for choosing the card's height (200 / 300 / 400).</summary>
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

            if (!next.Markdown)
            {
                words.AddRange(next.Body);   // an answer in Markdown is many blocks: a new answer builds the card again (StructureKey)
            }

            if (next.Raw is { } raw)
            {
                words.Add(raw);
            }

            for (var i = 0; i < words.Count && i < _texts.Count; i++)
            {
                if (Words(_texts[i]) != words[i])
                {
                    SetWords(_texts[i], words[i]);
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
                if (Words(_songText[i]) != lines[i])
                {
                    SetWords(_songText[i], lines[i]);
                }
            }
        }

        if (_footnote is not null && Words(_footnote) != (next.Footnote ?? string.Empty))
        {
            SetWords(_footnote, next.Footnote ?? string.Empty);   // an agent's next step: in place, many a minute
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
        s.Progress is { Left: null, Right: null }, s.Buttons.Count, s.Markdown ? string.Join('\n', s.Body) : string.Empty, s.Footnote is null,
        string.Join('\n', s.Files.Select(f => (f.Gone ? "-" : "+") + f.Path)));   // a file in or out, or gone: the list again

    /// <summary>
    /// Takes the source row's icon out, so a card built again can hold it (10-09: working → done while lifted rebuilt the card, and
    /// adding the icon still sitting in the old row failed every tick — "refresh failed … SourceRow", the card stayed blank).
    /// </summary>
    public void ReleaseIcon()
    {
        if (_sourceRow is { } row && row.Children.Count > 0)
        {
            row.Children.RemoveAt(0);
        }
    }

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

    /// <summary>"完成 2分钟前": the state word bold in its colour, then the time in grey after the gap (<see cref="TextGap"/>).</summary>
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
            _stateText.Inlines.Add(new Run { Text = sheet.State is null ? time : TextGap.Break + time, FontFamily = normal, Foreground = Brush(_palette.Hint) });
        }

        GapRuns.Spread(_stateText.Inlines, _stateText.FontSize);
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

        if (sheet.Markdown)
        {
            AddMarkdown(stack, sheet.Body.Count > 0 ? sheet.Body[0] : null);
        }

        foreach (var paragraph in sheet.Markdown ? Array.Empty<string>() : sheet.Body)
        {
            var p = Text(paragraph, strong: false);
            _texts.Add(p);
            p.Foreground = Brush(_palette.Hint);
            p.Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : sheet.Title is not null && stack.Children.Count == 1 ? CardSheets.TitleGap : CardSheets.ParagraphGap, 0, 0);
            stack.Children.Add(p);
        }

        if (sheet.Footnote is { } footnote)
        {
            _footnote = MdNote(footnote, _palette.Hint);
            _footnote.TextWrapping = TextWrapping.NoWrap;
            _footnote.TextTrimming = TextTrimming.CharacterEllipsis;
            _footnote.Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : sheet.Title is not null && stack.Children.Count == 1 ? CardSheets.TitleGap : MdGap, 0, 0);
            stack.Children.Add(_footnote);
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
                Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : sheet.Title is not null && stack.Children.Count == 1 ? CardSheets.TitleGap : 6, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(4),
                Background = Brush(Faint(_palette.Fore, 0.06)),
                BorderBrush = Brush(Faint(_palette.Fore, 0.09)),
                BorderThickness = new Thickness(1),
                Child = code,
            });
        }

        return Faded(stack, out scroll);
    }

    /// <summary>The middle's contents in a scroller whose sides fade where there is more to scroll (3:4 only, see <see cref="SetSize"/>).</summary>
    private Grid Faded(StackPanel stack, out ScrollViewer scroll)
    {
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
        var box = new Grid();
        box.Children.Add(scroll);
        var text = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(scroll);
        var c = text.Compositor;
        _fadeSurface = c.CreateVisualSurface();
        _fadeSurface.SourceVisual = text;
        var follow = c.CreateExpressionAnimation("text.Size");
        follow.SetReferenceParameter("text", text);
        _fadeSurface.StartAnimation("SourceSize", follow);
        text.Opacity = 0;   // the surface still draws it (it ignores its source's own opacity)
        _fadeMask = c.CreateLinearGradientBrush();
        _fadeMask.StartPoint = new System.Numerics.Vector2(0, 0);
        _fadeMask.EndPoint = new System.Numerics.Vector2(0, 1);
        _fadeStops = [.. Enumerable.Range(0, 4).Select(_ => c.CreateColorGradientStop())];
        foreach (var stop in _fadeStops)
        {
            stop.Color = Color.FromArgb(255, 255, 255, 255);
            _fadeMask.ColorStops.Add(stop);
        }

        var masked = c.CreateMaskBrush();
        masked.Source = c.CreateSurfaceBrush(_fadeSurface);
        masked.Mask = _fadeMask;
        _fadeSprite = c.CreateSpriteVisual();
        _fadeSprite.Brush = masked;
        _fadeSprite.RelativeSizeAdjustment = System.Numerics.Vector2.One;
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(box, _fadeSprite);
        box.SizeChanged += (_, _) => Fades();
        return box;
    }

    // ---------------- a list of files (the transit station, TODO ④, 10-10) ----------------

    private const double FileRow = 32, FileRowGap = 2, FileGlyph = 16, FileClose = 24, FileInset = 8;

    /// <summary>
    /// One row per file: its icon (document / folder) and name 14, "not found" 12 grey when it's gone, ✕ at the end. A row is dragged
    /// out on its own — the receiver moves or copies it, like File Explorer; one that's gone can't be dragged.
    /// </summary>
    private Grid FilesMiddle(CardSheet sheet, out ScrollViewer scroll)
    {
        var stack = new StackPanel { Spacing = FileRowGap };
        foreach (var file in sheet.Files)
        {
            stack.Children.Add(FileRowFor(file, sheet.FileGone ?? string.Empty));
        }

        return Faded(stack, out scroll);
    }

    private Border FileRowFor(SheetFile file, string goneWords)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // the file's own icon, as File Explorer shows it (user 10-10); till it's in, or when the file is gone, a line icon in grey
        var icon = new Grid { Width = FileGlyph, Height = FileGlyph, VerticalAlignment = VerticalAlignment.Center };
        icon.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = FileGlyph, Foreground = Brush(_palette.Hint),
            Glyph = Directory.Exists(file.Path) ? "" : "",   // folder / document
        });
        if (!file.Gone)
        {
            Interop.AppIcon.ResolveFile(file.Path, source =>
            {
                icon.Children.Clear();
                icon.Children.Add(new Image { Source = source, Width = FileGlyph, Height = FileGlyph });
            });
        }

        grid.Children.Add(icon);

        var name = new TextBlock
        {
            Text = file.Name, FontFamily = Fonts(file.Name).Normal, FontSize = CardSheets.TextSize, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(file.Gone ? _palette.Hint : _palette.Fore),
        };
        ToolTipService.SetToolTip(name, file.Path);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        if (file.Gone)
        {
            var gone = Note();
            gone.Text = goneWords;
            gone.FontFamily = Fonts(gone.Text).Normal;
            gone.Foreground = Brush(_palette.Hint);
            gone.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(gone, 2);
            grid.Children.Add(gone);
        }

        var close = new Border
        {
            Width = FileClose, Height = FileClose, CornerRadius = new CornerRadius(4), Background = Brush(Microsoft.UI.Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12, Glyph = "", Foreground = Brush(_palette.Hint),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        close.PointerEntered += (_, _) => close.Background = Brush(ButtonFill(1));
        close.PointerExited += (_, _) => close.Background = Brush(Microsoft.UI.Colors.Transparent);
        close.Tapped += (_, e) =>
        {
            e.Handled = true;
            RemoveFile?.Invoke(file.Path);
        };
        Grid.SetColumn(close, 3);
        grid.Children.Add(close);

        var row = new Border
        {
            Height = FileRow, Padding = new Thickness(FileInset, 0, FileInset, 0), CornerRadius = new CornerRadius(4),
            Background = Brush(Microsoft.UI.Colors.Transparent), Child = grid, CanDrag = !file.Gone,
        };
        if (!file.Gone)
        {
            row.PointerEntered += (_, _) => row.Background = Brush(ButtonFill(1));
            row.PointerExited += (_, _) => row.Background = Brush(Microsoft.UI.Colors.Transparent);
            row.DragStarting += async (_, e) =>
            {
                var deferral = e.GetDeferral();
                try
                {
                    Windows.Storage.IStorageItem item = Directory.Exists(file.Path)
                        ? await Windows.Storage.StorageFolder.GetFolderFromPathAsync(file.Path)
                        : await Windows.Storage.StorageFile.GetFileFromPathAsync(file.Path);
                    e.Data.SetStorageItems([item]);
                    e.AllowedOperations = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
                        | Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)   // async void: anything escaping ends the app
                {
                    Interop.TaskbarHost.Log("transit: row drag failed — " + ex.Message);
                    e.Cancel = true;
                }
                finally
                {
                    deferral.Complete();
                }
            };
            row.DropCompleted += (_, _) => FileDragged?.Invoke(file.Path);
        }

        return row;
    }

    // ---------------- an answer in Markdown (F2b, user 10-08) ----------------

    private const double MdGap = 6, MdHeadingExtra = 4, MdItemGap = 2, MdMarker = 14, MdNumberMarker = 18, MdCellGap = 12, MdCellPad = 4;

    /// <summary>
    /// The answer's blocks under the title (preview https://claude.ai/artifact/XwYwmoxSbDXue1NH2kpXNL): paragraphs 14 in the main
    /// colour 6 apart; section names 12 grey; list items 2 apart with "•" / "1." hanging; code monospace 12 grey on a faint box,
    /// scrolling sideways (never wrapped: indents stay); tables 12 with a grey header and a hairline under each row.
    /// </summary>
    private void AddMarkdown(StackPanel stack, string? markdown)
    {
        MdKind? last = null;
        foreach (var block in CardMarkdown.Parse(markdown))
        {
            FrameworkElement element = block.Kind switch
            {
                MdKind.Heading => MdNote(block.Text, _palette.Hint),
                MdKind.Bullet or MdKind.Number => MdItem(block),
                MdKind.Code => MdCode(block.Text),
                MdKind.Table => MdTable(block.Rows!),
                _ => MdText(block.Text),
            };
            var listRun = block.Kind is MdKind.Bullet or MdKind.Number && last is MdKind.Bullet or MdKind.Number;
            var top = stack.Children.Count == 0 ? 0
                : last is null ? CardSheets.TitleGap   // under the title
                : listRun ? MdItemGap
                : block.Kind == MdKind.Heading ? MdGap + MdHeadingExtra
                : MdGap;
            element.Margin = new Thickness(element.Margin.Left, top, 0, 0);
            stack.Children.Add(element);
            last = block.Kind;
        }
    }

    private TextBlock MdText(string text)
    {
        var t = Text(text, strong: false);
        t.TextWrapping = TextWrapping.Wrap;   // breaks a long path anywhere — the middle never scrolls sideways
        return t;
    }

    private TextBlock MdNote(string text, Color color)
    {
        var t = Note();
        t.FontFamily = Fonts(text).Normal;
        SetWords(t, text);
        t.Foreground = Brush(color);
        t.TextWrapping = TextWrapping.Wrap;
        t.VerticalAlignment = VerticalAlignment.Top;
        return t;
    }

    /// <summary>"•" / "1." in grey, the words beside it; a second line starts under the words, not under the marker.</summary>
    private Grid MdItem(MdBlock block)
    {
        var marker = block.Kind == MdKind.Number ? MdNumberMarker : MdMarker;
        var row = new Grid { Margin = new Thickness(block.Level * MdMarker, 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(marker) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var mark = Text(block.Marker ?? "•", strong: false);
        mark.Foreground = Brush(_palette.Hint);
        mark.TextWrapping = TextWrapping.NoWrap;
        row.Children.Add(mark);
        var words = MdText(block.Text);
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        return row;
    }

    /// <summary>Monospace 12 grey on a faint box (black 4.5% light / white 7% dark, user 10-08); a long line scrolls inside the box.</summary>
    private Border MdCode(string code)
    {
        var text = new TextBlock
        {
            Text = code, FontFamily = AppFonts.Family(Mono), FontSize = CardSheets.NoteSize, LineHeight = CardSheets.NoteLine,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.NoWrap, Foreground = Brush(_palette.Hint),
            IsTextSelectionEnabled = false,
        };
        return new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = Brush(Faint(_palette.Fore, _palette.Dark ? 0.07 : 0.045)),
            Child = new ScrollViewer
            {
                Content = text,
                // the baseline sits low in the 16 line: top 8 left 12.5 above the ink, 10 below — top 6 makes ink-to-edge ~10 on all
                // four sides (measured from the user's screenshot, 10-09)
                Padding = new Thickness(10, 6, 10, 8),
                HorizontalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
    }

    /// <summary>A real table (user 10-08): header 12 grey, cells 12 in the main colour, a hairline under every row, columns 12 apart.</summary>
    private Grid MdTable(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var columns = rows.Count == 0 ? 0 : rows[0].Count;
        var grid = new Grid { ColumnSpacing = MdCellGap };
        var cells = new TextBlock[rows.Count, columns];
        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < columns; c++)
            {
                var cell = MdNote(c < rows[r].Count ? rows[r][c] : string.Empty, r == 0 ? _palette.Hint : _palette.Fore);
                cell.Margin = new Thickness(0, MdCellPad, 0, MdCellPad);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
                cells[r, c] = cell;
            }

            var line = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Fill = Brush(_palette.Track), IsHitTestVisible = false,
            };
            Grid.SetRow(line, r);
            Grid.SetColumnSpan(line, Math.Max(1, columns));
            grid.Children.Add(line);
        }

        foreach (var width in ColumnWidths(cells, rows.Count, columns))
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        return grid;
    }

    /// <summary>
    /// How wide each column is (user 10-08: a long name in the first column shouldn't squeeze every row). Two columns: every width
    /// that fits one of the first column's cells on one line is tried, and the one giving the lowest table wins (a long name wraps
    /// so the rest get room). More columns: each as wide as its longest cell if they all fit, else in proportion to that.
    /// </summary>
    private List<GridLength> ColumnWidths(TextBlock[,] cells, int rows, int columns)
    {
        var free = Math.Max(0, _width - (MdCellGap * Math.Max(0, columns - 1)));
        var natural = new double[columns];
        for (var c = 0; c < columns; c++)
        {
            for (var r = 0; r < rows; r++)
            {
                natural[c] = Math.Max(natural[c], Measure(cells[r, c], double.PositiveInfinity).Width);
            }
        }

        if (columns <= 1 || natural.Sum() <= free)
        {
            return [.. natural.Select((w, c) => c == columns - 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(Math.Ceiling(w)))];
        }

        if (columns == 2)
        {
            var best = -1.0;
            var lowest = double.MaxValue;
            var tried = Enumerable.Range(0, rows).Select(r => Math.Ceiling(Measure(cells[r, 0], double.PositiveInfinity).Width))
                .Where(w => w > 0 && w <= free * 0.6).Distinct().OrderByDescending(w => w);
            foreach (var w in tried)
            {
                var height = 0.0;
                for (var r = 0; r < rows; r++)
                {
                    height += Math.Max(Measure(cells[r, 0], w).Height, Measure(cells[r, 1], free - w).Height);
                }

                if (height < lowest - 0.5)   // a tie keeps the wider first column (fewer names broken)
                {
                    lowest = height;
                    best = w;
                }
            }

            var first = best > 0 ? best : Math.Min(natural[0], free * 0.45);
            return [new GridLength(first), new GridLength(1, GridUnitType.Star)];
        }

        var total = natural.Sum();
        return [.. natural.Select(w => new GridLength(Math.Max(1, w / total), GridUnitType.Star))];
    }

    private static Windows.Foundation.Size Measure(TextBlock block, double width)
    {
        block.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        return block.DesiredSize;
    }

    /// <summary>The fades only on a side that still has more to scroll (user 10-08).</summary>
    private void Fades()
    {
        if (_scroll is not { } s)
        {
            return;
        }

        var scrolls = s.VerticalScrollMode == ScrollMode.Enabled;
        _fadesTop = scrolls && s.VerticalOffset > 1;
        _fadesBottom = scrolls && s.VerticalOffset < s.ScrollableHeight - 1;
        if (_fadeStops.Length == 4 && s.ActualHeight > 2 * CardSheets.Fade)
        {
            // clear at a fading side, solid from Fade in
            var edge = (float)(CardSheets.Fade / s.ActualHeight);
            (float Offset, bool Clear)[] stops = [(0, _fadesTop), (edge, false), (1 - edge, false), (1, _fadesBottom)];
            for (var i = 0; i < 4; i++)
            {
                _fadeStops[i].Offset = stops[i].Offset;
                _fadeStops[i].Color = Color.FromArgb(stops[i].Clear ? (byte)0 : (byte)255, 255, 255, 255);
            }
        }
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
            // Drawn here, not WinUI's Button: on the glass (丙④) its fill came out darker than the glass around it, and it flashed
            // lighter then darker as the card's own fill faded out (user 10-10, dark only). Windows' colours for a button's fill:
            // rest / pointer over / pressed.
            var button = new Border
            {
                Height = CardSheets.ButtonHeight,
                Padding = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Background = Brush(ButtonFill(0)),
                BorderBrush = Brush(Faint(_palette.Fore, _palette.Dark ? 0.09 : 0.1)),
            };
            SetButton(button, buttons[i]);
            var index = i;
            var over = false;
            button.PointerEntered += (_, _) => { over = true; button.Background = Brush(ButtonFill(1)); };
            button.PointerExited += (_, _) => { over = false; button.Background = Brush(ButtonFill(0)); };
            button.PointerCanceled += (_, _) => { over = false; button.Background = Brush(ButtonFill(0)); };
            button.PointerPressed += (_, _) => button.Background = Brush(ButtonFill(2));
            button.PointerReleased += (_, _) => button.Background = Brush(ButtonFill(over ? 1 : 0));
            button.Tapped += (_, _) => Act?.Invoke(_actions[index]);   // read at click time: pause becomes resume in place
            Grid.SetColumn(button, i);
            row.Children.Add(button);
            _buttons.Add(button);
        }

        return row;
    }

    /// <summary>A button's fill: 0 rest, 1 pointer over, 2 pressed (Windows' control fills: dark 6 / 8 / 3% white; light 70 / 50 / 30%).</summary>
    private Color ButtonFill(int state) => _palette.Dark
        ? Color.FromArgb(state switch { 1 => 0x15, 2 => 0x08, _ => 0x0F }, 255, 255, 255)
        : Color.FromArgb(state switch { 1 => 0x80, 2 => 0x4D, _ => 0xB3 }, 0xFF, 0xFF, 0xFF);

    /// <summary>A button's words or icon (a media button: its icon, its name as a tooltip). Unchanged ones are left alone.</summary>
    private void SetButton(Border button, SheetButton b)
    {
        if (b.Glyph is { } glyph)
        {
            if (button.Child is FontIcon icon)
            {
                if (icon.Glyph != glyph)
                {
                    icon.Glyph = glyph;
                }
            }
            else
            {
                button.Child = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), Glyph = glyph, FontSize = 16, Foreground = Brush(_palette.Fore), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }

            ToolTipService.SetToolTip(button, b.Label);
            return;
        }

        var label = b.Label ?? string.Empty;
        if (button.Child is TextBlock text)
        {
            if (text.Text != label)
            {
                text.Text = label;
            }
        }
        else
        {
            button.Child = new TextBlock
            {
                Text = label, FontFamily = Fonts(label).Normal, FontSize = CardSheets.TextSize, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush(_palette.Fore), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }
    }

    // ---------------- type ----------------

    /// <summary>14/20 content: the title in the strong face, the rest in the normal one.</summary>
    private TextBlock Text(string text, bool strong)
    {
        var (normal, bold) = Fonts(text);
        var t = new TextBlock
        {
            FontFamily = strong ? bold : normal,
            FontWeight = strong ? FontWeights.Medium : FontWeights.Normal,
            FontSize = CardSheets.TextSize,
            LineHeight = CardSheets.TextLine,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(_palette.Fore),
            Tag = strong ? new StrongWords(string.Empty) : null,
        };
        if (strong)
        {
            SetWords(t, text);
        }
        else
        {
            SetWords(t, text);   // "Chrome 已下载 12 MB": a break is a 4 dip gap
        }

        return t;
    }

    /// <summary>A strong text's words (its <c>Text</c> is split into runs, see <see cref="SetWords"/>).</summary>
    private sealed record StrongWords(string Text);

    /// <summary>A plain text's words as given (with its breaks — its <c>Text</c> reads without them).</summary>
    private sealed record PlainWords(string Text);

    private static string Words(TextBlock t) => t.Tag switch { StrongWords s => s.Text, PlainWords p => p.Text, _ => t.Text };

    /// <summary>
    /// Puts <paramref name="text"/> in <paramref name="t"/>. A strong one (a title) is split: Han characters, kana and full-width
    /// marks in the system CJK font's <b>Bold</b>, the rest in Pretendard Medium as it was. Asked for Medium the Han characters fell
    /// back to Microsoft YaHei UI, which has no Medium, and came out Regular — the title looked like the text under it; asking the
    /// whole line for 600 didn't reach the fallback either (user 10-10).
    /// </summary>
    private void SetWords(TextBlock t, string text)
    {
        if (t.Tag is not StrongWords)
        {
            t.Tag = new PlainWords(text);
            GapRuns.Fill(t, text);
            return;
        }

        t.Tag = new StrongWords(text);
        t.Inlines.Clear();
        var cjk = AppFonts.Family(IsleBar.Core.Localization.UiFonts.Stack(null, _language, IsleBar.Core.Localization.UiFonts.Entry(_language)));
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && IsCjk(text[i]) == IsCjk(text[start]))
            {
                continue;
            }

            var piece = text[start..i];
            t.Inlines.Add(IsCjk(text[start])
                ? new Run { Text = piece, FontFamily = cjk, FontWeight = FontWeights.Bold }
                : new Run { Text = piece });
            start = i;
        }

        GapRuns.Spread(t.Inlines, t.FontSize);
    }

    /// <summary>Drawn by a CJK font, not Pretendard (which has Latin, digits and Hangul): Han, kana, CJK and full-width marks.</summary>
    private static bool IsCjk(char c)
        => c is (>= '\u2E80' and <= '\u9FFF') or (>= '\uF900' and <= '\uFAFF') or (>= '\uFE30' and <= '\uFE4F') or (>= '\uFF00' and <= '\uFFEF')
           || char.IsSurrogate(c);

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