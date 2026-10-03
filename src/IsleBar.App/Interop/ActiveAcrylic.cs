using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace IsleBar.App.Interop;

/// <summary>
/// Acrylic that stays on. The stock <c>DesktopAcrylicBackdrop</c> follows window activation and turns to a flat colour while the
/// window isn't active — and the popups above the bar never are (they must not take the keyboard from the input box), so the
/// file list was plain grey and never looked like the Start menu's search (user 10-04).
/// </summary>
internal sealed partial class ActiveAcrylic : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _config;
    private FrameworkElement? _themeSource;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _config = new SystemBackdropConfiguration { IsInputActive = true };
        _themeSource = xamlRoot.Content as FrameworkElement;
        if (_themeSource is not null)
        {
            _themeSource.ActualThemeChanged += OnThemeChanged;
        }

        _controller = new DesktopAcrylicController();
        ApplyTheme();
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_config);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        if (_themeSource is not null)
        {
            _themeSource.ActualThemeChanged -= OnThemeChanged;
            _themeSource = null;
        }

        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => ApplyTheme();

    private void ApplyTheme()
    {
        if (_config is null)
        {
            return;
        }

        var dark = (_themeSource?.ActualTheme ?? ElementTheme.Default) switch
        {
            ElementTheme.Dark => true,
            ElementTheme.Light => false,
            _ => !ThemeColors.FromSystem().Light,
        };
        _config.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        if (_controller is null)
        {
            return;
        }

        // The Start menu's recipe (WinUI's "acrylic background base"): a dense tint with only a hint of what's behind. The
        // controller's defaults are the thin kind — far see-through, out of place next to Windows' own popups (user 10-04).
        _controller.Kind = DesktopAcrylicKind.Base;
        _controller.TintColor = dark ? Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20) : Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3);
        _controller.TintOpacity = dark ? 0.5f : 0f;
        _controller.LuminosityOpacity = dark ? 0.96f : 0.9f;
        _controller.FallbackColor = dark ? Windows.UI.Color.FromArgb(255, 0x1C, 0x1C, 0x1C) : Windows.UI.Color.FromArgb(255, 0xEE, 0xEE, 0xEE);
    }
}
