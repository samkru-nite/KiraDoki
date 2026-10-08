using System.Numerics;
using IsleBar.Core.Ui;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;

namespace IsleBar.App.IslandUi;

/// <summary>
/// Button press feedback (09-30 design M5): sinks in on press (0.86, 0.08 s) and springs back with a slight overshoot on release.
/// "What can be pressed responds to the hand". The button consumes the press first, so we subscribe with handledEventsToo.
/// </summary>
internal static class PressFeedback
{
    /// <summary>How far a small button sinks. Bigger things sink less (the big pill: <see cref="PillPressed"/>), so every press moves a few dip.</summary>
    private const float Pressed = 0.86f;

    /// <summary>The big pill's press (user 10-08): light — 220 wide, 0.985 is ~1.6 dip a side (0.97 read as "a bit much").</summary>
    public const float PillPressed = 0.985f;

    /// <summary>The spring back: small buttons wobble a little (damping 0.55); the big pill springs back with one small overshoot only
    /// (damping 0.8 — at 0.55 it "bounced several times", user 10-08).</summary>
    private const double Damping = 0.55;
    private const double Response = 0.35;   // the spring's period (s)
    public const double PillDamping = 0.8;

    /// <summary>Whether presses animate at all: they are part of the bounce setting (user 10-08: bounce off = no press motion).</summary>
    public static Func<bool> Enabled { get; set; } = () => true;

    /// <summary>The button currently pressed (during animation Visual.Scale differs from the on-screen value, so we track it separately).</summary>
    private static readonly HashSet<UIElement> Down = [];

    private sealed record Feel(float Pressed, double Damping);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, Feel> Feels = [];   // cells are rebuilt often; forgotten with them

    /// <summary>
    /// Holding the left button = the first half (sinks in); letting go = the second half (springs back) — and only then does the click
    /// act (user 10-08: "press does half, release completes it and the card pops").
    /// </summary>
    public static void Attach(UIElement element, float pressed = Pressed, double damping = Damping)
    {
        Feels.AddOrUpdate(element, new Feel(pressed, damping));
        if (element is Microsoft.UI.Xaml.Controls.Button button)
        {
            // Motion signals the press — turn off the default pressed background (gray box). Leave the hover background as-is
            var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.Resources["ButtonBackgroundPressed"] = clear;
            button.Resources["ButtonBorderBrushPressed"] = clear;
        }

        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
            {
                Press(element);   // not the right button: its menu takes the release, and the button stayed sunk
            }
        }), handledEventsToo: true);
        element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Release(element)), handledEventsToo: true);
        element.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => Release(element)), handledEventsToo: true);
        element.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => Release(element)), handledEventsToo: true);
    }

    private static Visual Prepare(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (element is FrameworkElement fe)
        {
            visual.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0f);
        }

        return visual;
    }

    /// <summary>The whole press, down and back, without a hand: the big pill when the lifted card lands back in it (user 10-08).</summary>
    public static void Pulse(UIElement element)
    {
        Press(element);
        var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(80);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Release(element);
        timer.Start();
    }

    /// <summary>Pressed things that stay down after the button comes up, until <see cref="LetGo"/>.</summary>
    private static readonly HashSet<UIElement> Held = [];

    /// <summary>
    /// Keep <paramref name="element"/> down when the button is released: the big pill when its click puts the card back — it springs
    /// up only as the card lands (<see cref="LetGo"/>). Before this it sprang back on release and pressed again on landing: two
    /// presses in a row "bounced several times" (user 10-08).
    /// </summary>
    public static void Hold(UIElement element) => Held.Add(element);

    public static bool IsHeld(UIElement element) => Held.Contains(element);

    /// <summary>How long <paramref name="element"/> takes to spring from pressed back to full size (its overshoot comes after).</summary>
    public static TimeSpan RiseTime(UIElement element) => TimeSpan.FromSeconds(SpringCurve.RiseTime(Response, FeelOf(element).Damping));

    /// <summary>Lets a held element spring back after <paramref name="delay"/>.</summary>
    public static void LetGoAfter(UIElement element, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            LetGo(element);
            return;
        }

        var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => LetGo(element);
        timer.Start();
    }

    /// <summary>Lets a held element spring back.</summary>
    public static void LetGo(UIElement element)
    {
        if (Held.Remove(element))
        {
            Release(element);
        }
    }

    private static Feel FeelOf(UIElement element) => Feels.TryGetValue(element, out var f) ? f : new Feel(Pressed, Damping);

    private static void Press(UIElement element)
    {
        if (!Enabled())
        {
            return;
        }

        Down.Add(element);
        var pressed = FeelOf(element).Pressed;
        var visual = Prepare(element);
        var c = visual.Compositor;
        var down = c.CreateVector3KeyFrameAnimation();
        down.InsertKeyFrame(1f, new Vector3(pressed, pressed, 1f), c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.8f), new Vector2(0.2f, 1f)));
        down.Duration = TimeSpan.FromMilliseconds(80);
        visual.StartAnimation(nameof(Visual.Scale), down);
    }

    private static void Release(UIElement element)
    {
        if (Held.Contains(element) || !Down.Remove(element))
        {
            return;   // held down for later, or never pressed (just passed over)
        }

        var visual = Prepare(element);
        var c = visual.Compositor;
        var feel = FeelOf(element);
        var from = feel.Pressed;
        var linear = c.CreateLinearEasingFunction();
        var up = c.CreateVector3KeyFrameAnimation();
        foreach (var (fraction, progress) in SpringCurve.Sample(Response, feel.Damping))
        {
            var v = from + ((1f - from) * progress);
            up.InsertKeyFrame(fraction, new Vector3(v, v, 1f), linear);
        }

        up.Duration = TimeSpan.FromSeconds(SpringCurve.SettleTime(Response, feel.Damping));
        visual.StartAnimation(nameof(Visual.Scale), up);
    }
}
