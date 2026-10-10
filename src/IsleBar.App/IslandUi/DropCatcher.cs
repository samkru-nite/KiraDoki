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
        Content = root;
    }

    public Action<DragEventArgs>? Over { get; set; }
    public Action? Left { get; set; }
    public Action<DragEventArgs>? Dropped { get; set; }

    public bool IsUp { get; private set; }

    /// <summary>Up over this rectangle (screen pixels), or moved there when it's up already.</summary>
    public void Cover(int left, int top, int right, int bottom)
    {
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(left, top, right - left, bottom - top));
        if (!IsUp)
        {
            AppWindow.Show(activateWindow: false);
            IsUp = true;
        }

        SeeThrough.OnTop(_hwnd);
    }

    /// <summary>Back over the taskbar (it lifts itself over topmost windows when something passes over it).</summary>
    public void OnTop() => SeeThrough.OnTop(_hwnd);

    public void Down()
    {
        if (IsUp)
        {
            AppWindow.Hide();
            IsUp = false;
        }
    }
}
