using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace IsleBar.App.Interop;

/// <summary>
/// A system backdrop that is a fully transparent brush: the window shows what's behind it wherever XAML draws nothing, with
/// per-pixel alpha (anti-aliased rounded corners). The same trick as WinUIEx's TransparentTintBackdrop. Making a window see-through
/// with DwmExtendFrameIntoClientArea alone only gave an opaque white box here (ExpandedNotice, 09-30).
/// The backdrop brush belongs to the system compositor (Windows.UI.Composition), which needs a Windows.System dispatcher queue on
/// the UI thread — WinUI 3 only makes its own kind, so one is made once.
/// </summary>
internal sealed partial class TransparentBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? s_compositor;
    private static IntPtr s_queueController;
    private ICompositionSupportsSystemBackdrop? _target;

    // the live glass (TODO 7.9 丙④): made once per window size, moved every frame
    private Windows.UI.Composition.CompositionRoundedRectangleGeometry? _glassShape;
    private Windows.UI.Composition.ShapeVisual? _glassVisual;
    private Windows.UI.Composition.ContainerVisual? _glassRoot;
    private Windows.UI.Composition.InsetClip? _glassCut;
    private Windows.UI.Composition.CompositionColorBrush? _glassInk;
    private Windows.UI.Composition.CompositionEffectBrush? _glassBrush;
    private System.Numerics.Vector2 _glassSize;

    /// <summary>
    /// The lifted card's live glass (TODO 7.9 丙④, 10-10): the window's backdrop becomes what's behind it, blurred and live (the
    /// host backdrop brush — the system blurs it), but only inside one rounded rectangle that <see cref="MoveGlass"/> places every
    /// frame; everywhere else stays see-through. False when it couldn't (the window stays see-through).
    /// </summary>
    /// <param name="tint">The acrylic's tint colour, <paramref name="tintOpacity"/> of it, and how much its lightness sets the glass's
    /// (<paramref name="luminosityOpacity"/>) — WinUI's acrylic recipe (10-10: Windows' base acrylic is #202020 × 0.5, 0.96 dark;
    /// #F3F3F3 × 0, 0.9 light).</param>
    public bool StartGlass(float windowWidth, float windowHeight, Windows.UI.Color tint, float tintOpacity, float luminosityOpacity)
    {
        if (_target is null)
        {
            return false;
        }

        var step = "host brush";
        try
        {
            var c = Compositor();
            var size = new System.Numerics.Vector2(windowWidth, windowHeight);
            if (_glassBrush is null || _glassSize != size)
            {
                var host = c.CreateHostBackdropBrush();
                step = "shape";
                _glassShape = c.CreateRoundedRectangleGeometry();
                _glassInk = c.CreateColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                var sprite = c.CreateSpriteShape(_glassShape);
                sprite.FillBrush = _glassInk;
                _glassVisual = c.CreateShapeVisual();
                _glassVisual.Size = size;
                _glassVisual.Shapes.Add(sprite);
                // the surface draws its source's children with their own (3D) transforms; the cut sits on a flat box in between
                var cutBox = c.CreateContainerVisual();
                cutBox.Size = size;
                _glassCut = c.CreateInsetClip();
                cutBox.Clip = _glassCut;
                cutBox.Children.InsertAtTop(_glassVisual);
                _glassRoot = c.CreateContainerVisual();
                _glassRoot.Size = size;
                _glassRoot.Children.InsertAtTop(cutBox);
                step = "visual surface";
                var surface = c.CreateVisualSurface();
                surface.SourceVisual = _glassRoot;
                surface.SourceSize = size;
                // WinUI's acrylic on the system's blur, blurred some more (user 10-10: the notification centre is softer; colour not
                // boosted — 1.25 was too much): the luminosity layer takes the lightness to one steady level while the colours behind
                // stay (the "colour" blend — WinUI notes the two modes' names are swapped); the tint on top (the "luminosity" blend).
                // Then cut to the card's shape — a mask brush won't take a backdrop brush as its source (E_INVALIDARG, 10-10): an
                // alpha-mask effect does.
                step = "effect";
                var softer = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
                {
                    Name = "Blur",
                    BlurAmount = 20,
                    BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Hard,
                    Source = new Windows.UI.Composition.CompositionEffectSourceParameter("glass"),
                };
                var luminosity = new Microsoft.Graphics.Canvas.Effects.BlendEffect
                {
                    Mode = Microsoft.Graphics.Canvas.Effects.BlendEffectMode.Color,
                    Background = softer,
                    Foreground = new Microsoft.Graphics.Canvas.Effects.ColorSourceEffect { Name = "Luminosity" },
                };
                var tinted = new Microsoft.Graphics.Canvas.Effects.BlendEffect
                {
                    Mode = Microsoft.Graphics.Canvas.Effects.BlendEffectMode.Luminosity,
                    Background = luminosity,
                    Foreground = new Microsoft.Graphics.Canvas.Effects.ColorSourceEffect { Name = "Tint" },
                };
                // The card's solid colour over the glass, faded in here, not by the card's own fill: a half see-through XAML fill over
                // the glass came out lighter than both (user 10-10: the bottom of the card, buttons most, brightened while the fill
                // faded and snapped dark when it was gone — measured on video 75 → 100 → 62). Mixed in one graph the sum is right.
                var covered = new Microsoft.Graphics.Canvas.Effects.CompositeEffect
                {
                    Mode = Microsoft.Graphics.Canvas.CanvasComposite.SourceOver,
                    Sources = { tinted, new Microsoft.Graphics.Canvas.Effects.ColorSourceEffect { Name = "Solid" } },
                };
                var effect = new Microsoft.Graphics.Canvas.Effects.AlphaMaskEffect
                {
                    Source = covered,
                    AlphaMask = new Windows.UI.Composition.CompositionEffectSourceParameter("shape"),
                };
                _glassBrush = c.CreateEffectFactory(effect, ["Luminosity.Color", "Tint.Color", "Solid.Color"]).CreateBrush();
                _glassBrush.SetSourceParameter("glass", host);
                _glassBrush.SetSourceParameter("shape", c.CreateSurfaceBrush(surface));
                _glassSize = size;
            }

            _glassShape!.Size = System.Numerics.Vector2.Zero;   // nothing until the first MoveGlass
            step = "colours";
            _glassBrush.Properties.InsertColor("Tint.Color", Windows.UI.Color.FromArgb((byte)Math.Round(255 * tintOpacity), tint.R, tint.G, tint.B));
            _glassBrush.Properties.InsertColor("Luminosity.Color", Windows.UI.Color.FromArgb((byte)Math.Round(255 * luminosityOpacity), tint.R, tint.G, tint.B));
            step = "effect brush as the backdrop";
            _target.SystemBackdrop = _glassBrush;
            return true;
        }
        catch (Exception ex)
        {
            TaskbarHost.Log($"lift: live glass failed at {step}: {ex.GetType().Name} {ex.Message} (0x{ex.HResult:X})");
            _glassBrush = null;
            return false;
        }
    }

    /// <summary>
    /// Puts the glass this frame (window pixels): a rounded rectangle at <paramref name="offset"/>, seen through
    /// <paramref name="transform"/> (the card's stretch, lean and perspective), <paramref name="opacity"/> of it, cut off below
    /// <paramref name="cutBottom"/> (the slide's cut at the taskbar's top edge; the window's height for none), the card's
    /// <paramref name="solid"/> colour over it (its alpha = how much of the glass it still hides).
    /// </summary>
    public void MoveGlass(System.Numerics.Vector2 offset, System.Numerics.Vector2 size, float radius, System.Numerics.Matrix4x4 transform, float opacity, float cutBottom, Windows.UI.Color solid)
    {
        if (_glassShape is null || _glassVisual is null || _glassCut is null || _glassInk is null || _glassBrush is null)
        {
            return;
        }

        _glassBrush.Properties.InsertColor("Solid.Color", solid);

        _glassShape.Offset = offset;
        _glassShape.Size = size;
        _glassShape.CornerRadius = new System.Numerics.Vector2(radius, radius);
        _glassVisual.TransformMatrix = transform;
        _glassInk.Color = Windows.UI.Color.FromArgb((byte)Math.Round(255 * Math.Clamp(opacity, 0, 1)), 255, 255, 255);
        _glassCut.BottomInset = Math.Max(0, _glassSize.Y - cutBottom);
    }

    // the solid look in the same layer (the input panel without glass): a rounded rectangle of one colour, nothing read from behind
    private Windows.UI.Composition.CompositionRoundedRectangleGeometry? _solidShape;
    private Windows.UI.Composition.CompositionColorBrush? _solidInk;
    private Windows.UI.Composition.CompositionBrush? _solidBrush;
    private System.Numerics.Vector2 _solidSize;

    /// <summary>
    /// The panel's solid colour drawn here instead of by XAML (user 10-09): over a XAML fill the text box's own caret showed (still,
    /// never blinking) on top of the panel's own one; over a see-through window, as with the glass, it doesn't. False when it couldn't.
    /// </summary>
    public bool StartSolid(float windowWidth, float windowHeight, Windows.UI.Color colour)
    {
        if (_target is null)
        {
            return false;
        }

        try
        {
            var c = Compositor();
            var size = new System.Numerics.Vector2(windowWidth, windowHeight);
            if (_solidBrush is null || _solidSize != size)
            {
                _solidShape = c.CreateRoundedRectangleGeometry();
                _solidInk = c.CreateColorBrush(colour);
                var sprite = c.CreateSpriteShape(_solidShape);
                sprite.FillBrush = _solidInk;
                var visual = c.CreateShapeVisual();
                visual.Size = size;
                visual.Shapes.Add(sprite);
                var surface = c.CreateVisualSurface();
                surface.SourceVisual = visual;
                surface.SourceSize = size;
                _solidBrush = c.CreateSurfaceBrush(surface);
                _solidSize = size;
            }

            _solidInk!.Color = colour;
            _solidShape!.Size = System.Numerics.Vector2.Zero;
            _target.SystemBackdrop = _solidBrush;
            return true;
        }
        catch (Exception ex)
        {
            TaskbarHost.Log($"panel: solid backdrop failed: {ex.GetType().Name} {ex.Message} (0x{ex.HResult:X})");
            _solidBrush = null;
            return false;
        }
    }

    /// <summary>Puts the solid shape this frame (window pixels).</summary>
    public void MoveSolid(System.Numerics.Vector2 offset, System.Numerics.Vector2 size, float radius)
    {
        if (_solidShape is null)
        {
            return;
        }

        _solidShape.Offset = offset;
        _solidShape.Size = size;
        _solidShape.CornerRadius = new System.Numerics.Vector2(radius, radius);
    }

    public void HideGlass()
    {
        if (_target is not null)
        {
            _target.SystemBackdrop = Compositor().CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _target = connectedTarget;
        try
        {
            connectedTarget.SystemBackdrop = Compositor().CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            TaskbarHost.Log("lift: transparent backdrop connected");
        }
        catch (Exception ex)
        {
            TaskbarHost.Log($"lift: transparent backdrop failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }

    private static Windows.UI.Composition.Compositor Compositor()
    {
        if (s_compositor is null)
        {
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
            {
                var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 2 };   // this thread, STA
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out s_queueController));
            }

            s_compositor = new Windows.UI.Composition.Compositor();
        }

        return s_compositor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
}
