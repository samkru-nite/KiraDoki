using System.Runtime.InteropServices;

namespace IsleBar.App.Interop;

/// <summary>
/// How many bytes processes have written to disk (Windows keeps the count per process). Used where the files don't show how far a
/// transfer is: Baidu Netdisk reserves each download at full size up front, and Explorer creates copied files at full size
/// (measured 10-04). Reading one process's count takes microseconds; the process list is refreshed only every few seconds.
/// </summary>
internal sealed class ProcessWriteMeter(string processName)
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(5);

    private readonly Dictionary<int, long> _last = [];
    private int[] _pids = [];
    private DateTimeOffset _refreshAt;

    /// <summary>Bytes written by every process of that name since the last call (0 on the first call, which sets the baseline).</summary>
    public long Delta(DateTimeOffset now)
    {
        if (now >= _refreshAt)
        {
            _refreshAt = now + RefreshEvery;
            _pids = Pids(processName);
        }

        long delta = 0;
        var seen = new HashSet<int>();
        foreach (var pid in _pids)
        {
            if (Written(pid) is not { } total)
            {
                continue;
            }

            seen.Add(pid);
            if (_last.TryGetValue(pid, out var before) && total >= before)
            {
                delta += total - before;
            }

            _last[pid] = total;
        }

        foreach (var gone in _last.Keys.Where(pid => !seen.Contains(pid)).ToList())
        {
            _last.Remove(gone);
        }

        return delta;
    }

    /// <summary>Forgets the baseline — the next <see cref="Delta"/> starts over (used when nothing is being followed).</summary>
    public void Reset()
    {
        _last.Clear();
        _refreshAt = default;
    }

    private static int[] Pids(string name)
    {
        var list = System.Diagnostics.Process.GetProcessesByName(name);
        var pids = list.Select(p => p.Id).ToArray();
        foreach (var process in list)
        {
            process.Dispose();
        }

        return pids;
    }

    /// <summary>Total bytes the process has written so far; null when it can't be read (gone, or not ours).</summary>
    public static long? Written(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return GetProcessIoCounters(handle, out var counters) ? (long)counters.WriteTransferCount : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
