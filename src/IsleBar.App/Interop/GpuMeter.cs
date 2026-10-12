using System.Runtime.InteropServices;

namespace IsleBar.App.Interop;

/// <summary>
/// DIAGNOSTIC (performance, 10-12): this process's GPU use, from the same performance counters Task Manager reads ("GPU Engine",
/// "GPU Process Memory"). <see cref="Read"/> gives the average since the previous call: per engine type (3D, Copy, VideoDecode…) the
/// engines are added up, and the busiest type counts — as Task Manager's GPU column does. Called from the perf watcher thread
/// every few minutes; a query takes some tens of ms there, nothing on the UI thread. Note: drawing our windows on screen is the
/// desktop compositor's (dwm.exe) work and is counted there, not here.
/// </summary>
internal sealed class GpuMeter : IDisposable
{
    private IntPtr _query;
    private IntPtr _engines;
    private IntPtr _dedicated;
    private IntPtr _shared;

    public GpuMeter()
    {
        var pid = Environment.ProcessId;
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
        {
            _query = IntPtr.Zero;
            return;
        }

        // a wildcard instance is matched again at each collection: engines that appear later are counted too
        PdhAddEnglishCounter(_query, $@"\GPU Engine(pid_{pid}_*)\Utilization Percentage", IntPtr.Zero, out _engines);
        PdhAddEnglishCounter(_query, $@"\GPU Process Memory(pid_{pid}_*)\Dedicated Usage", IntPtr.Zero, out _dedicated);
        PdhAddEnglishCounter(_query, $@"\GPU Process Memory(pid_{pid}_*)\Shared Usage", IntPtr.Zero, out _shared);
        PdhCollectQueryData(_query);   // the first sample: a rate needs two
    }

    /// <summary>"GPU 0.4% (3D), GPU memory 45 MB dedicated + 12 MB shared" since the last call, or why it can't be read.</summary>
    public string Read()
    {
        if (_query == IntPtr.Zero)
        {
            return "GPU unreadable";
        }

        var collected = PdhCollectQueryData(_query);
        if (collected != 0)
        {
            return $"GPU unreadable (0x{collected:X})";
        }

        var byType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in Values(_engines, PdhFmtDouble))
        {
            var at = name.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
            var type = at >= 0 ? name[(at + 8)..] : "?";
            byType[type] = byType.GetValueOrDefault(type) + value;
        }

        var busiest = byType.Count == 0 ? default : byType.MaxBy(t => t.Value);
        var dedicated = Values(_dedicated, PdhFmtDouble).Sum(v => v.Value);
        var shared = Values(_shared, PdhFmtDouble).Sum(v => v.Value);
        return $"GPU {busiest.Value:0.0}%{(busiest.Key is null ? string.Empty : $" ({busiest.Key})")}, "
            + $"GPU memory {dedicated / (1 << 20):0} MB dedicated + {shared / (1 << 20):0} MB shared";
    }

    private static List<(string Name, double Value)> Values(IntPtr counter, uint format)
    {
        var values = new List<(string, double)>();
        if (counter == IntPtr.Zero)
        {
            return values;
        }

        uint size = 0;
        var status = PdhGetFormattedCounterArray(counter, format, ref size, out _, IntPtr.Zero);
        if (status != PdhMoreData || size == 0)
        {
            return values;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, format, ref size, out var count, buffer) != 0)
            {
                return values;
            }

            var itemSize = Marshal.SizeOf<CounterItem>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + (i * itemSize));
                if (item.Status is 0 or 1)   // valid / new data
                {
                    values.Add((Marshal.PtrToStringUni(item.Name) ?? string.Empty, item.Value));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return values;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;

    /// <summary>PDH_FMT_COUNTERVALUE_ITEM_W with a double value.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public IntPtr Name;
        public uint Status;
        public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr items);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
