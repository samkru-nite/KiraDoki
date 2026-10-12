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
    private long _lastStamp;
    private double _longest;

    // DIAGNOSTIC (performance, 10-12): our own work per frame, and what the UI thread was busy with during each long gap (Perf)
    private double _work;
    private double _worstWork;
    private readonly List<string> _stutters = [];

    /// <summary>A gap this long (ms) between two frames is a stutter worth naming (165 Hz: 6 ms a frame).</summary>
    private const double LongGap = 25;

    public FrameMeter(string name) => _name = name;

    public void Frame()
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        var stamp = Perf.Now;
        if (_frames > 0)
        {
            var gap = now - _last;
            _longest = Math.Max(_longest, gap);
            if (gap >= LongGap && _stutters.Count < 5)
            {
                _stutters.Add($"+{_last:0} ms gap {gap:0} ({Perf.Around(_lastStamp, stamp)})");
            }
        }

        _last = now;
        _lastStamp = stamp;
        _frames++;
        Perf.FrameDrawn();
    }

    /// <summary>Our own drawing work for one frame, started at <paramref name="started"/> (<see cref="Perf.Now"/>).</summary>
    public void Work(long started)
    {
        var ms = Perf.Note($"{_name} frame", started);
        _work += ms;
        _worstWork = Math.Max(_worstWork, ms);
    }

    public void Report()
    {
        var ms = _clock.Elapsed.TotalMilliseconds;
        var rate = ms > 0 ? _frames * 1000 / ms : 0;
        var work = _frames > 0 && _work > 0 ? $", our work per frame avg {_work / _frames:0.0} max {_worstWork:0.0} ms" : string.Empty;
        var stutters = _stutters.Count > 0 ? $"; stutters: {string.Join("; ", _stutters)}" : string.Empty;
        foreach (var _ in _stutters)
        {
            Perf.Stutter();
        }

        TaskbarHost.Log($"fps {_name}: {_frames} frames in {ms:0} ms = {rate:0}/s (screen {RefreshHz()} Hz), longest gap {_longest:0} ms{work}{stutters}");
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
