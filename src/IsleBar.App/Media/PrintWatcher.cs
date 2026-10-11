using System.Runtime.InteropServices;
using IsleBar.App.Interop;
using IsleBar.Core.Island;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.Media;

/// <summary>
/// The printer on the island (TODO 四, 10-11): Windows' print queue, read through the spooler's own API (winspool) — the steadiest
/// source. One card, key "print", all its stages: "打印中 {document} +N" with pages done / pages when the driver says them; something
/// stopping it (paper out, a jam, offline, toner, a door open…) → that word in orange, pinned until it's dealt with (like the battery
/// at 5%); left the queue printed → "已打印 {document}" (<see cref="PrintTally"/>). Printers that make files (PDF, XPS, OneNote, fax)
/// show nothing. EXPERIMENTAL past the normal flow: the user has no printer, only Print to PDF to try with.
/// <para>
/// The queue is read on a task of its own every 2 s (a network printer that's away may keep the spooler waiting; the download
/// thread mustn't wait with it). Idle cost: one list of printers and one queue per printer. Test switch: while
/// <c>%LOCALAPPDATA%\IsleBar\print-virtual</c> exists, file-making printers count too. Log "print: …".
/// </para>
/// </summary>
internal sealed class PrintWatcher
{
    private const string Key = "print";
    private const string PrinterGlyph = NoticeGlyphs.Printer;
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(2);
    private static readonly string VirtualSwitch = Path.Combine(AppPaths.DataDirectory, "print-virtual");

    private readonly Func<Core.Localization.LanguageStrings> _text;
    private readonly PrintTally _tally = new();
    private readonly List<ActivityState> _finished = [];
    private DateTimeOffset _since;
    private DateTimeOffset _nextRead;
    private Task<List<PrintJobLook>?>? _reading;
    private List<PrintJobLook> _jobs = [];
    private string _said = string.Empty;

    public PrintWatcher(Func<Core.Localization.LanguageStrings> text) => _text = text;

    public List<ActivityState> Scan(DateTimeOffset now)
    {
        if (_reading is { IsCompleted: true } done)
        {
            if (done.Result is { } jobs)
            {
                Took(jobs, now);
            }

            _reading = null;
        }

        if (_reading is null && now >= _nextRead)
        {
            _nextRead = now + Every;
            var virtuals = File.Exists(VirtualSwitch);
            _reading = Task.Run(() => Read(virtuals));
        }

        var result = new List<ActivityState>();
        if (Card(now) is { } card)
        {
            result.Add(card);
        }

        _finished.RemoveAll(f => now - f.UpdatedAt > ActivityStore.DoneLinger);
        result.AddRange(_finished);
        return result;
    }

    private void Took(List<PrintJobLook> jobs, DateTimeOffset now)
    {
        if (_jobs.Count == 0 && jobs.Count > 0)
        {
            _since = now;
        }

        foreach (var printed in _tally.See(jobs))
        {
            _finished.RemoveAll(f => f.SourcePath == Key);
            _finished.Add(new ActivityState
            {
                RawKind = ActivityState.KindTransfer,
                Title = "Files",
                Name = printed.Document,
                AppName = printed.Printer,   // the lifted card is headed by the printer (user 10-11)
                Took = now - _since,
                Words = _text().Printed,
                Stage = "copy",
                State = "done",
                SourcePath = Key,   // the same card as the one in progress
                UpdatedAt = now,
            });
            AppLog.Write($"print: printed \"{printed.Document}\" on {printed.Printer} (status 0x{printed.Status:X}, pages {printed.PagesPrinted}/{printed.TotalPages})");
        }

        // the bits as they come, to check the experimental ones against a real printer later
        var said = string.Join("; ", jobs.Select(j => $"{j.Printer} #{j.Id} \"{j.Document}\" job 0x{j.Status:X} printer 0x{j.PrinterStatus:X} pages {j.PagesPrinted}/{j.TotalPages}"));
        if (said != _said)
        {
            _said = said;
            AppLog.Write($"print: {(jobs.Count == 0 ? "queue empty" : said)}");
        }

        _jobs = jobs;
    }

    private ActivityState? Card(DateTimeOffset now)
    {
        if (_jobs.Count == 0)
        {
            return null;
        }

        _finished.RemoveAll(f => f.SourcePath == Key);   // printing again: the card is this one
        var text = _text();
        var first = _jobs[0];
        var stuck = _jobs.Select(j => (Job: j, Problem: PrintRules.ProblemOf(j.Status, j.PrinterStatus))).FirstOrDefault(j => j.Problem != PrintProblem.None);
        if (stuck.Job is { } job)
        {
            // someone has to go to the printer: pinned and orange till it's dealt with (as the battery at 5%)
            var notice = SystemNotice.Make(Words(stuck.Problem, text), PrinterGlyph, _since, msg: job.Document, urgent: true);
            notice.Stage = SystemNotice.CriticalStage;
            notice.SourcePath = Key;
            notice.AppName = job.Printer;
            if (PrintRules.Pages(job) is { } stopped)
            {
                notice.Note = PagesOf(stopped, text);
                notice.Done = stopped.Done;   // the lifted card's bar stands where it stopped
                notice.Total = stopped.Total;
            }

            return notice;
        }

        var pages = _jobs.Count == 1 ? PrintRules.Pages(first) : null;
        return new ActivityState
        {
            RawKind = ActivityState.KindTransfer,
            Title = "Files",
            Name = first.Document,
            AppName = first.Printer,
            Note = pages is { } shown ? PagesOf(shown, text) : null,
            MoreCount = _jobs.Count - 1,
            Words = PrintRules.Paused(first.Status, first.PrinterStatus) ? text.Paused : text.Printing,
            Glyph = PrinterGlyph,
            Stage = "copy",
            Done = pages?.Done,
            Total = pages?.Total,   // no pages to believe (or several jobs): the bar flows
            State = "run",
            SourcePath = Key,
            UpdatedAt = _since,
        };
    }

    /// <summary>"已打印3页，共12页" — only pages Windows can be believed on (<see cref="PrintRules.Pages"/>).</summary>
    private static string PagesOf((int Done, int Total) pages, Core.Localization.LanguageStrings text)
        => text.CardPagesOf.Replace("{a}", pages.Done.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{b}", pages.Total.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string Words(PrintProblem problem, Core.Localization.LanguageStrings text) => problem switch
    {
        PrintProblem.PaperJam => text.PrintPaperJam,
        PrintProblem.PaperOut => text.PrintPaperOut,
        PrintProblem.Toner => text.PrintToner,
        PrintProblem.Offline => text.PrintOffline,
        PrintProblem.Attention => text.PrintAttention,
        _ => text.PrintError,
    };

    // ---------------- the spooler ----------------

    /// <summary>Every job on the printers that print on paper (all printers with the test switch). Null when the spooler can't be read.</summary>
    private static List<PrintJobLook>? Read(bool virtuals)
    {
        try
        {
            var jobs = new List<PrintJobLook>();
            foreach (var name in Printers())
            {
                if (!OpenPrinter(name, out var printer, IntPtr.Zero))
                {
                    continue;
                }

                try
                {
                    var queue = Jobs(printer);
                    if (queue.Count == 0)
                    {
                        continue;
                    }

                    var (status, port, driver) = Info(printer);
                    if (!virtuals && PrintRules.IsVirtual(port, driver))
                    {
                        continue;
                    }

                    jobs.AddRange(queue.Select(j => new PrintJobLook(name, j.JobId, j.Document ?? string.Empty, j.Status, status, (int)j.PagesPrinted, (int)j.TotalPages)));
                }
                finally
                {
                    ClosePrinter(printer);
                }
            }

            return jobs;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static List<string> Printers()
    {
        const uint flags = 0x2 | 0x4;   // PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS
        EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinters(flags, null, 4, buffer, needed, out _, out var count))
            {
                return [];
            }

            var size = Marshal.SizeOf<PrinterInfo4>();
            return [.. Enumerable.Range(0, (int)count).Select(i => Marshal.PtrToStructure<PrinterInfo4>(buffer + (i * size)).PrinterName).OfType<string>()];
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static List<JobInfo1> Jobs(IntPtr printer)
    {
        EnumJobs(printer, 0, 64, 1, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumJobs(printer, 0, 64, 1, buffer, needed, out _, out var count))
            {
                return [];
            }

            var size = Marshal.SizeOf<JobInfo1>();
            return [.. Enumerable.Range(0, (int)count).Select(i => Marshal.PtrToStructure<JobInfo1>(buffer + (i * size)))];
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (uint Status, string Port, string Driver) Info(IntPtr printer)
    {
        GetPrinter(printer, 2, IntPtr.Zero, 0, out var needed);
        if (needed == 0)
        {
            return (0, string.Empty, string.Empty);
        }

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!GetPrinter(printer, 2, buffer, needed, out _))
            {
                return (0, string.Empty, string.Empty);
            }

            var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
            return (info.Status, info.PortName ?? string.Empty, info.DriverName ?? string.Empty);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo4
    {
        public string? PrinterName;
        public string? ServerName;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JobInfo1
    {
        public uint JobId;
        public string? PrinterName;
        public string? MachineName;
        public string? UserName;
        public string? Document;
        public string? Datatype;
        public string? StatusText;
        public uint Status;
        public uint Priority;
        public uint Position;
        public uint TotalPages;
        public uint PagesPrinted;
        public SystemTime Submitted;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo2
    {
        public string? ServerName;
        public string? PrinterName;
        public string? ShareName;
        public string? PortName;
        public string? DriverName;
        public string? Comment;
        public string? Location;
        public IntPtr DevMode;
        public string? SepFile;
        public string? PrintProcessor;
        public string? Datatype;
        public string? Parameters;
        public IntPtr SecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint Jobs;
        public uint AveragePpm;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumJobs(IntPtr printer, uint first, uint count, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPrinter(IntPtr printer, uint level, IntPtr buffer, uint size, out uint needed);
}
