using IsleBar.App.Interop;
using IsleBar.App.IslandUi;
using IsleBar.App.Media;
using IsleBar.Core.Configuration;
using IsleBar.Core.Island;
using IsleBar.Core.Localization;
using IsleBar.Core.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace IsleBar.App;

/// <summary>
/// The big pill on the taskbar is a <see cref="PillView"/> (<c>Big</c>, 10-09). Its parts keep their old names here, so the rest
/// of the window (typing, hover, swap, lift) reads as before; what a pill needs from the window is <see cref="IPillHost"/>.
/// </summary>
public sealed partial class MainWindow : IPillHost
{
    private SmoothBorder PillSurface => Big.PillSurface;
    private Microsoft.UI.Xaml.Controls.Grid CardRow => Big.CardRow;
    private Microsoft.UI.Xaml.Controls.Grid CloseZone => Big.CloseZone;
    private SmoothBorder CloseChip => Big.CloseChip;
    private Microsoft.UI.Xaml.Controls.TextBlock CloseLabel => Big.CloseLabel;
    private Microsoft.UI.Xaml.Controls.Grid LeadSlot => Big.LeadSlot;
    private Microsoft.UI.Xaml.Controls.Image ClaudeGlyphImage => Big.ClaudeGlyphImage;
    private Microsoft.UI.Xaml.Controls.Image CodexGlyph => Big.CodexGlyph;
    private Microsoft.UI.Xaml.Controls.FontIcon LeadIcon => Big.LeadIcon;
    private Microsoft.UI.Xaml.Controls.Canvas ZipTeeth => Big.ZipTeeth;
    private Microsoft.UI.Xaml.Controls.Grid IslandContent => Big.IslandContent;
    private Microsoft.UI.Xaml.Controls.TextBlock IslandText => Big.IslandText;
    private Microsoft.UI.Xaml.Controls.Grid FlowTrack => Big.FlowTrack;
    private Microsoft.UI.Xaml.Controls.Border FlowPiece => Big.FlowPiece;
    private SmoothBorder DimVeil => Big.DimVeil;
    private SmoothBorder GlowRing => Big.GlowRing;

    IsleBarSettings IPillHost.Settings => _settings;
    ThemeColors IPillHost.Theme => _theme;
    LanguageStrings IPillHost.Text => _text;
    string IPillHost.Language => _language;
    bool IPillHost.Flying => _flying;
    ImageSource? IPillHost.ClaudeLogo => _claudeGlyph;
    ImageSource? IPillHost.CodexLogo => _codexBrand;
    AudioMeter IPillHost.Meter => _audioMeter;

    (BitmapImage Image, Windows.UI.Color? Tint)? IPillHost.Cover(Card? card) => CoverOf(card) is { } c ? (c.Image, c.Tint) : null;

    UIElement IPillHost.Dial(TimerDial dial, Windows.UI.Color color, bool paused) => DialContent(dial, color, paused);

    string? IPillHost.AgentSaid(ActivityState state) => _feed.Tails.For(state)?.Said;

    string? IPillHost.AgentAnswer(ActivityState state) => _feed.Tails.For(state)?.Answer;

    SolidColorBrush IPillHost.Brush(Windows.UI.Color color) => BrushOf(color);
}
