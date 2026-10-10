using IsleBar.App.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace IsleBar.App.IslandUi;

/// <summary>
/// An invisible window over a stretch of the taskbar (<see cref="Core.Ui.DropZone"/>), up only while something is being dragged
/// (TODO ④ 乙, user 10-10): files dragged over it count as over the pill — no 🚫 from the taskbar on the way, no flicker between the
/// taskbar and the pill, and with only the circle out the "drop here" pill comes out wherever in it they are. It draws nothing: the
/// pills under it show through; it only takes the drag and hands it to the bar (<see cref="Over"/>, <see cref="Left"/>,
/// <see cref="Dropped"/>). Never focused, topmost over the taskbar.
/// </summary>
internal sealed class DropCatcher : Window
{
    private readonly IntPtr _hwnd;

    public DropCatcher()
    {
        _hwnd = SeeThrough.Make(this, "dropzone");
        var root = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), AllowDrop = true };   // a fill, or no hit
        // on entering too: a drag only told "yes" on its first DragOver showed WinUI's 🚫 for a moment at the edge (user 10-10 photo)
        root.DragEnter += (_, e) => Over?.Invoke(e);
        root.DragOver += (_, e) => Over?.Invoke(e);
        root.DragLeave += (_, _) => Left?.Invoke();
        root.Drop += (_, e) => Dropped?.Invoke(e);
        root.Children.Add(_layer);
        Content = root;
    }

    // ---- the transit station in the hand (TODO ④ 丙, user 10-11): over its place its picture is drawn here, not in the bar's own
    // window — that is a strip inside the taskbar: it couldn't draw above it ("sticks to the taskbar's edge"), was cut at its end,
    // and showing a moving picture there meant reshaping the window every frame (it stuttered). This one is see-through.
    private readonly Canvas _layer = new() { IsHitTestVisible = false };
    private Image? _hand;
    private (int Left, int Top) _at;   // the window's top-left on screen (pixels)

    /// <summary>Shows the picture (sized in dip like the pill it shows).</summary>
    public void ShowHand(ImageSource picture, double width, double height)
    {
        if (_hand is null)
        {
            _hand = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
            _layer.Children.Add(_hand);
        }

        _hand.Source = picture;
        _hand.Width = width;
        _hand.Height = height;
        _hand.Opacity = 1;
        _hand.Visibility = Visibility.Visible;
    }

    /// <summary>Puts the picture's top-left at this screen point (pixels).</summary>
    public void MoveHand(double x, double y)
    {
        if (_hand is null)
        {
            return;
        }

        var scale = Content.XamlRoot?.RasterizationScale ?? 1;
        Canvas.SetLeft(_hand, (x - _at.Left) / scale);
        Canvas.SetTop(_hand, (y - _at.Top) / scale);
    }

    public double HandOpacity
    {
        set
        {
            if (_hand is not null)
            {
                _hand.Opacity = value;
            }
        }
    }

    public void HideHand()
    {
        if (_hand is not null)
        {
            _hand.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>While the picture is on its way home the window stays up (the button is up: the drag watch would take it down).</summary>
    public bool Holding { get; set; }

    public Action<DragEventArgs>? Over { get; set; }
    public Action? Left { get; set; }
    public Action<DragEventArgs>? Dropped { get; set; }

    public bool IsUp { get; private set; }

    /// <summary>Shown off-screen, ready to be moved in (<see cref="Warm"/>).</summary>
    public bool IsWarm { get; private set; }

    /// <summary>
    /// Shown off-screen ahead of a drag that may come (the transit station pressed, 10-11): showing it took part of the ~0.12 s the
    /// station stuck to its place before Windows' drag image came — moved in then, it's ready. Off-screen, it takes no mouse from the pill.
    /// </summary>
    public void Warm()
    {
        if (IsUp || IsWarm)
        {
            return;
        }

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-32000, -32000, 64, 64));
        AppWindow.Show(activateWindow: false);
        IsWarm = true;
    }

    /// <summary>Up over this rectangle (screen pixels), or moved there when it's up already.</summary>
    public void Cover(int left, int top, int right, int bottom)
    {
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(left, top, right - left, bottom - top));
        _at = (left, top);
        if (!IsUp)
        {
            if (!IsWarm)
            {
                AppWindow.Show(activateWindow: false);
            }

            IsUp = true;
            IsWarm = false;
        }

        SeeThrough.OnTop(_hwnd);
    }

    /// <summary>Back over the taskbar (it lifts itself over topmost windows when something passes over it).</summary>
    public void OnTop() => SeeThrough.OnTop(_hwnd);

    public void Down()
    {
        if (Holding)
        {
            return;
        }

        HideHand();
        if (IsUp || IsWarm)
        {
            AppWindow.Hide();
            IsUp = false;
            IsWarm = false;
        }
    }
}
