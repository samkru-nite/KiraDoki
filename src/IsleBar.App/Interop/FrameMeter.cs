using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IsleBar.App.Interop;

/// <summary>
/// Counts the frames an animation actually drew and its longest gap, against the screen's refresh rate (user 10-08: "can the
/// animations run at the full refresh rate?"). One log line per animation: "fps &lt;name&gt;: 41 frames in 340 ms = 121/s (screen 120 Hz), longest gap 12 ms".
/// </summary>
internal sealed class FrameMeter
{
    private readonly string _name;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _frames;
    private double _last;
    private double _longest;

    public FrameMeter(string name) => _name = name;

    public void Frame()
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        if (_frames > 0)
        {
            _longest = Math.Max(_longest, now - _last);
        }

        _last = now;
        _frames++;
    }

    public void Report()
    {
        var ms = _clock.Elapsed.TotalMilliseconds;
        var rate = ms > 0 ? _frames * 1000 / ms : 0;
        TaskbarHost.Log($"fps {_name}: {_frames} frames in {ms:0} ms = {rate:0}/s (screen {RefreshHz()} Hz), longest gap {_longest:0} ms");
    }

    private static int RefreshHz()
    {
        var dc = GetDC(IntPtr.Zero);
        try
        {
            return GetDeviceCaps(dc, VREFRESH);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    private const int VREFRESH = 116;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr dc, int index);
}
