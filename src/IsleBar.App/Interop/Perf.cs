using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;

namespace IsleBar.App.Interop;

/// <summary>
/// DIAGNOSTIC (performance, 10-12): finds what makes the animations drop frames. Logs only — nothing here changes what the bar does.
/// <list type="bullet">
/// <item>"slow tick / slow collect / slow lift open …": a timed piece of UI-thread work that took long, with each step's ms
/// (<see cref="Steps"/>; during an animation every one is logged, otherwise at most one per name every 5 s).</item>
/// <item>Each step of 2 ms or more is also remembered for a moment (<see cref="Note"/>), so a long gap between two animation frames
/// can say what the UI thread was busy with then (<see cref="FrameMeter"/>'s "stutters").</item>
/// <item>"ui stall": a watcher thread asks the UI thread to answer every 100 ms; an answer 50 ms or more late is logged with
/// whatever was timed in that span ("nothing timed" = something not measured yet: layout, an event handler…).</item>
/// <item>"perf summary" one minute after start, then every 10 minutes and once at exit: counts, the worst of each, CPU (all / UI
/// thread), GPU, battery, memory / handles (a slow leak shows as numbers that only grow), and the background watchers' work
/// (<see cref="Work"/>: which one costs most — the battery's view, they don't drop frames).</item>
/// </list>
/// </summary>
internal static class Perf
{
    private static readonly object Gate = new();

    private const int RingSize = 64;
    private static readonly (long At, double Ms, string What)[] Ring = new (long, double, string)[RingSize];
    private static int _next;

    /// <summary>When an animation last drew a frame (any of swap / lift / list).</summary>
    private static long _lastFrame;

    private static volatile bool _quiet;
    private static long _quietEnded;

    /// <summary>A modal menu holds the UI thread on purpose: not a stall (nor is a check sent while it was open).</summary>
    public static bool Quiet
    {
        get => _quiet;
        set
        {
            _quiet = value;
            if (!value)
            {
                Interlocked.Exchange(ref _quietEnded, Now);
            }
        }
    }

    private static bool Excused(long sent) => _quiet || sent <= Interlocked.Read(ref _quietEnded);

    // counters for the 10-minute summary
    private static int _ticks, _slowTicks, _stalls, _stutters;
    private static double _worstTick, _worstStall, _worstLog, _worstPixel;
    private static string _worstTickWhat = "-";

    public static long Now => Stopwatch.GetTimestamp();

    public static double Ms(long from) => Stopwatch.GetElapsedTime(from).TotalMilliseconds;

    public static double Ms(long from, long to) => Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;

    /// <summary>An animation is running: its frames came less than 50 ms ago.</summary>
    public static bool Animating => _lastFrame != 0 && Ms(_lastFrame) < 50;

    public static void FrameDrawn() => _lastFrame = Now;

    /// <summary>Remembers a step started at <paramref name="started"/> if it took 2 ms or more. No file write.</summary>
    public static double Note(string what, long started)
    {
        var ms = Ms(started);
        if (ms >= 2)
        {
            Remember(what, started, ms);
        }

        return ms;
    }

    private static void Remember(string what, long at, double ms)
    {
        lock (Gate)
        {
            Ring[_next] = (at, ms, what);
            _next = (_next + 1) % RingSize;
        }
    }

    /// <summary>The remembered steps that overlapped [<paramref name="from"/>, <paramref name="to"/>].</summary>
    public static string Around(long from, long to)
    {
        var hits = new List<(long At, double Ms, string What)>();
        lock (Gate)
        {
            foreach (var e in Ring)
            {
                if (e.What is null)
                {
                    continue;
                }

                var end = e.At + (long)(e.Ms * Stopwatch.Frequency / 1000);
                if (e.At <= to && end >= from)
                {
                    hits.Add(e);
                }
            }
        }

        return hits.Count == 0 ? "nothing timed" : string.Join(", ", hits.OrderBy(h => h.At).Select(h => $"{h.What} {h.Ms:0.0}"));
    }

    /// <summary>The log writer thread wrote a batch (off the UI thread since 10-12: only the summary's "slowest log write").</summary>
    public static void LogWritten(long started)
    {
        var ms = Ms(started);
        lock (Gate)
        {
            _worstLog = Math.Max(_worstLog, ms);
        }
    }

    public static void PixelRead(double ms)
    {
        lock (Gate)
        {
            _worstPixel = Math.Max(_worstPixel, ms);
        }
    }

    public static void Stutter()
    {
        lock (Gate)
        {
            _stutters++;
        }
    }

    private static readonly Dictionary<string, (int Count, double Total, double Max)> Works = new(StringComparer.Ordinal);

    /// <summary>One round of background work (a watcher's scan) started at <paramref name="started"/>: counted for the summary.</summary>
    public static void Work(string name, long started)
    {
        var ms = Ms(started);
        lock (Gate)
        {
            var w = Works.GetValueOrDefault(name);
            Works[name] = (w.Count + 1, w.Total + ms, Math.Max(w.Max, ms));
        }
    }

    // ---------------- timed pieces of work ----------------

    private static readonly Dictionary<string, (long At, int Skipped)> LastLogged = new(StringComparer.Ordinal);

    /// <summary>A piece of UI-thread work timed step by step: <c>var s = new Perf.Steps("tick"); … s.Mark("collect"); … s.End(4);</c></summary>
    public sealed class Steps(string what)
    {
        private readonly long _start = Now;
        private long _last = Now;
        private readonly StringBuilder _parts = new();
        private double _worst;
        private string _worstStep = "-";

        public void Mark(string step)
        {
            var now = Now;
            var ms = Ms(_last, now);
            if (ms >= 0.5)
            {
                _parts.Append($", {step} {ms:0.0}");
            }

            if (ms >= 2)
            {
                Remember($"{what}/{step}", _last, ms);
            }

            if (ms > _worst)
            {
                _worst = ms;
                _worstStep = step;
            }

            _last = now;
        }

        /// <summary>The whole so far (ms).</summary>
        public double Total => Ms(_start);

        /// <summary>"step ms, step ms, …" (steps under 0.5 ms left out).</summary>
        public string Parts => _parts.Length > 0 ? _parts.ToString(2, _parts.Length - 2) : "-";

        /// <summary>Logs "slow {what}: … ms = step ms, …" if the whole took <paramref name="threshold"/> ms or more.</summary>
        public void End(double threshold, string? note = null)
        {
            var total = Ms(_start);
            var tick = what == "tick";
            if (tick)
            {
                lock (Gate)
                {
                    _ticks++;
                    if (total >= threshold)
                    {
                        _slowTicks++;
                    }

                    if (total > _worstTick)
                    {
                        _worstTick = total;
                        _worstTickWhat = _worstStep;
                    }
                }
            }

            if (total < threshold)
            {
                return;
            }

            var animating = Animating;
            int skipped;
            lock (Gate)
            {
                var last = LastLogged.TryGetValue(what, out var l) ? l : (0, 0);
                if (!animating && last.At != 0 && Ms(last.At) < 5000)
                {
                    LastLogged[what] = (last.At, last.Skipped + 1);
                    return;
                }

                skipped = last.Skipped;
                LastLogged[what] = (Now, 0);
            }

            var parts = _parts.Length > 0 ? " =" + _parts.ToString(1, _parts.Length - 1) : string.Empty;
            AppLog.Write($"slow {what}: {total:0.0} ms{parts}{(animating ? " (during an animation)" : string.Empty)}"
                + $"{(note is null ? string.Empty : " " + note)}{(skipped > 0 ? $" (+{skipped} more slow ones since the last line)" : string.Empty)}");
        }
    }

    // ---------------- swallowed errors ----------------

    private static readonly Dictionary<string, long> FailLogged = new(StringComparer.Ordinal);

    /// <summary>An error something swallows on purpose (the tick must keep running): logged, the same one at most once a minute.</summary>
    public static void Failed(string where, Exception ex)
    {
        var key = $"{where}|{ex.GetType().Name}|{ex.Message}";
        lock (Gate)
        {
            if (FailLogged.TryGetValue(key, out var at) && Ms(at) < 60_000)
            {
                return;
            }

            FailLogged[key] = Now;
        }

        AppLog.Write($"{where} failed: {ex.ToString().Replace(Environment.NewLine, " | ")}");
    }

    // ---------------- UI thread watcher ----------------

    private static Thread? _watch;

    /// <summary>The UI thread's id (Windows'), for its own CPU time.</summary>
    private static uint _uiThread;

    /// <summary>
    /// Starts the thread that checks the UI thread answers in time, and writes the summaries. Call on the UI thread.
    /// </summary>
    public static void Watch(DispatcherQueue ui)
    {
        if (_watch is not null)
        {
            return;
        }

        _uiThread = GetCurrentThreadId();
        Sample.Take(out _last);
        _watch = new Thread(() => WatchLoop(ui)) { IsBackground = true, Name = "IsleBar perf watch" };
        _watch.Start();
    }

    /// <summary>The summary since the last one, now (at exit: the minutes since then aren't lost).</summary>
    public static void FinalSummary()
    {
        if (_watch is not null)
        {
            Summary("at exit");
        }
    }

    private static void WatchLoop(DispatcherQueue ui)
    {
        var summaryAt = Now;
        var first = true;
        using var answered = new ManualResetEventSlim();
        while (true)
        {
            Thread.Sleep(100);
            answered.Reset();
            var sent = Now;
            long got = 0;
            if (!ui.TryEnqueue(DispatcherQueuePriority.High, () =>
                {
                    got = Now;
                    answered.Set();
                }))
            {
                return;   // the app is closing
            }

            if (!answered.Wait(5000))
            {
                if (!Excused(sent))
                {
                    AppLog.Write($"ui stall: no answer for 5 s yet (busy with: {Around(sent, Now)})");
                }

                answered.Wait();
            }

            var delay = Ms(sent, got);
            if (delay >= 50 && !Excused(sent))
            {
                lock (Gate)
                {
                    _stalls++;
                    _worstStall = Math.Max(_worstStall, delay);
                }

                AppLog.Write($"ui stall: {delay:0} ms{(Animating ? " (during an animation)" : string.Empty)} (busy with: {Around(sent, got)})");
            }

            if (Ms(summaryAt) >= (first ? 60_000 : 10 * 60_000))
            {
                summaryAt = Now;
                Summary(first ? "first minute" : null);
                first = false;
            }
        }
    }

    private static readonly object SummaryGate = new();
    private static Sample _last;
    private static GpuMeter? _gpu;

    /// <summary>CPU times, the clock and the battery at one moment; two of them make the summary's averages.</summary>
    private readonly record struct Sample(long At, TimeSpan Cpu, TimeSpan UiCpu, int? Battery, bool Plugged)
    {
        public static void Take(out Sample sample)
        {
            var cpu = TimeSpan.Zero;
            var ui = TimeSpan.Zero;
            try
            {
                using var me = Process.GetCurrentProcess();
                cpu = me.TotalProcessorTime;
                foreach (ProcessThread thread in me.Threads)
                {
                    if (thread.Id == _uiThread)
                    {
                        ui = thread.TotalProcessorTime;
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // left at zero: the summary says "CPU unreadable"
            }

            var power = GetSystemPowerStatus(out var status);
            sample = new Sample(Now, cpu, ui, power && status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null, !power || status.ACLineStatus == 1);
        }
    }

    private static void Summary(string? label)
    {
        lock (SummaryGate)
        {
            SummaryNow(label);
        }
    }

    private static void SummaryNow(string? label)
    {
        Sample.Take(out var now);
        var from = _last;
        _last = now;
        var minutes = Ms(from.At, now.At) / 60_000;
        string cpu;
        if (now.Cpu > TimeSpan.Zero && minutes > 0)
        {
            var wall = Ms(from.At, now.At) * Environment.ProcessorCount;
            cpu = $"CPU {(now.Cpu - from.Cpu).TotalMilliseconds * 100 / wall:0.00}% of the whole PC (UI thread {(now.UiCpu - from.UiCpu).TotalMilliseconds * 100 / wall:0.00}%, "
                + $"{(now.Cpu - from.Cpu).TotalSeconds:0.0} s of CPU time in all)";
        }
        else
        {
            cpu = "CPU unreadable";
        }

        string gpu;
        try
        {
            _gpu ??= new GpuMeter();
            gpu = _gpu.Read();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            gpu = "GPU unreadable";
        }

        var battery = now.Battery is { } b
            ? $"battery {from.Battery?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}% → {b}% ({(now.Plugged ? "plugged in" : "on battery")}; the whole PC's, not ours alone)"
            : "no battery";

        string counts;
        string works;
        lock (Gate)
        {
            counts = $"ticks {_ticks}, slow (4 ms+) {_slowTicks}, worst {_worstTick:0.0} ms ({_worstTickWhat}); ui stalls {_stalls}, worst {_worstStall:0} ms; "
                + $"animation stutters {_stutters}; slowest pixel read {_worstPixel:0.0} ms; slowest log write {_worstLog:0.0} ms";
            _ticks = _slowTicks = _stalls = _stutters = 0;
            _worstTick = _worstStall = _worstLog = _worstPixel = 0;
            _worstTickWhat = "-";
            works = Works.Count == 0 ? "-" : string.Join(", ", Works.OrderByDescending(w => w.Value.Total).Take(10)
                .Select(w => $"{w.Key} {w.Value.Count}× avg {w.Value.Total / w.Value.Count:0.0} max {w.Value.Max:0} ms"));
            Works.Clear();
        }

        string memory;
        try
        {
            using var me = Process.GetCurrentProcess();
            var handle = me.Handle;
            memory = $"working set {me.WorkingSet64 >> 20} MB, private {me.PrivateMemorySize64 >> 20} MB, managed {GC.GetTotalMemory(false) >> 20} MB, "
                + $"handles {me.HandleCount}, threads {me.Threads.Count}, GDI {GetGuiResources(handle, 0)}, USER {GetGuiResources(handle, 1)}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            memory = $"memory unreadable ({ex.Message})";
        }

        AppLog.Write($"perf summary ({minutes:0.0} min{(label is null ? string.Empty : ", " + label)}): {cpu}; {gpu}; {battery}; {memory}; {counts}; "
            + $"background work (most total time first): {works}");
    }

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
