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

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
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
