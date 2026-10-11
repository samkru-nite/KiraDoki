namespace IsleBar.Core.SystemWatch;

/// <summary>One job in a printer's queue (Windows' print spooler, JOB_INFO_1), with its printer's own status (PRINTER_INFO_2).</summary>
/// <param name="Status">JOB_STATUS_* bits.</param>
/// <param name="PrinterStatus">PRINTER_STATUS_* bits.</param>
public sealed record PrintJobLook(string Printer, uint Id, string Document, uint Status, uint PrinterStatus, int PagesPrinted, int TotalPages);

/// <summary>What stops a printer, as the card says it (most pressing first).</summary>
public enum PrintProblem
{
    None,
    PaperJam,
    PaperOut,
    Toner,
    Offline,
    Attention,
    Error,
}

/// <summary>
/// The printer card's rules (TODO 四, 10-11; user: doesn't print, "write it anyway"). From the spooler's own records, the steadiest
/// source. Only the normal flow could be tried (Microsoft Print to PDF); the problems are Windows' documented bits, EXPERIMENTAL.
/// </summary>
public static class PrintRules
{
    // JOB_STATUS_*
    public const uint JobPaused = 0x1;
    public const uint JobError = 0x2;
    public const uint JobDeleting = 0x4;
    public const uint JobSpooling = 0x8;
    public const uint JobPrinting = 0x10;
    public const uint JobOffline = 0x20;
    public const uint JobPaperOut = 0x40;
    public const uint JobPrinted = 0x80;
    public const uint JobDeleted = 0x100;
    public const uint JobBlocked = 0x200;
    public const uint JobUserIntervention = 0x400;
    public const uint JobComplete = 0x1000;

    // PRINTER_STATUS_*
    public const uint PrinterPaused = 0x1;
    public const uint PrinterError = 0x2;
    public const uint PrinterPaperJam = 0x8;
    public const uint PrinterPaperOut = 0x10;
    public const uint PrinterPaperProblem = 0x40;
    public const uint PrinterOffline = 0x80;
    public const uint PrinterOutputBinFull = 0x800;
    public const uint PrinterNotAvailable = 0x1000;
    public const uint PrinterNoToner = 0x40000;
    public const uint PrinterUserIntervention = 0x100000;
    public const uint PrinterDoorOpen = 0x400000;

    /// <summary>What stops this job, from its own bits and its printer's.</summary>
    public static PrintProblem ProblemOf(uint job, uint printer) => true switch
    {
        _ when (printer & PrinterPaperJam) != 0 => PrintProblem.PaperJam,
        _ when (job & JobPaperOut) != 0 || (printer & (PrinterPaperOut | PrinterPaperProblem)) != 0 => PrintProblem.PaperOut,
        _ when (printer & PrinterNoToner) != 0 => PrintProblem.Toner,
        _ when (job & JobOffline) != 0 || (printer & (PrinterOffline | PrinterNotAvailable)) != 0 => PrintProblem.Offline,
        _ when (job & (JobUserIntervention | JobBlocked)) != 0
               || (printer & (PrinterUserIntervention | PrinterDoorOpen | PrinterOutputBinFull)) != 0 => PrintProblem.Attention,
        _ when (job & JobError) != 0 || (printer & PrinterError) != 0 => PrintProblem.Error,
        _ => PrintProblem.None,
    };

    /// <summary>
    /// Pages printed / pages, only when they can be believed: not while the program is still sending pages (Print to PDF, 10-11:
    /// the total is then the pages sent so far, 0/1 → 0/39 …) and never more printed than there are (after sending: 229/71, 298/2).
    /// </summary>
    public static (int Done, int Total)? Pages(PrintJobLook job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return (job.Status & JobSpooling) == 0 && job.TotalPages > 0 && job.PagesPrinted <= job.TotalPages ? (job.PagesPrinted, job.TotalPages) : null;
    }

    public static bool Paused(uint job, uint printer) => (job & JobPaused) != 0 || (printer & PrinterPaused) != 0;

    /// <summary>A printer that makes a file, not paper: Microsoft Print to PDF / XPS, OneNote, fax — no card for those.</summary>
    public static bool IsVirtual(string port, string driver)
    {
        var p = port.Trim().ToUpperInvariant();
        return p is "PORTPROMPT:" or "NUL:" or "FILE:" or "SHRFAX:" || p.StartsWith("MICROSOFT.OFFICE.ONENOTE", StringComparison.Ordinal)
               || driver.Contains("Print To PDF", StringComparison.OrdinalIgnoreCase) || driver.Contains("XPS Document Writer", StringComparison.OrdinalIgnoreCase)
               || driver.Contains("OneNote", StringComparison.OrdinalIgnoreCase) || driver.Contains("Fax", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Follows the jobs across looks and tells which ones went through when they leave the queue: the spooler marked it printed, or it
/// was printing and nothing said it was cancelled or failed (drivers often drop a job the moment it's done without marking it —
/// EXPERIMENTAL, a guess the user allowed for the printer: a wrong "已打印" costs little). Cancelled / failed → no word.
/// </summary>
public sealed class PrintTally
{
    private readonly Dictionary<(string, uint), Seen> _jobs = [];

    private sealed class Seen(PrintJobLook look)
    {
        public PrintJobLook Last { get; set; } = look;
        public bool Printed { get; set; }
        public bool Printing { get; set; }
        public bool Spoiled { get; set; }
    }

    /// <summary>Takes this look; returns the jobs that left the queue printed.</summary>
    public List<PrintJobLook> See(IReadOnlyList<PrintJobLook> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var present = new HashSet<(string, uint)>();
        foreach (var job in jobs)
        {
            var key = (job.Printer, job.Id);
            present.Add(key);
            if (!_jobs.TryGetValue(key, out var seen))
            {
                seen = _jobs[key] = new Seen(job);
            }

            seen.Last = job;
            seen.Printed |= (job.Status & (PrintRules.JobPrinted | PrintRules.JobComplete)) != 0;   // not by its pages: see Pages
            seen.Printing |= (job.Status & PrintRules.JobPrinting) != 0 || job.PagesPrinted > 0;
            seen.Spoiled |= (job.Status & (PrintRules.JobDeleting | PrintRules.JobDeleted | PrintRules.JobError)) != 0;
        }

        var printed = new List<PrintJobLook>();
        foreach (var (key, seen) in _jobs.Where(j => !present.Contains(j.Key)).ToList())
        {
            _jobs.Remove(key);
            if (seen.Printed || (seen.Printing && !seen.Spoiled))
            {
                printed.Add(seen.Last);
            }
        }

        return printed;
    }
}
