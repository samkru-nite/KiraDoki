using IsleBar.Core.SystemWatch;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>The printer card's rules (Windows' documented spooler bits; only Print to PDF to try with, 10-11).</summary>
public class PrintingTests
{
    private static PrintJobLook Job(uint status, uint printer = 0, int done = 0, int total = 0, uint id = 7)
        => new("HP LaserJet", id, "报告.docx", status, printer, done, total);

    [Fact]
    public void What_stops_a_printer_most_pressing_first()
    {
        Assert.Equal(PrintProblem.None, PrintRules.ProblemOf(PrintRules.JobPrinting, 0));
        Assert.Equal(PrintProblem.PaperOut, PrintRules.ProblemOf(PrintRules.JobPaperOut, 0));
        Assert.Equal(PrintProblem.PaperJam, PrintRules.ProblemOf(PrintRules.JobError, PrintRules.PrinterPaperJam | PrintRules.PrinterError));
        Assert.Equal(PrintProblem.Offline, PrintRules.ProblemOf(0, PrintRules.PrinterOffline));
        Assert.Equal(PrintProblem.Toner, PrintRules.ProblemOf(0, PrintRules.PrinterNoToner));
        Assert.Equal(PrintProblem.Attention, PrintRules.ProblemOf(0, PrintRules.PrinterDoorOpen));
        Assert.Equal(PrintProblem.Error, PrintRules.ProblemOf(PrintRules.JobError, 0));
    }

    [Fact]
    public void Pages_are_believed_only_when_they_add_up()
    {
        // Print to PDF, 10-11: while sending, the total is the pages sent so far; after it, printed and "total" crossed over
        Assert.Null(PrintRules.Pages(Job(0x2018, done: 0, total: 39)));
        Assert.Null(PrintRules.Pages(Job(0x2010, done: 229, total: 71)));
        Assert.Equal((3, 10), PrintRules.Pages(Job(PrintRules.JobPrinting, done: 3, total: 10)));
        Assert.Null(PrintRules.Pages(Job(PrintRules.JobPrinting)));   // no pages: the bar flows
    }

    [Fact]
    public void Printers_that_make_files_are_left_out()
    {
        Assert.True(PrintRules.IsVirtual("PORTPROMPT:", "Microsoft Print To PDF"));
        Assert.True(PrintRules.IsVirtual("nul:", "Send to Microsoft OneNote 16 Driver"));
        Assert.True(PrintRules.IsVirtual("Microsoft.Office.OneNoteVirtualPrinter_8wekyb3d8bbwe_onenote-psa:print-to-onenote_S-1-5", "Microsoft Software Printer Driver"));
        Assert.False(PrintRules.IsVirtual("USB001", "HP LaserJet Pro M404"));
        Assert.False(PrintRules.IsVirtual("WSD-0f3c", "Canon G3010 series"));
    }

    [Fact]
    public void A_job_marked_printed_or_seen_printing_is_told_when_it_leaves()
    {
        var tally = new PrintTally();
        tally.See([Job(PrintRules.JobPrinting, done: 1, total: 3)]);
        Assert.Single(tally.See([]));

        tally.See([Job(PrintRules.JobPrinted, id: 8)]);
        Assert.Single(tally.See([]));
    }

    [Fact]
    public void Cancelled_failed_or_never_printing_says_nothing()
    {
        var tally = new PrintTally();
        tally.See([Job(PrintRules.JobPrinting)]);
        tally.See([Job(PrintRules.JobPrinting | PrintRules.JobDeleting)]);
        Assert.Empty(tally.See([]));

        tally.See([Job(0, id: 9)]);   // only spooled, gone before it printed
        Assert.Empty(tally.See([]));
    }
}
